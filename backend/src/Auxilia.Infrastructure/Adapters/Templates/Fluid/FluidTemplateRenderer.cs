using System.Text.Encodings.Web;

using Auxilia.Application.Abstractions.Channels;

using Fluid;

using Microsoft.Extensions.Caching.Memory;

namespace Auxilia.Infrastructure.Adapters.Templates.Fluid;

/// <summary>
/// Liquid templates with Fluid; values are HTML-encoded in bodies. Parsed templates are cached by source in a bounded
/// cache (sources edited or previewed many times must not grow the memory without limit), and a render stops after
/// <see cref="MaxSteps"/> statements (a huge loop must not hold an Api thread).
/// </summary>
internal sealed class FluidTemplateRenderer : ITemplateRenderer, IDisposable
{
    /// <summary>Statements a render may execute: far above any real template, low enough to end a runaway loop quickly.</summary>
    internal const int MaxSteps = 100_000;

    /// <summary>Characters of template source kept in the cache (each entry weighs its length).</summary>
    internal const long CacheSizeLimit = 10_000_000;

    private static readonly FluidParser Parser = new();

    private static readonly TemplateOptions RenderOptions = new() { MaxSteps = MaxSteps };

    private static readonly TimeSpan SlidingExpiration = TimeSpan.FromHours(1);

    private readonly MemoryCache parsed;

    public FluidTemplateRenderer()
        : this(CacheSizeLimit)
    {
    }

    internal FluidTemplateRenderer(long cacheSizeLimit)
    {
        parsed = new MemoryCache(new MemoryCacheOptions { SizeLimit = cacheSizeLimit });
    }

    internal int CachedCount => parsed.Count;

    public bool IsValid(string template) => Parse(template) is not null;

    public string? Render(string template, IReadOnlyDictionary<string, object?> model, bool html)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (Parse(template) is not { } fluid)
        {
            return null;
        }

        // Default member access: only values set here are visible (no reflection over arbitrary objects).
        var context = new TemplateContext(RenderOptions);
        foreach (var (name, value) in model)
        {
            context.SetValue(name, value);
        }

        try
        {
            return html ? fluid.Render(context, HtmlEncoder.Default) : fluid.Render(context, NullEncoder.Default);
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException or ArgumentException)
        {
            // Includes a render stopped by MaxSteps or MaxRecursion.
            return null;
        }
    }

    public void Dispose() => parsed.Dispose();

    private IFluidTemplate? Parse(string template)
    {
        ArgumentNullException.ThrowIfNull(template);
        if (parsed.TryGetValue(template, out Parsed? cached))
        {
            return cached!.Template;
        }

        var entry = new Parsed(Parser.TryParse(template, out var result, out _) ? result : null);
        parsed.Set(template, entry, new MemoryCacheEntryOptions { Size = Math.Max(1, template.Length), SlidingExpiration = SlidingExpiration });
        return entry.Template;
    }

    /// <summary>Also caches sources that do not parse (<see cref="Template"/> null).</summary>
    private sealed record Parsed(IFluidTemplate? Template);
}
