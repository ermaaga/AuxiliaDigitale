using System.Text.Encodings.Web;

using Auxilia.Application.Abstractions.Channels;

using Fluid;

namespace Auxilia.Infrastructure.Adapters.Templates.Fluid;

/// <summary>Liquid templates with Fluid; values are HTML-encoded in bodies. Parsed templates are cached by source.</summary>
internal sealed class FluidTemplateRenderer : ITemplateRenderer
{
    private static readonly FluidParser Parser = new();

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, IFluidTemplate?> parsed = new(StringComparer.Ordinal);

    public bool IsValid(string template) => Parse(template) is not null;

    public string? Render(string template, IReadOnlyDictionary<string, object?> model, bool html)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (Parse(template) is not { } fluid)
        {
            return null;
        }

        // Default member access: only values set here are visible (no reflection over arbitrary objects).
        var context = new TemplateContext();
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
            return null;
        }
    }

    private IFluidTemplate? Parse(string template)
    {
        ArgumentNullException.ThrowIfNull(template);
        return parsed.GetOrAdd(template, source => Parser.TryParse(source, out var result, out _) ? result : null);
    }
}
