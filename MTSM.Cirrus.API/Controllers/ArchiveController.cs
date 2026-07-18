using Microsoft.AspNetCore.Mvc;
using MTSM.Cirrus.API.Contracts.Requests;
using MTSM.Cirrus.API.Contracts.Responses;
using MTSM.Cirrus.API.Mapping;
using MTSM.Cirrus.Core.Abstractions;
using MTSM.Cirrus.Core.Models;
using MTSM.Cirrus.Core.Services;

namespace MTSM.Cirrus.API.Controllers;

[ApiController]
[Route("api/archive")]
[Produces("application/json")]
public sealed class ArchiveController : ControllerBase
{
    private const string ActorHeaderName = "X-Actor";

    private readonly IArchiveService _archiveService;
    private readonly ILogger<ArchiveController> _logger;

    public ArchiveController(
        IArchiveService archiveService,
        ILogger<ArchiveController> logger)
    {
        _archiveService = archiveService;
        _logger = logger;
    }

    /// <summary>
    /// Archives a file and its metadata.
    /// </summary>
    [HttpPost]
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
        [FromForm] ArchiveUploadRequest request,
        CancellationToken cancellationToken)
    {
        if (request.File.Length <= 0)
        {
            ModelState.AddModelError(
                nameof(request.File),
                "The uploaded file must not be empty.");

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
                        reference.BusinessType.Trim(),
                        reference.Tenant.Trim()))
                .ToArray();

        await using Stream content =
            request.File.OpenReadStream();

        var archiveRequest =
            new MTSM.Cirrus.Core.Models.ArchiveFileRequest
            {
                Content = content,

                OriginalFilename =
                    GetSafeOriginalFilename(
                        request.File.FileName),

                FileType =
                    request.FileType.Trim(),

                MimeType =
                    NormalizeOptionalValue(
                        request.File.ContentType),

                SourceSystem =
                    request.SourceSystem.Trim(),

                Partner =
                    NormalizeOptionalValue(
                        request.Partner),

                Tenant =
                    request.Tenant.Trim(),

                ReceivedAt =
                    receivedAt,

                CreatedBy =
                    request.CreatedBy.Trim(),

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

        return CreatedAtAction(
            nameof(GetMetadataAsync),
            new
            {
                archiveObjectId =
                    response.ArchiveObjectId
            },
            response);
    }

    /// <summary>
    /// Downloads an archived file.
    /// </summary>
    [HttpGet("{archiveObjectId:long}")]
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
        [FromRoute] long archiveObjectId,
        [FromHeader(Name = ActorHeaderName)] string? actor,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(actor))
        {
            ModelState.AddModelError(
                ActorHeaderName,
                $"The HTTP header '{ActorHeaderName}' is required.");

            return ValidationProblem(ModelState);
        }

        ArchiveDownloadResult result =
            await _archiveService.DownloadAsync(
                archiveObjectId,
                actor.Trim(),
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
    /// Returns the metadata of an archive object.
    /// </summary>
    [HttpGet("{archiveObjectId:long}/metadata")]
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
            [FromRoute] long archiveObjectId,
            CancellationToken cancellationToken)
    {
        ArchiveMetadataResult? metadata =
            await _archiveService.GetMetadataAsync(
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
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ExistsAsync(
        [FromRoute] long archiveObjectId,
        CancellationToken cancellationToken)
    {
        ArchiveMetadataResult? metadata =
            await _archiveService.GetMetadataAsync(
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

    private static string GetSafeOriginalFilename(
        string filename)
    {
        string safeFilename =
            Path.GetFileName(filename);

        if (string.IsNullOrWhiteSpace(safeFilename))
        {
            throw new ArgumentException(
                "The uploaded file has no valid filename.",
                nameof(filename));
        }

        return safeFilename;
    }

    private static string? NormalizeOptionalValue(
        string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }
}