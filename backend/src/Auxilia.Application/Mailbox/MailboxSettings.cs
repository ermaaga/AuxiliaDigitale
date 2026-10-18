using System.Globalization;

using Auxilia.Application.Abstractions.Settings;

namespace Auxilia.Application.Mailbox;

/// <summary>Settings of the mailbox module (N05).</summary>
public static class MailboxSettings
{
    public const string Module = "Mailbox";

    /// <summary>Ports a user-entered IMAP server may use, comma separated (993 = TLS, 143 = STARTTLS).</summary>
    public static readonly SettingDefinition<string> ImapAllowedPorts = new(
        "mailbox.imap.allowedPorts", Module, "993,143", isValid: value => ParsePorts(value) is { Count: > 0 });

    /// <summary>A sync of the same account and scope ended less than this ago: opening the page queues nothing (#106).</summary>
    public static readonly SettingDefinition<int> SyncMinIntervalSeconds = new(
        "mailbox.sync.minIntervalSeconds", Module, 120, isValid: value => value is >= 30 and <= 3600);

    /// <summary>Days of past calendar events kept (sync window).</summary>
    public static readonly SettingDefinition<int> CalendarPastDays = new(
        "mailbox.calendar.pastDays", Module, 30, isValid: value => value is >= 0 and <= 365);

    /// <summary>Days of future calendar events kept (sync window).</summary>
    public static readonly SettingDefinition<int> CalendarFutureDays = new(
        "mailbox.calendar.futureDays", Module, 180, isValid: value => value is >= 1 and <= 730);

    /// <summary>The first (or a full) mail sync copies the headers received in the last days only.</summary>
    public static readonly SettingDefinition<int> SyncInitialDays = new(
        "mailbox.sync.initialDays", Module, 90, isValid: value => value is >= 1 and <= 365);

    /// <summary>The first (or a full) mail sync copies at most this many headers per folder, newest first.</summary>
    public static readonly SettingDefinition<int> SyncInitialMaxMessages = new(
        "mailbox.sync.initialMaxMessages", Module, 2000, isValid: value => value is >= 100 and <= 10000);

    public static IReadOnlyList<SettingDefinition> All { get; } =
        [ImapAllowedPorts, SyncMinIntervalSeconds, CalendarPastDays, CalendarFutureDays, SyncInitialDays, SyncInitialMaxMessages];

    /// <returns>Null when a part is not a port.</returns>
    public static IReadOnlySet<int>? ParsePorts(string? value)
    {
        var ports = new HashSet<int>();
        foreach (var part in (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535)
            {
                return null;
            }

            ports.Add(port);
        }

        return ports;
    }
}
