using MTSM.Cirrus.API.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCirrusApi(
    builder.Configuration);

var app = builder.Build();

app.UseCirrusApi();

app.Run();

public partial class Program;
