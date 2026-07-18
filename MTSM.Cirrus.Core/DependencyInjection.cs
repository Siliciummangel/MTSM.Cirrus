using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MTSM.Cirrus.Core.Abstractions;
using MTSM.Cirrus.Core.Config;
using MTSM.Cirrus.Core.Providers.S3;
using MTSM.Cirrus.Core.Services;

namespace MTSM.Cirrus.Core.Data;

public static class DependencyInjection
{
    public static IServiceCollection AddCirrusDatabase(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContextPool<CirrusDbContext>(options =>
        {
            options.UseNpgsql(
                connectionString,
                npgsql =>
                {
                    npgsql.MigrationsAssembly(
                        "MTSM.Cirrus.Migration");

                    npgsql.EnableRetryOnFailure(
                        maxRetryCount: 5,
                        maxRetryDelay: TimeSpan.FromSeconds(10),
                        errorCodesToAdd: null);
                });

            options.UseSnakeCaseNamingConvention();
        });

        return services;
    }

    public static IServiceCollection AddCirrusCore(
    this IServiceCollection services,
    IConfiguration configuration)
    {
        services
            .AddOptions<ArchiveOptions>()
            .Bind(configuration.GetSection(
                ArchiveOptions.SectionName))
            .Validate(
                options =>
                    !string.IsNullOrWhiteSpace(
                        options.BucketName),
                "Archive bucket name is required.")
            .ValidateOnStart();

        services
            .AddOptions<S3Options>()
            .Bind(configuration.GetSection(
                S3Options.SectionName))
            .Validate(
                options => Uri.TryCreate(
                    options.ServiceUrl,
                    UriKind.Absolute,
                    out _),
                "A valid absolute S3 service URL is required.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(
                    options.AccessKey),
                "S3 access key is required.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(
                    options.SecretKey),
                "S3 secret key is required.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(
                    options.Region),
                "S3 region is required.")
            .ValidateOnStart();

        services.AddSingleton<IObjectStorage, S3ObjectStorage>();
        services.AddScoped<IArchiveService, ArchiveService>();

        return services;
    }
}