using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace MTSM.Cirrus.API.Security;

public sealed class ApiKeyOpenApiTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes = new Dictionary<string, IOpenApiSecurityScheme>
        {
            [ApiKeyOptions.Scheme] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = ApiKeyOptions.Scheme,
                Description = "Cirrus machine credential: cirrus_<key-id>.<secret>"
            }
        };

        foreach (var operation in document.Paths.Values.SelectMany(path => path.Operations ?? []))
        {
            operation.Value.Security ??= [];
            operation.Value.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(ApiKeyOptions.Scheme, document)] = []
            });
        }

        return Task.CompletedTask;
    }
}
