using MTSM.Cirrus.API.Middleware;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Scalar.AspNetCore;

namespace MTSM.Cirrus.API.Extensions;

public static class WebApplicationExtensions
{
    public static WebApplication UseCirrusApi(
        this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseMiddleware<ExceptionMiddleware>();

        app.UseHttpsRedirection();

        app.UseAuthorization();

        app.MapControllers();

        app.MapHealthChecks(
            "/health/live",
            new HealthCheckOptions
            {
                Predicate = _ => false
            });

        app.MapHealthChecks(
            "/health/ready",
            new HealthCheckOptions
            {
                Predicate = registration =>
                    registration.Tags.Contains("ready")
            });

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();

            app.MapScalarApiReference(
                "/scalar",
                options =>
                {
                    options
                        .WithTitle("MTSM Cirrus Archive API")
                        .WithOpenApiRoutePattern(
                            "/openapi/{documentName}.json");
                });
        }

        return app;
    }
}