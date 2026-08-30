namespace MTSM.Cirrus.Worker.StorageV2;

public sealed record ContentChunkData(
    int SequenceNumber,
    long OriginalOffset,
    byte[] Bytes);
