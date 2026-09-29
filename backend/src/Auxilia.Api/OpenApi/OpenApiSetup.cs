using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

using Scalar.AspNetCore;

namespace Auxilia.Api.Infrastructure;

/// <summary>
/// OpenAPI document <c>v1</c> (<c>Microsoft.AspNetCore.OpenApi</c>) served with the Scalar UI in Development only.
/// The committed contract <c>backend/openapi/v1.json</c> is verified by <c>OpenApiContractTests</c>.
/// </summary>
internal static class OpenApiSetup
{
    public const string DocumentName = "v1";

    public const string DocumentPath = "/openapi/{documentName}.json";

    public const string ScalarPath = "/scalar";

    public static IServiceCollection AddAuxiliaOpenApi(this IServiceCollection services) =>
        services.AddOpenApi(DocumentName, options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info = new OpenApiInfo
                {
                    Title = "Auxilia API",
                    Version = DocumentName,
                    Description = "Tenant and platform API of Auxilia. Errors are RFC 9457 ProblemDetails with errorCode (AUX-NNNNN) and traceId.",
                };
                document.Servers = [];
                return Task.CompletedTask;
            });
            options.AddDocumentTransformer<ApiVersionPathTransformer>();
            options.AddOperationTransformer<ApiVersionParameterTransformer>();
        });

    public static WebApplication MapAuxiliaOpenApi(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi(DocumentPath);
            app.MapScalarApiReference(ScalarPath, options => options.WithOpenApiRoutePattern(DocumentPath));
        }

        return app;
    }
}
