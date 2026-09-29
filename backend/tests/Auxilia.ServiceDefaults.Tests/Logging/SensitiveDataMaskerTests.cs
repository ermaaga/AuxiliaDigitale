using Auxilia.ServiceDefaults.Logging;

using NSubstitute;

using Serilog.Core;
using Serilog.Events;

namespace Auxilia.ServiceDefaults.Tests.Logging;

public sealed class SensitiveDataMaskerTests
{
    private readonly SensitiveDataMasker masker = new();

    [Theory]
    [InlineData("Password")]
    [InlineData("RefreshToken")]
    [InlineData("ClientSecret")]
    [InlineData("ConnectionString")]
    [InlineData("ApiKey")]
    public void Enrich_SecretProperty_IsRedacted(string name)
    {
        var logEvent = Enrich(new LogEventProperty(name, new ScalarValue("s3cr3t")));

        Scalar(logEvent, name).ShouldBe(SensitiveDataMasker.Redacted);
    }

    [Fact]
    public void Enrich_FiscalCodeInAnyProperty_KeepsFirstThreeAndLastCharacter()
    {
        var logEvent = Enrich(new LogEventProperty("Search", new ScalarValue("RSSMRA85T10A562S")));

        Scalar(logEvent, "Search").ShouldBe("RSS***…***S");
    }

    [Fact]
    public void Enrich_NestedStructure_IsMasked()
    {
        var request = new StructureValue(
        [
            new LogEventProperty("UserName", new ScalarValue("mario")),
            new LogEventProperty("Password", new ScalarValue("s3cr3t")),
        ]);

        var logEvent = Enrich(new LogEventProperty("Request", request));

        var masked = (StructureValue)logEvent.Properties["Request"];
        masked.Properties.Single(property => property.Name == "UserName").Value.ToString().ShouldBe("\"mario\"");
        masked.Properties.Single(property => property.Name == "Password").Value.ToString().ShouldBe("\"***\"");
    }

    [Fact]
    public void Enrich_OrdinaryProperty_IsUnchanged()
    {
        var value = new ScalarValue("acme");

        var logEvent = Enrich(new LogEventProperty("TenantSlug", value));

        logEvent.Properties["TenantSlug"].ShouldBeSameAs(value);
    }

    private LogEvent Enrich(LogEventProperty property)
    {
        var logEvent = LogEvents.Create(properties: property);
        masker.Enrich(logEvent, Substitute.For<ILogEventPropertyFactory>());
        return logEvent;
    }

    private static object? Scalar(LogEvent logEvent, string name) => ((ScalarValue)logEvent.Properties[name]).Value;
}
