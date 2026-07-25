using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Options;

namespace MTSM.Cirrus.API.Config;

public sealed class ConfigureKestrelServerOptions
    : IConfigureOptions<KestrelServerOptions>
{
    private readonly ApiOptions _apiOptions;

    public ConfigureKestrelServerOptions(
    IOptions<ApiOptions> apiOptions)
    {
        ArgumentNullException.ThrowIfNull(apiOptions);

        _apiOptions = apiOptions.Value;
    }

    public void Configure(
        KestrelServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.Limits.MaxRequestBodySize =
            _apiOptions.MaxMultipartUploadSizeBytes;
    }
}

