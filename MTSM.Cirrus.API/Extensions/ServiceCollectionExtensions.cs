using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Options;
using MTSM.Cirrus.API.Config;
using MTSM.Cirrus.Core.Data;
using System.Text.Json.Serialization;

namespace MTSM.Cirrus.API.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCirrusApi(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<ApiOptions>()
            .Bind(configuration.GetSection(
                ApiOptions.SectionName))
            .Validate(
                options =>
                    options.MaxMultipartUploadSizeBytes > 0,
                "The maximum upload size must be greater than zero.")
            .ValidateOnStart();

        services.AddSingleton<
            IConfigureOptions<FormOptions>,
            ConfigureFormOptions>();

        services.AddSingleton<
            IConfigureOptions<KestrelServerOptions>,
            ConfigureKestrelServerOptions>();

        services
            .AddControllers()
            .AddJsonOptions(options =>
                options.JsonSerializerOptions.Converters.Add(
                    new JsonStringEnumConverter()));

        services.AddProblemDetails();

        services.AddOpenApi();

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
