using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Auxilia.Api.Infrastructure;

/// <summary>Publishes <c>/api/v{version}/…</c> routes as <c>/api/v1/…</c>: the version is part of the document, not a parameter.</summary>
internal sealed class ApiVersionPathTransformer : IOpenApiDocumentTransformer
{
    public const string VersionPlaceholder = "{version}";

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.Paths is null)
        {
            return Task.CompletedTask;
        }

        var paths = new OpenApiPaths();
        foreach (var (path, item) in document.Paths)
        {
            paths[path.Replace("v" + VersionPlaceholder, OpenApiSetup.DocumentName, StringComparison.Ordinal)] = item;
        }

        document.Paths = paths;
        return Task.CompletedTask;
    }
}

internal sealed class ApiVersionParameterTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var version = operation.Parameters?.FirstOrDefault(parameter => parameter.In == ParameterLocation.Path && parameter.Name == "version");
        if (version is not null)
        {
            operation.Parameters!.Remove(version);
        }

        return Task.CompletedTask;
    }
}
