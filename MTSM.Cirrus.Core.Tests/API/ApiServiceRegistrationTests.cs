using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MTSM.Cirrus.API.Extensions;
using MTSM.Cirrus.API.Security;

namespace MTSM.Cirrus.Core.Tests;

public sealed class ApiServiceRegistrationTests
{
    [Fact]
    public async Task AddCirrusApi_RegistersApiKeyAuthenticationWithoutDataProtection()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:ArchiveDatabase"] =
                    "Host=localhost;Database=cirrus;Username=cirrus;Password=test",
                ["S3:ServiceUrl"] = "http://localhost:8333",
                ["S3:AccessKey"] = "test",
                ["S3:SecretKey"] = "test"
            })
            .Build();
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddCirrusApi(configuration);

        Assert.DoesNotContain(services,
            descriptor => descriptor.ServiceType == typeof(IDataProtectionProvider));

        await using ServiceProvider provider = services.BuildServiceProvider();
        var schemes = provider.GetRequiredService<IAuthenticationSchemeProvider>();
        AuthenticationScheme? defaultScheme = await schemes.GetDefaultAuthenticateSchemeAsync();
        ApiKeyOptions schemeOptions = provider
            .GetRequiredService<IOptionsMonitor<ApiKeyOptions>>()
            .Get(ApiKeyOptions.Scheme);

        Assert.Equal(ApiKeyOptions.Scheme, defaultScheme?.Name);
        Assert.NotNull(schemeOptions.TimeProvider);
    }
}
