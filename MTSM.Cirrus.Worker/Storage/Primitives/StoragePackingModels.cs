using MTSM.Cirrus.Core.Entities;

namespace MTSM.Cirrus.Worker.StorageV2;

internal sealed record StoragePackingWorkItem(long ArchiveObjectId);
internal sealed record PlannedChunk(int Sequence, long Offset, int Length, string Hash, long? ExistingId);
internal sealed record ArchivePackPlan(long ArchiveObjectId, List<PlannedChunk> Chunks);
internal sealed record PackChunkCandidate(string Hash, int Length, long PackId, PackEntry Entry);
internal sealed record PendingPackChunk(string Hash, int Length, PackEntry Entry);
internal sealed record UploadedPack(long PackId, IReadOnlyList<PackChunkCandidate> Candidates);
internal sealed record RegisteredChunk(long Id, bool Inserted);
internal sealed record PackingBatch(string LeaseOwner, ArchiveObject[] Items);
