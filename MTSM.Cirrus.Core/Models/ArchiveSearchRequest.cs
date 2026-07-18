using MTSM.Cirrus.Core.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace MTSM.Cirrus.Core.Models;

public sealed class ArchiveSearchRequest
{
    public long? ArchiveObjectId { get; init; }

    public string? Tenant { get; init; }

    public string? FileType { get; init; }

    public string? SourceSystem { get; init; }

    public string? Partner { get; init; }

    public string? OriginalFilename { get; init; }

    public string? Sha256Hash { get; init; }

    public ArchiveStatus? ArchiveStatus { get; init; }

    public DateTimeOffset? ReceivedFrom { get; init; }

    public DateTimeOffset? ReceivedUntil { get; init; }

    public DateTimeOffset? ArchivedFrom { get; init; }

    public DateTimeOffset? ArchivedUntil { get; init; }

    public int? BusinessReferenceTypeId { get; init; }

    public string? BusinessReferenceValue { get; init; }

    public string? BusinessType { get; init; }

    public int PageNumber { get; init; } = 1;

    public int PageSize { get; init; } = 50;
}