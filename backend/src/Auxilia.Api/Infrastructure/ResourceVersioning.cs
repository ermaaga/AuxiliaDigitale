using System.Buffers.Text;
using System.Security.Cryptography;

using Auxilia.Diagnostics;

using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.Net.Http.Headers;

namespace Auxilia.Api.Infrastructure;

/// <summary>
/// Optimistic concurrency of shared resources (F29, skill auxilia-api-contract). The version of a resource is the
/// fingerprint of what its <c>GET</c> returns (SHA-256 of the JSON, a weak <c>ETag</c>): any change the reader could
/// see — also on a resource made of several rows, such as a client — gives a new version, with no version field in
/// the DTOs. A write (<c>PUT</c>/<c>DELETE</c>) of the same URL needs <c>If-Match</c>: the filter reads the resource again
/// through that <c>GET</c> (same caller, same headers) and answers 428 <c>AUX-10025</c> without the header, 412
/// <c>AUX-10011</c> when it no longer matches. A resource the caller cannot read is left to the write handler (its own
/// 404/403). Two writes in the same instant are still caught by the <c>xmin</c> token (409 <c>AUX-10010</c>).
/// </summary>
internal static class ResourceVersioning
{
    /// <summary>Setting <c>Concurrency:RequireIfMatch</c> (default true): false only for test hosts whose suites predate F29.</summary>
    public const string RequireIfMatchSetting = "Concurrency:RequireIfMatch";

    /// <summary>A single-resource <c>GET</c> that answers with its <c>ETag</c>.</summary>
    public static RouteHandlerBuilder WithETag(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter(new ResourceVersionFilter(write: false)).WithMetadata(new ETagMetadata());

    /// <summary>A write of the resource whose <c>GET</c> (same route) has <see cref="WithETag"/>: <c>If-Match</c> required.</summary>
    public static RouteHandlerBuilder RequireIfMatch(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter(new ResourceVersionFilter(write: true))
            .WithMetadata(new IfMatchMetadata())
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired);

    /// <summary>The weak entity tag of a representation.</summary>
    public static string ETagOf(ReadOnlySpan<byte> representation)
    {
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(representation, hash);
        return $"W/\"{Base64Url.EncodeToString(hash[..16])}\"";
    }

    /// <summary>Weak comparison (RFC 9110 §8.8.3.2): the opaque tags are equal; <c>*</c> matches any current version.</summary>
    public static bool Matches(string? ifMatch, string current)
    {
        if (string.IsNullOrWhiteSpace(ifMatch))
        {
            return false;
        }

        if (ifMatch.Trim() == "*")
        {
            return true;
        }

        return EntityTagHeaderValue.TryParseList([ifMatch], out var tags)
            && EntityTagHeaderValue.TryParse(current, out var currentTag)
            && tags.Any(tag => tag.Compare(currentTag, useStrongComparison: false));
    }

    private sealed class ResourceVersionFilter(bool write) : IEndpointFilter
    {
        public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(next);

            var http = context.HttpContext;
            if (!write)
            {
                var result = await next(context);
                if (result is IResult value && await ETagOfAsync(http, value) is { } etag)
                {
                    http.Response.Headers.ETag = etag;
                }

                return result;
            }

            var ifMatch = http.Request.Headers.IfMatch.ToString();
            var required = http.RequestServices.GetRequiredService<IConfiguration>().GetValue(RequireIfMatchSetting, defaultValue: true);
            if ((required || !string.IsNullOrWhiteSpace(ifMatch)) && await CurrentETagAsync(http) is { } current)
            {
                if (string.IsNullOrWhiteSpace(ifMatch))
                {
                    return Errors.Host.PreconditionRequired().ToProblem();
                }

                if (!Matches(ifMatch, current))
                {
                    return Errors.Host.PreconditionFailed().ToProblem();
                }
            }

            return await next(context);
        }

        /// <summary>Writes the result into memory to fingerprint the bytes it will send (only a 200 has a version).</summary>
        private static async Task<string?> ETagOfAsync(HttpContext http, IResult result)
        {
            var capture = new DefaultHttpContext { RequestServices = http.RequestServices };
            using var body = new MemoryStream();
            capture.Response.Body = body;
            await result.ExecuteAsync(capture);
            return capture.Response.StatusCode == StatusCodes.Status200OK ? ETagOf(body.ToArray()) : null;
        }

        /// <summary>The current version: the <c>GET</c> of the same route, run for the same caller; null when it is not a 200.</summary>
        private static async Task<string?> CurrentETagAsync(HttpContext http)
        {
            if (http.GetEndpoint() is not RouteEndpoint endpoint || ReaderOf(http, endpoint) is not { RequestDelegate: { } read } reader)
            {
                throw new InvalidOperationException($"{http.GetEndpoint()?.DisplayName} requires If-Match but its route has no GET.");
            }

            var sub = new DefaultHttpContext { RequestServices = http.RequestServices, User = http.User, RequestAborted = http.RequestAborted };
            foreach (var (name, values) in http.Request.Headers)
            {
                if (!name.StartsWith("Content-", StringComparison.OrdinalIgnoreCase) && !name.Equals(HeaderNames.IfMatch, StringComparison.OrdinalIgnoreCase))
                {
                    sub.Request.Headers[name] = values;
                }
            }

            sub.Request.Method = HttpMethods.Get;
            sub.Request.Scheme = http.Request.Scheme;
            sub.Request.Host = http.Request.Host;
            sub.Request.PathBase = http.Request.PathBase;
            sub.Request.Path = http.Request.Path;
            sub.Request.RouteValues = new RouteValueDictionary(http.Request.RouteValues);
            sub.SetEndpoint(reader);
            using var body = new MemoryStream();
            sub.Response.Body = body;

            await read(sub);
            return sub.Response.StatusCode == StatusCodes.Status200OK && sub.Response.Headers.ETag.ToString() is { Length: > 0 } etag
                ? etag
                : null;
        }

        private static RouteEndpoint? ReaderOf(HttpContext http, RouteEndpoint write) =>
            http.RequestServices.GetServices<EndpointDataSource>()
                .SelectMany(source => source.Endpoints)
                .OfType<RouteEndpoint>()
                .FirstOrDefault(candidate => candidate.RoutePattern.RawText == write.RoutePattern.RawText
                    && candidate.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Contains(HttpMethods.Get) == true);
    }
}

/// <summary>Endpoint metadata: a <c>GET</c> answering with an <c>ETag</c> (tests).</summary>
public sealed record ETagMetadata;

/// <summary>Endpoint metadata: a write that checks <c>If-Match</c> against the <c>GET</c> of its route (tests).</summary>
public sealed record IfMatchMetadata;
