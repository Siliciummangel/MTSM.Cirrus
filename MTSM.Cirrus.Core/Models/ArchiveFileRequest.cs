using System;
using System.Collections.Generic;
using System.Text;

namespace MTSM.Cirrus.Core.Models;

public sealed class ArchiveFileRequest
{
    public required Stream Content { get; init; }

    public required string OriginalFilename { get; init; }

    public required string FileType { get; init; }

    public string? MimeType { get; init; }

    public required string SourceSystem { get; init; }

    public string? Partner { get; init; }

    public required string Tenant { get; init; }

    public DateTimeOffset ReceivedAt { get; init; }

    public required string CreatedBy { get; init; }

    public int? RetentionPolicyId { get; init; }

    public DateOnly? RetentionUntil { get; init; }

    public IReadOnlyCollection<ArchiveBusinessReferenceInput>
        BusinessReferences { get; init; }
        = Array.Empty<ArchiveBusinessReferenceInput>();
}