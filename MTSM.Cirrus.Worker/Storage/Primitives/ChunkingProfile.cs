namespace MTSM.Cirrus.Worker.StorageV2;

public sealed record ChunkingProfile(
    string Algorithm,
    int AlgorithmVersion,
    int MinimumSizeBytes,
    int AverageSizeBytes,
    int MaximumSizeBytes,
    bool Normalized);
