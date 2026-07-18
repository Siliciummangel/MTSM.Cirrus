using MTSM.Cirrus.API.Extensions;

const long maximumUploadSizeBytes =
    1024L * 1024L * 1024L;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(
    options =>
    {
        options.Limits.MaxRequestBodySize =
            maximumUploadSizeBytes;
    });

builder.Services.AddCirrusApi(
    builder.Configuration);

var app = builder.Build();

app.UseCirrusApi();

app.Run();

public partial class Program;