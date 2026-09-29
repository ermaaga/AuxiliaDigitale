using Auxilia.Diagnostics;
using Auxilia.ServiceDefaults.Logging;

using NSubstitute;

using Serilog.Core;
using Serilog.Events;

namespace Auxilia.ServiceDefaults.Tests.Logging;

public sealed class EventCodeEnricherTests
{
    [Fact]
    public void Enrich_AuxiliaEventId_AddsEventCode()
    {
        var logEvent = Enrich(EventCodes.Host.UnhandledException);

        logEvent.Properties["EventCode"].ToString().ShouldBe("\"AUX-10001\"");
    }

    [Fact]
    public void Enrich_FrameworkEventId_AddsNothing()
    {
        // e.g. Microsoft.Hosting.Lifetime "ListeningOnAddress" = 14
        var logEvent = Enrich(14);

        logEvent.Properties.ContainsKey("EventCode").ShouldBeFalse();
    }

    private static LogEvent Enrich(int eventId)
    {
        var logEvent = LogEvents.Create(properties: LogEvents.EventId(eventId));
        var factory = Substitute.For<ILogEventPropertyFactory>();
        factory.CreateProperty(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<bool>())
            .Returns(call => new LogEventProperty(call.ArgAt<string>(0), new ScalarValue(call.ArgAt<object?>(1))));

        new EventCodeEnricher().Enrich(logEvent, factory);
        return logEvent;
    }
}
