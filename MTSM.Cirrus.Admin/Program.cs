using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using MTSM.Cirrus.Admin.Commands;
using MTSM.Cirrus.Admin.Infrastructure;
using System.CommandLine;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddUserSecrets<Program>(optional: true);
builder.Configuration.AddEnvironmentVariables();

var context = new AdminCommandContext(builder.Configuration);

var rootCommand = new RootCommand(
    "MTSM.Cirrus administration CLI");

rootCommand.Subcommands.Add(MachineCommand.Create(context));
rootCommand.Subcommands.Add(ApiKeyCommand.Create(context));

return await rootCommand.Parse(args).InvokeAsync();
