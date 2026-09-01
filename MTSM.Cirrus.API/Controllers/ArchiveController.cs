using Microsoft.AspNetCore.Mvc;
using MTSM.Cirrus.API.Contracts.Requests;
using MTSM.Cirrus.API.Contracts.Responses;
using MTSM.Cirrus.API.Mapping;
using MTSM.Cirrus.API.Security;
using MTSM.Cirrus.Core.Abstractions;
using MTSM.Cirrus.Core.Models;
using Microsoft.AspNetCore.Authorization;
using MTSM.Cirrus.API.Filters;
using MTSM.Cirrus.API.Uploads;

namespace MTSM.Cirrus.API.Controllers;

[ApiController]
[Authorize]
[Route("api/tenants/{tenantId:long}/archive")]
public sealed class ArchiveController : ControllerBase
{
    private readonly IArchiveService _archiveService;
    private readonly IArchiveUploadReader _uploadReader;
    private readonly ICirrusIdentityAccessor _identityAccessor;
    private readonly ILogger<ArchiveController> _logger;

    public ArchiveController(
        IArchiveService archiveService,
        ICirrusIdentityAccessor identityAccessor,
        ILogger<ArchiveController> logger,
        IArchiveUploadReader uploadReader)
    {
        _archiveService = archiveService;
        _identityAccessor = identityAccessor;
        _logger = logger;
        _uploadReader = uploadReader;
    }

