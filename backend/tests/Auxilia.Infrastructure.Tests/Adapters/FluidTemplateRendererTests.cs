using Auxilia.Infrastructure.Adapters.Templates.Fluid;

namespace Auxilia.Infrastructure.Tests.Adapters;

public sealed class FluidTemplateRendererTests : IDisposable
{
    private readonly FluidTemplateRenderer renderer = new();

    public void Dispose() => renderer.Dispose();

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

    [Fact]
    public void RunawayLoop_StopsAfterMaxSteps_AndRendersNothing()
    {
        renderer.IsValid("{% for i in (1..100000000) %}x{% endfor %}").ShouldBeTrue();

        renderer.Render("{% for i in (1..100000000) %}x{% endfor %}", new Dictionary<string, object?>(), html: true).ShouldBeNull();
    }

    [Fact]
    public void LoopWithinMaxSteps_Renders() =>
        renderer.Render("{% for i in (1..3) %}{{ i }}{% endfor %}", new Dictionary<string, object?>(), html: false).ShouldBe("123");

    [Fact]
    public void Cache_StaysWithinItsSizeLimit()
    {
        using var bounded = new FluidTemplateRenderer(cacheSizeLimit: 100);

        for (var i = 0; i < 50; i++)
        {
            bounded.Render($"Template number {i:D4} {{{{ name }}}}", new Dictionary<string, object?> { ["name"] = "x" }, html: false)
                .ShouldBe($"Template number {i:D4} x");
        }

        // Each entry weighs its length (31 characters): at most three fit in 100.
        bounded.CachedCount.ShouldBeLessThanOrEqualTo(3);
    }
}
