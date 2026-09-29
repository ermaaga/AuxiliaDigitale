using Asp.Versioning;

namespace Auxilia.Api.Infrastructure;

/// <summary>URL-segment versioning <c>/api/v{major}</c> (skill auxilia-api-contract); current version 1.</summary>
internal static class ApiVersioningSetup
{
    public const string VersionedPrefix = "/api/v{version:apiVersion}";

    public static readonly ApiVersion V1 = new(1, 0);

    public static IServiceCollection AddAuxiliaApiVersioning(this IServiceCollection services)
    {
        services.AddApiVersioning(options =>
        {
            options.DefaultApiVersion = V1;
            options.AssumeDefaultVersionWhenUnspecified = false;
            options.ApiVersionReader = new UrlSegmentApiVersionReader();
            options.ReportApiVersions = true;
        });

        return services;
    }

    /// <summary>The <c>/api/v1</c> group where every tenant and platform endpoint is mapped.</summary>
    public static RouteGroupBuilder MapApiV1(this WebApplication app) =>
        app.NewVersionedApi("Auxilia").MapGroup(VersionedPrefix).HasApiVersion(V1);
}
