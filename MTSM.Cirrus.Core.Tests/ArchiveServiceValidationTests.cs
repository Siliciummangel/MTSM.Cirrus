using Microsoft.EntityFrameworkCore;
using MTSM.Cirrus.Core.Data;
using MTSM.Cirrus.Core.Models;
using MTSM.Cirrus.Core.Tests.TestInfrastructure;

namespace MTSM.Cirrus.Core.Tests;

public sealed class ArchiveServiceValidationTests
{
    [Theory]
    [InlineData("", "type", "source", "tenant", "actor")]
    [InlineData("file.txt", " ", "source", "tenant", "actor")]
    [InlineData("file.txt", "type", "\r\n", "tenant", "actor")]
    [InlineData("file.txt", "type", "source", "tenant", "\u0001")]
    [InlineData("folder/file.txt", "type", "source", "tenant", "actor")]
    public async Task ArchiveAsync_RejectsInvalidRequiredMetadata(
        string filename,
        string fileType,
        string sourceSystem,
        string tenant,
        string actor)
    {
        await using CirrusDbContext dbContext = CreateDisconnectedContext();
        var service = CoreTestFactory.CreateService(dbContext, new InMemoryObjectStorage());
        ArchiveFileRequest request = CreateRequest(
            filename,
            fileType,
            sourceSystem,
            tenant,
            actor);

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => service.ArchiveAsync(request));
    }

    [Fact]
    public async Task ArchiveAsync_RejectsRetentionBeforeReceipt()
    {
        await using CirrusDbContext dbContext = CreateDisconnectedContext();
        var service = CoreTestFactory.CreateService(dbContext, new InMemoryObjectStorage());
        ArchiveFileRequest request = CreateRequest(
            retentionUntil: new DateOnly(2025, 12, 31));

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.ArchiveAsync(request));
    }

    [Theory]
    [InlineData(0, 50)]
    [InlineData(1, 0)]
    [InlineData(1, 501)]
    [InlineData(int.MaxValue, 500)]
    public async Task SearchAsync_RejectsInvalidPagination(int pageNumber, int pageSize)
    {
        await using CirrusDbContext dbContext = CreateDisconnectedContext();
        var service = CoreTestFactory.CreateService(dbContext, new InMemoryObjectStorage());

        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            service.SearchAsync(new ArchiveSearchRequest
            {
                PageNumber = pageNumber,
                PageSize = pageSize
            }));
    }

    [Fact]
    public async Task ArchiveAsync_RejectsDuplicateNormalizedBusinessReferences()
    {
        await using CirrusDbContext dbContext = CreateDisconnectedContext();
        var service = CoreTestFactory.CreateService(dbContext, new InMemoryObjectStorage());
        ArchiveFileRequest request = new()
        {
            Content = new MemoryStream("content"u8.ToArray()),
            OriginalFilename = "file.txt",
            FileType = "invoice",
            SourceSystem = "erp",
            Tenant = "tenant-a",
            ReceivedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            CreatedBy = "test-suite",
            RetentionUntil = new DateOnly(2036, 1, 1),
            BusinessReferences =
            [
                new ArchiveBusinessReferenceInput(1, "REF-1", "invoice", "tenant-a"),
                new ArchiveBusinessReferenceInput(1, " REF-1 ", " invoice ", " tenant-a ")
            ]
        };

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.ArchiveAsync(request));
    }

    [Fact]
    public async Task SearchAsync_RejectsInvalidSha256BeforeDatabaseAccess()
    {
        await using CirrusDbContext dbContext = CreateDisconnectedContext();
        var service = CoreTestFactory.CreateService(dbContext, new InMemoryObjectStorage());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SearchAsync(new ArchiveSearchRequest
            {
                Sha256Hash = "not-a-sha256"
            }));
    }

    private static CirrusDbContext CreateDisconnectedContext()
    {
        var options = new DbContextOptionsBuilder<CirrusDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=unused_test;Username=unused;Password=unused")
            .UseSnakeCaseNamingConvention()
            .Options;

        return new CirrusDbContext(options);
    }

    private static ArchiveFileRequest CreateRequest(
        string filename = "file.txt",
        string fileType = "invoice",
        string sourceSystem = "erp",
        string tenant = "tenant-a",
        string actor = "test-suite",
        DateOnly? retentionUntil = null)
    {
        return new ArchiveFileRequest
        {
            Content = new MemoryStream("content"u8.ToArray()),
            OriginalFilename = filename,
            FileType = fileType,
            SourceSystem = sourceSystem,
            Tenant = tenant,
            ReceivedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            CreatedBy = actor,
            RetentionUntil = retentionUntil ?? new DateOnly(2036, 1, 1)
        };
    }
}
