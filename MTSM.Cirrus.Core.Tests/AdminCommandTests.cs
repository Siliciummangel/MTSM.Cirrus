using Microsoft.Extensions.Configuration;
using MTSM.Cirrus.Admin.Commands;
using MTSM.Cirrus.Admin.Infrastructure;
using System.CommandLine;

namespace MTSM.Cirrus.Core.Tests;

public sealed class AdminCommandTests
{
    [Theory]
    [InlineData("machine create --tenant 0 --name importer --permission archive.read")]
    [InlineData("machine create --tenant 1 --name importer --permission archive.unknown")]
    [InlineData("api-key create --tenant 1 --machine importer --expires-at 2020-01-01T00:00:00Z")]
    public async Task InvalidOptions_AreRejectedBeforeDatabaseAccess(string commandLine)
    {
        RootCommand root = CreateRootCommand();

        int exitCode = await root.Parse(commandLine).InvokeAsync();

        Assert.NotEqual(0, exitCode);
    }

    private static RootCommand CreateRootCommand()
    {
        IConfiguration configuration = new ConfigurationBuilder().Build();
        var context = new AdminCommandContext(configuration);
        var root = new RootCommand();
        root.Subcommands.Add(MachineCommand.Create(context));
        root.Subcommands.Add(ApiKeyCommand.Create(context));
        return root;
    }
}
