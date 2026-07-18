using MTSM.Cirrus.Core.Enums;
using System.Text.Json;

namespace MTSM.Cirrus.Core.Entities;

public sealed class ArchiveEvent
{
    public long ArchiveEventId { get; set; }

    public long ArchiveObjectId { get; set; }

    public ArchiveEventType EventType { get; set; }

    public DateTimeOffset EventTimestamp { get; set; }

    public required string Actor { get; set; }

    public JsonDocument? DetailsJson { get; set; }

    public ArchiveObject ArchiveObject { get; set; } = null!;
}