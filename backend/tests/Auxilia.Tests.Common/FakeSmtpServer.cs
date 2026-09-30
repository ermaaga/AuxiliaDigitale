using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Auxilia.Tests.Common;

/// <summary>
/// A minimal SMTP server for tests (no TLS): accepts AUTH PLAIN/LOGIN for <see cref="Password"/>, refuses recipients
/// starting with <c>reject</c> (550) and answers 451 to recipients starting with <c>busy</c> (transient).
/// Received messages are in <see cref="Messages"/>.
/// </summary>
public sealed class FakeSmtpServer : IAsyncDisposable
{
    public const string Password = "smtp-secret";

    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource stopping = new();
    private readonly Task accepting;

    public FakeSmtpServer()
    {
        listener.Start();
        accepting = AcceptAsync();
    }

    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

    public ConcurrentQueue<ReceivedMail> Messages { get; } = new();

    public async ValueTask DisposeAsync()
    {
        await stopping.CancelAsync();
        listener.Stop();
        try
        {
            await accepting;
        }
        catch (OperationCanceledException)
        {
        }

        stopping.Dispose();
    }

    private async Task AcceptAsync()
    {
        while (!stopping.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(stopping.Token);
            }
            catch (Exception exception) when (exception is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                return;
            }

            _ = Task.Run(() => ServeAsync(client));
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using var _ = client;
        using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII);
        await using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };

        string? from = null;
        var recipients = new List<string>();
        await writer.WriteLineAsync("220 fake.smtp ESMTP");
        while (await reader.ReadLineAsync() is { } line)
        {
            var command = line.Length >= 4 ? line[..4].ToUpperInvariant() : line.ToUpperInvariant();
            switch (command)
            {
                case "EHLO":
                    await writer.WriteLineAsync("250-fake.smtp");
                    await writer.WriteLineAsync("250 AUTH PLAIN LOGIN");
                    break;
                case "HELO":
                    await writer.WriteLineAsync("250 fake.smtp");
                    break;
                case "AUTH":
                    await AuthenticateAsync(line, reader, writer);
                    break;
                case "MAIL":
                    from = Address(line);
                    recipients.Clear();
                    await writer.WriteLineAsync("250 OK");
                    break;
                case "RCPT":
                    var recipient = Address(line);
                    if (recipient.StartsWith("reject", StringComparison.OrdinalIgnoreCase))
                    {
                        await writer.WriteLineAsync("550 5.1.1 No such user");
                    }
                    else if (recipient.StartsWith("busy", StringComparison.OrdinalIgnoreCase))
                    {
                        await writer.WriteLineAsync("451 4.3.0 Try again later");
                    }
                    else
                    {
                        recipients.Add(recipient);
                        await writer.WriteLineAsync("250 OK");
                    }

                    break;
                case "DATA":
                    await writer.WriteLineAsync("354 End with <CRLF>.<CRLF>");
                    var data = new StringBuilder();
                    while (await reader.ReadLineAsync() is { } dataLine && dataLine != ".")
                    {
                        data.AppendLine(dataLine.StartsWith("..", StringComparison.Ordinal) ? dataLine[1..] : dataLine);
                    }

                    Messages.Enqueue(new ReceivedMail(from ?? string.Empty, recipients.ToArray(), data.ToString()));
                    await writer.WriteLineAsync("250 OK queued");
                    break;
                case "RSET":
                case "NOOP":
                    await writer.WriteLineAsync("250 OK");
                    break;
                case "QUIT":
                    await writer.WriteLineAsync("221 Bye");
                    return;
                default:
                    await writer.WriteLineAsync("502 Command not implemented");
                    break;
            }
        }
    }

    private static async Task AuthenticateAsync(string line, StreamReader reader, StreamWriter writer)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string? password;
        if (parts[1].Equals("PLAIN", StringComparison.OrdinalIgnoreCase))
        {
            var payload = parts.Length > 2 ? parts[2] : await PromptAsync(string.Empty, reader, writer);
            password = Encoding.UTF8.GetString(Convert.FromBase64String(payload)).Split('\0').LastOrDefault();
        }
        else
        {
            var initial = parts.Length > 2 ? parts[2] : await PromptAsync("VXNlcm5hbWU6", reader, writer);
            _ = initial;
            password = Encoding.UTF8.GetString(Convert.FromBase64String(await PromptAsync("UGFzc3dvcmQ6", reader, writer)));
        }

        await writer.WriteLineAsync(password == Password ? "235 2.7.0 Authentication successful" : "535 5.7.8 Authentication credentials invalid");
    }

    private static async Task<string> PromptAsync(string challenge, StreamReader reader, StreamWriter writer)
    {
        await writer.WriteLineAsync("334 " + challenge);
        return await reader.ReadLineAsync() ?? string.Empty;
    }

    private static string Address(string line)
    {
        var start = line.IndexOf('<', StringComparison.Ordinal);
        var end = line.IndexOf('>', StringComparison.Ordinal);
        return start >= 0 && end > start ? line[(start + 1)..end] : string.Empty;
    }
}

public sealed record ReceivedMail(string From, IReadOnlyList<string> Recipients, string Data);
