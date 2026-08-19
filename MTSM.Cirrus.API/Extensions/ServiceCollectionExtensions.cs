using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Options;
using MTSM.Cirrus.API.Config;
using MTSM.Cirrus.API.Security;
using MTSM.Cirrus.Core.Data;
using MTSM.Cirrus.Core.Security;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;

namespace MTSM.Cirrus.API.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCirrusApi(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var apiOptions = new ApiOptions();
        configuration.GetSection(ApiOptions.SectionName).Bind(apiOptions);

        services
            .AddOptions<ApiOptions>()
            .Bind(configuration.GetSection(
                ApiOptions.SectionName))
            .Validate(
                options =>
                    options.MaxMultipartUploadSizeBytes > 0,
                "The maximum upload size must be greater than zero.")
            .Validate(options => options.RateLimitPermitCount > 0,
                "The rate limit permit count must be greater than zero.")
            .Validate(options => options.RateLimitWindowSeconds > 0,
                "The rate limit window must be greater than zero.")
            .ValidateOnStart();

        services.AddSingleton<
            IConfigureOptions<FormOptions>,
            ConfigureFormOptions>();

        services.AddSingleton<
            IConfigureOptions<KestrelServerOptions>,
            ConfigureKestrelServerOptions>();

        services.AddHttpContextAccessor();
        services.AddScoped<ICirrusIdentityAccessor, HttpCirrusIdentityAccessor>();
        services.AddScoped<TenantBoundaryFilter>();

        services.AddAuthentication(ApiKeyOptions.Scheme)
            .AddScheme<ApiKeyOptions, ApiKeyAuthenticationHandler>(ApiKeyOptions.Scheme, _ => { });

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser().Build())
            .AddPolicy(CirrusAuthorizationPolicies.Read, policy => policy
                .RequireClaim(CirrusClaimTypes.Permission, CirrusPermissionNames.ArchiveRead))
            .AddPolicy(CirrusAuthorizationPolicies.Write, policy => policy
                .RequireClaim(CirrusClaimTypes.Permission, CirrusPermissionNames.ArchiveWrite))
            .AddPolicy(CirrusAuthorizationPolicies.Delete, policy => policy
                .RequireClaim(CirrusClaimTypes.Permission, CirrusPermissionNames.ArchiveDelete))
            .AddPolicy(CirrusAuthorizationPolicies.Verify, policy => policy
                .RequireClaim(CirrusClaimTypes.Permission, CirrusPermissionNames.ArchiveVerify));

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, _) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter))
                    context.HttpContext.Response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds)
                        .ToString(System.Globalization.CultureInfo.InvariantCulture);
                return ValueTask.CompletedTask;
            };
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                string partition = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                    ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return RateLimitPartition.GetFixedWindowLimiter(partition, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = apiOptions.RateLimitPermitCount,
                    Window = TimeSpan.FromSeconds(apiOptions.RateLimitWindowSeconds),
                    QueueLimit = 0,
                    AutoReplenishment = true
                });
            });
        });

        services
            .AddControllers(options => options.Filters.Add<TenantBoundaryFilter>())
            .AddJsonOptions(options =>
                options.JsonSerializerOptions.Converters.Add(
                    new JsonStringEnumConverter()));

        services.AddProblemDetails();

        services.AddOpenApi(options =>
            options.AddDocumentTransformer<ApiKeyOpenApiTransformer>());

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
