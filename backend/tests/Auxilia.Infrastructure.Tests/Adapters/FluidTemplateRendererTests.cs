using Auxilia.Infrastructure.Adapters.Templates.Fluid;

namespace Auxilia.Infrastructure.Tests.Adapters;

public sealed class FluidTemplateRendererTests
{
    private readonly FluidTemplateRenderer renderer = new();

    [Fact]
    public void Body_EncodesValuesForHtml_SubjectDoesNot()
    {
        var model = new Dictionary<string, object?> { ["name"] = "<b>D'Angelo</b>", ["count"] = 3 };

        renderer.Render("<p>Ciao {{ name }} ({{ count }})</p>", model, html: true).ShouldBe("<p>Ciao &lt;b&gt;D&#x27;Angelo&lt;/b&gt; (3)</p>");
        renderer.Render("Ciao {{ name }}", model, html: false).ShouldBe("Ciao <b>D'Angelo</b>");
    }

    [Fact]
    public void InvalidTemplate_IsReported()
    {
        renderer.IsValid("{{ name ").ShouldBeFalse();
        renderer.Render("{% if %}", new Dictionary<string, object?>(), html: true).ShouldBeNull();
        renderer.IsValid("Hello {{ name | upcase }}").ShouldBeTrue();
    }

    [Fact]
    public void MissingValues_RenderEmpty() =>
        renderer.Render("[{{ missing }}]", new Dictionary<string, object?>(), html: false).ShouldBe("[]");
}
