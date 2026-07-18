using Microsoft.AspNetCore.Http.Features;
using MTSM.Cirrus.Core.Data;

namespace MTSM.Cirrus.API.Extensions;

public static class ServiceCollectionExtensions
{
    private const long MaximumUploadSizeBytes =
    1024L * 1024L * 1024L;

    public static IServiceCollection AddCirrusApi(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddControllers();

        services.AddProblemDetails();

        services.AddOpenApi();

        services.Configure<FormOptions>(
            options =>
            {
                options.MultipartBodyLengthLimit =
                    MaximumUploadSizeBytes;
            });

        string connectionString =
            configuration.GetConnectionString("ArchiveDatabase")
            ?? throw new InvalidOperationException(
                "The connection string 'ArchiveDatabase' is missing.");

        services.AddCirrusDatabase(connectionString);
        services.AddCirrusCore(configuration);

        services
            .AddHealthChecks()
            .AddDbContextCheck<CirrusDbContext>(
                name: "postgresql",
                tags: ["ready"]);

        return services;
    }
}
