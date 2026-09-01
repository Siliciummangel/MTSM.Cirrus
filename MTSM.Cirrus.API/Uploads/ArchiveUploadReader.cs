using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using MTSM.Cirrus.API.Config;
using MTSM.Cirrus.API.Contracts.Requests;
using MTSM.Cirrus.API.Streams;
using System.Text.Json;

namespace MTSM.Cirrus.API.Uploads;

public sealed class ArchiveUploadReader(
    IOptions<ApiOptions> apiOptions) : IArchiveUploadReader
{
    private readonly ApiOptions _apiOptions = apiOptions.Value;

    public async Task<ArchiveUpload> ReadAsync(
        HttpRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        MultipartReader reader = CreateMultipartReader(request);
        MultipartSection metadataSection =
            await ReadRequiredSectionAsync(reader, "metadata", cancellationToken);
        ArchiveUploadMetadataRequest metadata =
            await ReadMetadataAsync(metadataSection, cancellationToken);
        MultipartSection fileSection =
            await ReadRequiredSectionAsync(reader, "file", cancellationToken);
        ContentDispositionHeaderValue fileDisposition =
            GetContentDisposition(fileSection);
        string originalFilename = GetSafeOriginalFilename(
            HeaderUtilities.RemoveQuotes(fileDisposition.FileNameStar).Value
            ?? HeaderUtilities.RemoveQuotes(fileDisposition.FileName).Value
            ?? string.Empty);

        var firstByte = new byte[1];
        int bytesRead = await fileSection.Body.ReadAsync(
            firstByte,
            cancellationToken);

        if (bytesRead == 0)
        {
            throw new ArgumentException("The uploaded file must not be empty.");
        }

        return new ArchiveUpload(
            metadata,
            new PrefixReadStream(firstByte[0], fileSection.Body),
            originalFilename,
            fileSection.ContentType);
    }

    private MultipartReader CreateMultipartReader(HttpRequest request)
    {
        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var contentType)
            || !contentType.MediaType.Equals(
                "multipart/form-data",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Content-Type must be multipart/form-data.");
        }

        string boundary = HeaderUtilities.RemoveQuotes(contentType.Boundary).Value
            ?? string.Empty;

        if (string.IsNullOrWhiteSpace(boundary))
        {
            throw new ArgumentException("The multipart boundary is missing.");
        }

        if (boundary.Length > FormOptions.DefaultMultipartBoundaryLengthLimit)
        {
            throw new ArgumentException("The multipart boundary is too long.");
        }

        return new MultipartReader(boundary, request.Body)
        {
            BodyLengthLimit = _apiOptions.MaxMultipartUploadSizeBytes
        };
    }

    private static async Task<MultipartSection> ReadRequiredSectionAsync(
        MultipartReader reader,
        string expectedName,
        CancellationToken cancellationToken)
    {
        MultipartSection? section =
            await reader.ReadNextSectionAsync(cancellationToken);

        if (section is null)
        {
            throw new ArgumentException(
                $"The multipart section '{expectedName}' is missing.");
        }

        ContentDispositionHeaderValue disposition = GetContentDisposition(section);
        string name = HeaderUtilities.RemoveQuotes(disposition.Name).Value
            ?? string.Empty;

        if (!name.Equals(expectedName, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Expected multipart section '{expectedName}', but received '{name}'. " +
                "The metadata section must precede the file section.");
        }

        return section;
    }

    private static ContentDispositionHeaderValue GetContentDisposition(
        MultipartSection section)
    {
        if (!ContentDispositionHeaderValue.TryParse(
                section.ContentDisposition,
                out var disposition)
            || !disposition.DispositionType.Equals(
                "form-data",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Every multipart section must use form-data content disposition.");
        }

        return disposition;
    }

    private async Task<ArchiveUploadMetadataRequest> ReadMetadataAsync(
        MultipartSection section,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(
                section.ContentType?.Split(';', 2)[0].Trim(),
                "application/json",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The metadata section must have Content-Type application/json.");
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[8192];

        while (true)
        {
            int read = await section.Body.ReadAsync(chunk, cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (buffer.Length + read > _apiOptions.MaxUploadMetadataSizeBytes)
            {
                throw new ArgumentException(
                    "The metadata section exceeds the configured size limit of " +
                    $"{_apiOptions.MaxUploadMetadataSizeBytes} bytes.");
            }

            buffer.Write(chunk, 0, read);
        }

        try
        {
            return JsonSerializer.Deserialize<ArchiveUploadMetadataRequest>(
                       buffer.ToArray(),
                       new JsonSerializerOptions(JsonSerializerDefaults.Web))
                   ?? throw new ArgumentException("The metadata section must contain JSON.");
        }
        catch (JsonException exception)
        {
            throw new ArgumentException(
                "The metadata section contains invalid JSON.",
                exception);
        }
    }

    private static string GetSafeOriginalFilename(string filename)
    {
        string safeFilename = Path.GetFileName(filename);

        if (string.IsNullOrWhiteSpace(safeFilename))
        {
            throw new ArgumentException(
                "The uploaded file has no valid filename.",
                nameof(filename));
        }

        return safeFilename;
    }
}
