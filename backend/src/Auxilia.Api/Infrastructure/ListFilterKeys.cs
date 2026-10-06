using System.Reflection;

using Auxilia.Diagnostics;

using Microsoft.AspNetCore.Mvc;

namespace Auxilia.Api.Infrastructure;

/// <summary>
/// List filters (F28): a <c>filter[key]</c> the endpoint does not declare is refused with 400 <c>AUX-10020</c> (field
/// error <c>validation.paging.filter</c>) instead of being silently ignored, so a client never believes a list is
/// filtered when it is not. The declared keys come from the handler's <c>[FromQuery(Name = "filter[…]")]</c>
/// parameters, read once when the endpoint is built. A handler that takes <see cref="HttpRequest"/> reads the query
/// itself (the exports forward a list's filters) and is left alone. Sort fields are checked by each QueryService.
/// </summary>
internal static class ListFilterKeys
{
    public const string Prefix = "filter[";

    public const string UnknownFilterKey = "validation.paging.filter";

    public static TBuilder RejectUnknownFilters<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilterFactory((context, next) =>
        {
            var parameters = context.MethodInfo.GetParameters();
            if (parameters.Any(parameter => parameter.ParameterType == typeof(HttpRequest)))
            {
                return next;
            }

            var declared = parameters
                .Select(QueryName)
                .Where(name => name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return invocation =>
            {
                var unknown = invocation.HttpContext.Request.Query.Keys
                    .Where(key => key.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) && !declared.Contains(key))
                    .ToArray();
                return unknown.Length == 0
                    ? next(invocation)
                    : ValueTask.FromResult<object?>(Errors.Host.ValidationFailed(
                        unknown.ToDictionary(key => key, _ => new[] { UnknownFilterKey }, StringComparer.Ordinal)).ToProblem());
            };
        });

    private static string QueryName(ParameterInfo parameter) =>
        parameter.GetCustomAttribute<FromQueryAttribute>()?.Name ?? parameter.Name ?? string.Empty;
}
