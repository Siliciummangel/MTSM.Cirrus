using Microsoft.AspNetCore.Mvc;
using MTSM.Cirrus.API.Contracts.Responses;
using MTSM.Cirrus.Core.Enums;
using MTSM.Cirrus.Core.Exceptions;
using MTSM.Cirrus.Core.Models;
using MTSM.Cirrus.Core.Tests.TestInfrastructure;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace MTSM.Cirrus.Core.Tests;

public sealed class ArchiveApiIntegrationTests
{
    [Fact]
    public async Task ArchiveAsync_ValidMultipartRequestReturnsCreatedResource()
    {
        using var factory = new ApiTestFactory();
        using HttpClient client = factory.CreateClient();
        using var content = new MultipartFormDataContent
        {
            { new ByteArrayContent("payload"u8.ToArray()), "file", "payload.txt" },
            { new StringContent("document"), "fileType" },
            { new StringContent("source-a"), "sourceSystem" }
        };

        HttpResponseMessage response = await client.PostAsync(
            "/api/tenants/1/archive",
            content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(
            "/api/tenants/1/archive/42/metadata",
            response.Headers.Location?.AbsolutePath);
        JsonDocument? body =
            await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.NotNull(body);
        Assert.Equal(42, body.RootElement.GetProperty("archiveObjectId").GetInt64());
        Assert.Equal(1, body.RootElement.GetProperty("tenantId").GetInt64());
        Assert.Equal(
            new string('a', 64),
            body.RootElement.GetProperty("sha256Hash").GetString());
        Assert.Equal(7, body.RootElement.GetProperty("sizeBytes").GetInt64());
        Assert.True(body.RootElement.TryGetProperty("archivedAt", out _));
        Assert.False(body.RootElement.TryGetProperty("objectKey", out _));
        Assert.Equal("payload.txt", factory.ArchiveService.LastArchiveRequest?.OriginalFilename);
        Assert.Equal("source-a", factory.ArchiveService.LastArchiveRequest?.SourceSystem);
    }

    [Fact]
    public async Task DownloadAsync_ReturnsContentAndArchiveHeaders()
    {
        using var factory = new ApiTestFactory();
        using HttpClient client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/tenants/1/archive/42");
        request.Headers.Add("X-Actor", "attacker-controlled");

        HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("payload", await response.Content.ReadAsStringAsync());
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"", response.Headers.ETag?.Tag);
        Assert.Equal("42", response.Headers.GetValues("X-Archive-Object-Id").Single());
        Assert.Equal("apikey:machine:1", factory.ArchiveService.LastActor);
    }

    [Fact]
    public async Task SearchAsync_ReturnsMappedResult()
    {
        using var factory = new ApiTestFactory();
        factory.ArchiveService.SearchHandler = (request, _) =>
        {
            Assert.Equal(2, request.PageNumber);
            return Task.FromResult(new ArchiveSearchResult([], 2, 25, 0, 0));
        };
        using HttpClient client = factory.CreateClient();

        ArchiveSearchResponse? response = await client.GetFromJsonAsync<ArchiveSearchResponse>(
            "/api/tenants/1/archive/search?pageNumber=2&pageSize=25");
        Assert.Equal(1, factory.ArchiveService.LastTenantId);

        Assert.NotNull(response);
        Assert.Equal(2, response.PageNumber);
        Assert.Equal(25, response.PageSize);
        Assert.Empty(response.Items);
    }

    [Fact]
    public async Task DeleteAsync_WithActorReturnsAcceptedResource()
    {
        using var factory = new ApiTestFactory();
        using HttpClient client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Delete, "/api/tenants/1/archive/42");

        HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("/api/tenants/1/archive/42/metadata", response.Headers.Location?.AbsolutePath);
        JsonDocument? body = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.NotNull(body);
        Assert.Equal(
            "DeletionRequested",
            body.RootElement.GetProperty("archiveStatus").GetString());
        Assert.True(body.RootElement.GetProperty("stateChanged").GetBoolean());
    }

    [Fact]
    public async Task VerifyIntegrityAsync_WithActorReturnsVerificationResult()
    {
        using var factory = new ApiTestFactory();
        using HttpClient client = factory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/tenants/1/archive/42/verify-integrity");

        HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        ArchiveIntegrityResponse? body =
            await response.Content.ReadFromJsonAsync<ArchiveIntegrityResponse>();
        Assert.True(body?.IsValid);
        Assert.Equal(42, body?.ArchiveObjectId);
        Assert.Equal("apikey:machine:1", factory.ArchiveService.LastActor);
    }

    [Fact]
    public async Task IntegrityStatusAsync_ReturnsSchedulingState()
    {
        using var factory = new ApiTestFactory();
        DateTimeOffset nextCheckAt = DateTimeOffset.Parse("2026-08-20T00:00:00Z");
        factory.ArchiveService.IntegrityStatusHandler = (id, _) =>
            Task.FromResult<ArchiveIntegrityStatusResult?>(
                new ArchiveIntegrityStatusResult(
                    id,
                    null,
                    null,
                    null,
                    nextCheckAt,
                    false,
                    null,
                    null));
        using HttpClient client = factory.CreateClient();

        ArchiveIntegrityStatusResponse? response =
            await client.GetFromJsonAsync<ArchiveIntegrityStatusResponse>(
                "/api/tenants/1/archive/42/integrity-status");

        Assert.NotNull(response);
        Assert.Equal(42, response.ArchiveObjectId);
        Assert.Equal(nextCheckAt, response.NextCheckAt);
        Assert.False(response.IsCheckInProgress);
    }

    [Fact]
    public async Task MetadataAndHeadAsync_ReturnMetadataAndHeaders()
    {
        using var factory = new ApiTestFactory();
        factory.ArchiveService.MetadataHandler = (id, _) =>
            Task.FromResult<ArchiveMetadataResult?>(CreateMetadata(id));
        using HttpClient client = factory.CreateClient();

        JsonDocument? metadata = await client.GetFromJsonAsync<JsonDocument>(
            "/api/tenants/1/archive/42/metadata");
        using var headRequest = new HttpRequestMessage(
            HttpMethod.Head,
            "/api/tenants/1/archive/42");
        HttpResponseMessage headResponse = await client.SendAsync(headRequest);

        Assert.NotNull(metadata);
        Assert.Equal(
            42,
            metadata.RootElement.GetProperty("archiveObjectId").GetInt64());
        Assert.Equal(
            "payload.txt",
            metadata.RootElement.GetProperty("originalFilename").GetString());
        Assert.Equal(
            "Completed",
            metadata.RootElement.GetProperty("storageProcessingStatus").GetString());
        Assert.Equal(HttpStatusCode.OK, headResponse.StatusCode);
        Assert.Equal(7, headResponse.Content.Headers.ContentLength);
        Assert.Equal("Active", headResponse.Headers.GetValues("X-Archive-Status").Single());
    }

    [Fact]
    public async Task MetadataAsync_MissingObjectReturnsProblemDetails()
    {
        using var factory = new ApiTestFactory();
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(
            "/api/tenants/1/archive/404/metadata");

        await AssertProblemAsync(
            response,
            HttpStatusCode.NotFound,
            "Archive object not found",
            "/api/tenants/1/archive/404/metadata");
    }

    [Fact]
    public async Task ArchiveEndpoint_WithoutAuthenticationReturnsUnauthorized()
    {
        using var factory = new ApiTestFactory();
        using HttpClient client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/tenants/1/archive/42");
        request.Headers.Add("X-Test-Anonymous", "true");
        HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ArchiveEndpoint_WithoutRequiredPermissionReturnsForbidden()
    {
        using var factory = new ApiTestFactory();
        using HttpClient client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Delete, "/api/tenants/1/archive/42");
        request.Headers.Add("X-Test-Permissions", "archive.read");

        HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(factory.ArchiveService.LastTenantId);
    }

    public static TheoryData<HttpMethod, string> ProtectedArchiveEndpoints => new()
    {
        { HttpMethod.Post, "/api/tenants/1/archive" },
        { HttpMethod.Get, "/api/tenants/1/archive/42" },
        { HttpMethod.Get, "/api/tenants/1/archive/42/metadata" },
        { HttpMethod.Head, "/api/tenants/1/archive/42" },
        { HttpMethod.Get, "/api/tenants/1/archive/search" },
        { HttpMethod.Get, "/api/tenants/1/archive/42/integrity-status" },
        { HttpMethod.Post, "/api/tenants/1/archive/42/verify-integrity" },
        { HttpMethod.Delete, "/api/tenants/1/archive/42" }
    };

    [Theory]
    [MemberData(nameof(ProtectedArchiveEndpoints))]
    public async Task EveryArchiveEndpoint_WithoutPermissionReturnsForbidden(HttpMethod method, string path)
    {
        using var factory = new ApiTestFactory();
        using HttpClient client = factory.CreateClient();
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Test-Permissions", " ");

        HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ArchiveEndpoint_WithForeignRouteTenantReturnsNotFound()
    {
        using var factory = new ApiTestFactory();
        using HttpClient client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/tenants/2/archive/42/metadata");
        request.Headers.Add("X-Test-Tenant", "1");

        HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(factory.ArchiveService.LastTenantId);
    }

    [Theory]
    [InlineData("not-found", HttpStatusCode.NotFound, "Archive object not found")]
    [InlineData("unavailable", HttpStatusCode.Conflict, "Archive object unavailable")]
    [InlineData("invalid", HttpStatusCode.BadRequest, "Invalid request")]
    [InlineData("conflict", HttpStatusCode.Conflict, "Operation could not be completed")]
    [InlineData("archive", HttpStatusCode.InternalServerError, "Archive operation failed")]
    [InlineData("unexpected", HttpStatusCode.InternalServerError, "Internal server error")]
    public async Task VerifyIntegrityAsync_MapsExceptionsToProblemDetails(
        string exceptionType,
        HttpStatusCode expectedStatus,
        string expectedTitle)
    {
        using var factory = new ApiTestFactory();
        factory.ArchiveService.IntegrityHandler = (_, _, _) =>
            throw CreateException(exceptionType);
        using HttpClient client = factory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/tenants/1/archive/42/verify-integrity");

        HttpResponseMessage response = await client.SendAsync(request);

        ProblemDetails problem = await AssertProblemAsync(
            response,
            expectedStatus,
            expectedTitle,
            "/api/tenants/1/archive/42/verify-integrity");
        if ((int)expectedStatus >= 500)
        {
            Assert.DoesNotContain("sensitive provider detail", problem.Detail);
        }
        Assert.True(problem.Extensions.TryGetValue("traceId", out object? traceId));
        Assert.False(string.IsNullOrWhiteSpace(traceId?.ToString()));
    }

    [Fact]
    public async Task UnknownRouteReturnsNotFound()
    {
        using var factory = new ApiTestFactory();
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/unknown");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<ProblemDetails> AssertProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedTitle,
        string expectedInstance)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        ProblemDetails? problem =
            await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal((int)expectedStatus, problem.Status);
        Assert.Equal(expectedTitle, problem.Title);
        Assert.Equal(expectedInstance, problem.Instance);
        return problem;
    }

    private static Exception CreateException(string exceptionType) =>
        exceptionType switch
        {
            "not-found" => new ArchiveObjectNotFoundException(42),
            "unavailable" => new ArchiveObjectUnavailableException(
                42,
                ArchiveStatus.DeletionRequested),
            "invalid" => new ArgumentException("invalid input"),
            "conflict" => new InvalidOperationException("conflicting operation"),
            "archive" => new ArchiveException("sensitive provider detail"),
            _ => new Exception("sensitive provider detail")
        };

    private static ArchiveMetadataResult CreateMetadata(long archiveObjectId) =>
        new(
            archiveObjectId,
            1,
            "objects/42",
            "cirrus-test",
            "document",
            "text/plain",
            "source-a",
            null,
            "payload.txt",
            new string('a', 64),
            7,
            DateTimeOffset.Parse("2026-08-18T00:00:00Z"),
            DateTimeOffset.Parse("2026-08-19T00:00:00Z"),
            new DateOnly(2036, 8, 19),
            null,
            ArchiveStatus.Active,
            StorageProcessingStatus.Completed,
            null,
            null,
            null,
            null,
            null,
            false,
            "api-user",
            [],
            []);
}
