using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MTSM.Cirrus.Core.Abstractions;
using MTSM.Cirrus.Core.Data;
using MTSM.Cirrus.Core.Entities;
using MTSM.Cirrus.Worker.StorageV2;
using System.Security.Cryptography;
using ZstdSharp;

namespace MTSM.Cirrus.Worker;

public sealed class ArchivePackPlanner(
    IServiceScopeFactory scopeFactory,
    IOptions<StorageProcessingOptions> options,
    IContentChunker chunker,
    PackWriter packWriter,
    ManifestCommitter committer)
{
    private readonly StorageProcessingOptions _options = options.Value;

    public async Task PackAndCommitAsync(ArchiveObject[] items, string lease, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IObjectStorage storage = scope.ServiceProvider.GetRequiredService<IObjectStorage>();
        CirrusDbContext db = scope.ServiceProvider.GetRequiredService<CirrusDbContext>();
        var profile = new ChunkingProfile("FastCDC", 1, _options.MinimumChunkSizeBytes,
            _options.AverageChunkSizeBytes, _options.MaximumChunkSizeBytes, true);
        var existing = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var plans = new List<ArchivePackPlan>();
        var candidates = new Dictionary<string, PackChunkCandidate>(StringComparer.OrdinalIgnoreCase);
        var pending = new List<PendingPackChunk>();
        var packIds = new List<long>();
        TemporaryPackBuilder? builder = null;

        async Task FlushAsync()
        {
            if (builder is null || builder.Length == 0) return;
            TemporaryPackBuilder current = builder;
            builder = null;
            UploadedPack uploaded = await packWriter.UploadAsync(storage, db, items[0], current, pending, cancellationToken);
            packIds.Add(uploaded.PackId);
            foreach (PackChunkCandidate candidate in uploaded.Candidates)
                candidates[candidate.Hash] = candidate;
            pending.Clear();
        }

        foreach (ArchiveObject item in items.OrderBy(x => x.ArchiveObjectId))
        {
            var plan = new ArchivePackPlan(item.ArchiveObjectId, []);
            plans.Add(plan);
            await using Stream source = await storage.OpenReadAsync(item.BucketName, item.StagingObjectKey!, cancellationToken);
            await foreach (ContentChunkData chunk in chunker.ChunkAsync(source, profile, cancellationToken))
            {
                string hash = Convert.ToHexString(SHA256.HashData(chunk.Bytes)).ToLowerInvariant();
                if (!existing.ContainsKey(hash) && !candidates.ContainsKey(hash) && !pending.Any(x => x.Hash == hash))
                {
                    long id = await db.ContentChunks.AsNoTracking()
                        .Where(x => x.TenantId == item.TenantId && x.HashAlgorithm == "SHA-256" && x.ChunkHash == hash)
                        .Select(x => x.ContentChunkId).SingleOrDefaultAsync(cancellationToken);
                    if (id != 0) existing[hash] = id;
                }
                if (existing.TryGetValue(hash, out long existingId))
                {
                    plan.Chunks.Add(new(chunk.SequenceNumber, chunk.OriginalOffset, chunk.Bytes.Length, hash, existingId));
                    continue;
                }
                if (!candidates.ContainsKey(hash) && !pending.Any(x => x.Hash == hash))
                {
                    builder ??= new TemporaryPackBuilder();
                    if (builder.Length > 0 && builder.Length + chunk.Bytes.Length > _options.TargetPackSizeBytes)
                    { await FlushAsync(); builder = new TemporaryPackBuilder(); }
                    byte[] stored;
                    using (var compressor = new Compressor(_options.ZstdCompressionLevel))
                        stored = compressor.Wrap(chunk.Bytes).ToArray();
                    PackEntry entry = await builder.AppendAsync(stored, chunk.Bytes.Length, cancellationToken);
                    pending.Add(new PendingPackChunk(hash, chunk.Bytes.Length, entry));
                    if (builder.Length >= _options.TargetPackSizeBytes) await FlushAsync();
                }
                plan.Chunks.Add(new(chunk.SequenceNumber, chunk.OriginalOffset, chunk.Bytes.Length, hash, null));
            }
        }
        await FlushAsync();
        await committer.CommitAsync(db, items, plans, candidates, packIds, profile, lease, cancellationToken);
    }
}
