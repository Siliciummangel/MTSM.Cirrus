using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

namespace MTSM.Cirrus.API.Config;

public sealed class ConfigureFormOptions
    : IConfigureOptions<FormOptions>
{
    private readonly ApiOptions _apiOptions;

    public ConfigureFormOptions(
        IOptions<ApiOptions> apiOptions)
    {
        _apiOptions = apiOptions.Value;
    }

    public void Configure(FormOptions options)
    {
        options.MultipartBodyLengthLimit =
            _apiOptions.MaxMultipartUploadSizeBytes;
    }
}