    /// <summary>
    /// Archives a file and its metadata.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = CirrusAuthorizationPolicies.Write)]
    [DisableFormValueModelBinding]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<ArchiveFileResponse>(
        StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ArchiveFileResponse>> ArchiveAsync(
        [FromRoute] long tenantId,
        CancellationToken cancellationToken)
    {
        ArchiveUpload upload = await _uploadReader.ReadAsync(
            Request,
            cancellationToken);
        ArchiveUploadMetadataRequest request = upload.Metadata;

        if (!TryValidateModel(request, nameof(request)))
        {
            return ValidationProblem(ModelState);
        }

        DateTimeOffset receivedAt =
            request.ReceivedAt
            ?? DateTimeOffset.UtcNow;

        ArchiveBusinessReferenceInput[] businessReferences =
            request.BusinessReferences
                .Select(reference =>
                    new ArchiveBusinessReferenceInput(
                        reference.BusinessReferenceTypeId,
                        reference.ReferenceValue.Trim(),
                        reference.BusinessType.Trim()))
                .ToArray();

        await using Stream content =
            upload.Content;

        var archiveRequest =
            new MTSM.Cirrus.Core.Models.ArchiveFileRequest
            {
                Content = content,

                OriginalFilename =
                    upload.OriginalFilename,

                FileType =
                    request.FileType.Trim(),

                MimeType =
                    NormalizeOptionalValue(
                        upload.ContentType),

                SourceSystem =
                    request.SourceSystem.Trim(),

                Partner =
                    NormalizeOptionalValue(
                        request.Partner),

                TenantId = tenantId,

                ReceivedAt =
                    receivedAt,

                CreatedBy = _identityAccessor.GetRequiredIdentity().Actor,

                RetentionPolicyId =
                    request.RetentionPolicyId,

                RetentionUntil =
                    request.RetentionUntil,

                BusinessReferences =
                    businessReferences
            };

        _logger.LogInformation(
            "Received archive request for file {FileName} " +
            "from source system {SourceSystem}.",
            archiveRequest.OriginalFilename,
            archiveRequest.SourceSystem);

        ArchiveFileResult result =
            await _archiveService.ArchiveAsync(
                archiveRequest,
                cancellationToken);

        ArchiveFileResponse response =
            ArchiveResponseMapper.Map(result);

        return CreatedAtRoute(
            "GetArchiveMetadata",
            new
                {
                    tenantId,
                    archiveObjectId =
                    response.ArchiveObjectId
            },
            response);
    }

    /// <summary>
    /// Downloads an archived file.
    /// </summary>
    [HttpGet("{archiveObjectId:long}")]
    [Authorize(Policy = CirrusAuthorizationPolicies.Read)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DownloadAsync(
        [FromRoute] long tenantId,
        [FromRoute] long archiveObjectId,
        CancellationToken cancellationToken)
    {
        ArchiveDownloadResult result =
            await _archiveService.DownloadAsync(
                tenantId,
                archiveObjectId,
                _identityAccessor.GetRequiredIdentity().Actor,
                cancellationToken);

        string contentType =
            string.IsNullOrWhiteSpace(result.MimeType)
                ? "application/octet-stream"
                : result.MimeType;

        Response.Headers.ETag =
            $"\"{result.Sha256Hash}\"";

        Response.Headers.Append(
            "X-Archive-Object-Id",
            result.ArchiveObjectId.ToString());

        Response.Headers.Append(
            "X-Content-SHA256",
            result.Sha256Hash);

        return File(
            result.Content,
            contentType,
            result.OriginalFilename,
            enableRangeProcessing: true);
    }

    /// <summary>
    /// Requests the logical deletion of an archive object.
    /// </summary>
    /// <remarks>
    /// This operation does not immediately remove the object from object storage.
    /// The archive object is marked as DeletionRequested and will be processed
    /// asynchronously by a background worker.
    /// </remarks>
    [HttpDelete("{archiveObjectId:long}")]
    [Authorize(Policy = CirrusAuthorizationPolicies.Delete)]
    [ProducesResponseType<ArchiveDeletionRequestResponse>(
        StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ArchiveDeletionRequestResponse>>
        RequestDeletionAsync(
            [FromRoute] long tenantId,
            [FromRoute] long archiveObjectId,
            CancellationToken cancellationToken)
    {
        ArchiveDeletionRequestResult result =
            await _archiveService.RequestDeletionAsync(
                tenantId,
                archiveObjectId,
                _identityAccessor.GetRequiredIdentity().Actor,
                cancellationToken);

        ArchiveDeletionRequestResponse response =
            ArchiveResponseMapper.Map(result);

        return AcceptedAtRoute(
            "GetArchiveMetadata",
            new
                {
                    tenantId,
                    archiveObjectId =
                    response.ArchiveObjectId
            },
            response);
    }

    /// <summary>
    /// Searches archive metadata using optional filters and pagination.
    /// </summary>
    [HttpGet("search")]
    [Authorize(Policy = CirrusAuthorizationPolicies.Read)]
    [ProducesResponseType<ArchiveSearchResponse>(
        StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ArchiveSearchResponse>> SearchAsync(
        [FromRoute] long tenantId,
        [FromQuery] ArchiveSearchQuery query,
        CancellationToken cancellationToken)
    {
        var request = new ArchiveSearchRequest
        {
            ArchiveObjectId = query.ArchiveObjectId,
            FileType = NormalizeOptionalValue(query.FileType),
            SourceSystem = NormalizeOptionalValue(query.SourceSystem),
            Partner = NormalizeOptionalValue(query.Partner),
            OriginalFilename = NormalizeOptionalValue(query.OriginalFilename),
            Sha256Hash = NormalizeOptionalValue(query.Sha256Hash),
            ArchiveStatus = query.ArchiveStatus,
            ReceivedFrom = query.ReceivedFrom,
            ReceivedUntil = query.ReceivedUntil,
            ArchivedFrom = query.ArchivedFrom,
            ArchivedUntil = query.ArchivedUntil,
            BusinessReferenceTypeId = query.BusinessReferenceTypeId,
            BusinessReferenceValue =
                NormalizeOptionalValue(query.BusinessReferenceValue),
            BusinessType = NormalizeOptionalValue(query.BusinessType),
            PageNumber = query.PageNumber,
            PageSize = query.PageSize
        };

        ArchiveSearchResult result =
            await _archiveService.SearchAsync(
                tenantId,
                request,
                cancellationToken);

        return Ok(ArchiveResponseMapper.Map(result));
    }

    /// <summary>
    /// Verifies the integrity of an archived file.
    /// </summary>
    /// <remarks>
    /// Reads the complete storage object, recalculates its SHA-256 hash and
    /// size, compares both values with the stored metadata and records an
    /// integrity event. A completed verification returns 200 OK even when
    /// the comparison fails; inspect isValid for the result.
    /// </remarks>
    [HttpPost("{archiveObjectId:long}/verify-integrity")]
    [Authorize(Policy = CirrusAuthorizationPolicies.Verify)]
    [ProducesResponseType<ArchiveIntegrityResponse>(
        StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ArchiveIntegrityResponse>>
        VerifyIntegrityAsync(
            [FromRoute] long tenantId,
            [FromRoute] long archiveObjectId,
            CancellationToken cancellationToken)
    {
        ArchiveIntegrityResult result =
            await _archiveService.VerifyIntegrityAsync(
                tenantId,
                archiveObjectId,
                _identityAccessor.GetRequiredIdentity().Actor,
                cancellationToken);

        return Ok(ArchiveResponseMapper.Map(result));
    }

    /// <summary>
    /// Returns scheduling and execution status for integrity checks.
    /// </summary>
    [HttpGet("{archiveObjectId:long}/integrity-status")]
    [Authorize(Policy = CirrusAuthorizationPolicies.Read)]
    [ProducesResponseType<ArchiveIntegrityStatusResponse>(
        StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ArchiveIntegrityStatusResponse>>
        GetIntegrityStatusAsync(
            [FromRoute] long tenantId,
            [FromRoute] long archiveObjectId,
            CancellationToken cancellationToken)
    {
        ArchiveIntegrityStatusResult? result =
            await _archiveService.GetIntegrityStatusAsync(
                tenantId,
                archiveObjectId,
                cancellationToken);

        if (result is null)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Archive object not found",
                detail:
                    $"Archive object {archiveObjectId} does not exist.",
                type: "https://httpstatuses.com/404",
                instance: HttpContext.Request.Path);
        }

        return Ok(ArchiveResponseMapper.Map(result));
    }

    /// <summary>
    /// Returns the metadata of an archive object.
    /// </summary>
    [HttpGet("{archiveObjectId:long}/metadata",
        Name = "GetArchiveMetadata")]
    [Authorize(Policy = CirrusAuthorizationPolicies.Read)]
    [ProducesResponseType<ArchiveMetadataResponse>(
        StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ArchiveMetadataResponse>>
        GetMetadataAsync(
            [FromRoute] long tenantId,
            [FromRoute] long archiveObjectId,
            CancellationToken cancellationToken)
    {
        ArchiveMetadataResult? metadata =
            await _archiveService.GetMetadataAsync(
                tenantId,
                archiveObjectId,
                cancellationToken);

        if (metadata is null)
        {
            return Problem(
                statusCode:
                    StatusCodes.Status404NotFound,

                title:
                    "Archive object not found",

                detail:
                    $"Archive object {archiveObjectId} does not exist.",

                type:
                    "https://httpstatuses.com/404",

                instance:
                    HttpContext.Request.Path);
        }

        ArchiveMetadataResponse response =
            ArchiveResponseMapper.Map(metadata);

        return Ok(response);
    }

    /// <summary>
    /// Checks whether an archive object exists.
    /// </summary>
    [HttpHead("{archiveObjectId:long}")]
    [Authorize(Policy = CirrusAuthorizationPolicies.Read)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ExistsAsync(
        [FromRoute] long tenantId,
        [FromRoute] long archiveObjectId,
        CancellationToken cancellationToken)
    {
        ArchiveMetadataResult? metadata =
            await _archiveService.GetMetadataAsync(
                tenantId,
                archiveObjectId,
                cancellationToken);

        if (metadata is null)
        {
            return NotFound();
        }

        Response.ContentType =
            metadata.MimeType
            ?? "application/octet-stream";

        Response.ContentLength =
            metadata.SizeBytes;

        if (!string.IsNullOrWhiteSpace(
                metadata.Sha256Hash))
        {
            Response.Headers.ETag =
                $"\"{metadata.Sha256Hash}\"";

            Response.Headers.Append(
                "X-Content-SHA256",
                metadata.Sha256Hash);
        }

        Response.Headers.Append(
            "X-Archive-Status",
            metadata.ArchiveStatus.ToString());

        if (metadata.ArchivedAt.HasValue)
        {
            Response.Headers.LastModified =
                metadata.ArchivedAt.Value.ToString("R");
        }

        return Ok();
    }

    private static string? NormalizeOptionalValue(
        string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }
}
