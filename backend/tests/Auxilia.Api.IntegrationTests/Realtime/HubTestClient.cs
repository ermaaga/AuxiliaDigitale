using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

using Microsoft.AspNetCore.TestHost;

namespace Auxilia.Api.IntegrationTests.Realtime;

/// <summary>
/// A minimal SignalR client (JSON protocol over WebSockets on the test server): negotiate, handshake, then read the
/// server's invocation messages. Avoids a client package for tests.
/// </summary>
internal sealed class HubTestClient : IAsyncDisposable
{
    private const char RecordSeparator = '\u001e';

    private readonly WebSocket socket;
    private readonly StringBuilder pending = new();

    private HubTestClient(WebSocket socket) => this.socket = socket;

    public static async Task<HubTestClient> ConnectAsync(TestServer server, string path, string accessToken, CancellationToken cancellationToken)
    {
        using var http = server.CreateClient();
        using var negotiate = new HttpRequestMessage(HttpMethod.Post, $"{path}/negotiate?negotiateVersion=1");
        negotiate.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var negotiated = await http.SendAsync(negotiate, cancellationToken);
        negotiated.EnsureSuccessStatusCode();
        var connectionToken = (await negotiated.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)).GetProperty("connectionToken").GetString();

        // Browsers put the token in the query string on WebSockets: the API accepts it only on the hub path.
        var uri = new Uri(server.BaseAddress, $"{path}?id={connectionToken}&access_token={Uri.EscapeDataString(accessToken)}");
        var socket = await server.CreateWebSocketClient().ConnectAsync(new UriBuilder(uri) { Scheme = "ws" }.Uri, cancellationToken);
        var client = new HubTestClient(socket);

        await client.SendAsync("""{"protocol":"json","version":1}""", cancellationToken);
        var handshake = await client.ReceiveAsync(cancellationToken);
        handshake.TryGetProperty("error", out _).ShouldBeFalse(handshake.ToString());

        // Barrier: the hub handles messages only after OnConnectedAsync (groups joined), so the completion of this
        // invocation of an unknown method proves the connection is fully set up.
        await client.SendAsync("""{"type":1,"invocationId":"ready","target":"NoSuchMethod","arguments":[]}""", cancellationToken);
        await client.NextAsync(message => message.GetProperty("type").GetInt32() == 3, cancellationToken);
        return client;
    }

    /// <summary>The next invocation of <paramref name="target"/> (its first argument), skipping pings and others.</summary>
    public async Task<JsonElement> NextInvocationAsync(string target, CancellationToken cancellationToken)
    {
        var message = await NextAsync(
            item => item.GetProperty("type").GetInt32() == 1 && item.GetProperty("target").GetString() == target, cancellationToken);
        return message.GetProperty("arguments")[0];
    }

    /// <summary>True when the server closed the connection (or sends a close message) before <paramref name="timeout"/>.</summary>
    public async Task<bool> IsClosedAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeout);
        try
        {
            while (true)
            {
                var message = await ReceiveAsync(linked.Token);
                if (message.ValueKind == JsonValueKind.Undefined || message.GetProperty("type").GetInt32() == 7)
                {
                    return true;
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (WebSocketException)
        {
            return true;
        }
    }

    /// <summary>Waits up to <paramref name="timeout"/> for an invocation of <paramref name="target"/>; null when none arrives.</summary>
    public async Task<JsonElement?> TryNextInvocationAsync(string target, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeout);
        try
        {
            return await NextInvocationAsync(target, linked.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (socket.State == WebSocketState.Open)
        {
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
        }

        socket.Dispose();
    }

    private async Task<JsonElement> NextAsync(Func<JsonElement, bool> match, CancellationToken cancellationToken)
    {
        while (true)
        {
            var message = await ReceiveAsync(cancellationToken);
            if (message.ValueKind == JsonValueKind.Undefined)
            {
                throw new InvalidOperationException("The hub closed the connection.");
            }

            if (match(message))
            {
                return message;
            }
        }
    }

    private Task SendAsync(string json, CancellationToken cancellationToken) =>
        socket.SendAsync(Encoding.UTF8.GetBytes(json + RecordSeparator), WebSocketMessageType.Text, endOfMessage: true, cancellationToken);

    /// <returns>The next JSON message; <c>default</c> when the socket closed.</returns>
    private async Task<JsonElement> ReceiveAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        while (true)
        {
            var text = pending.ToString();
            var separator = text.IndexOf(RecordSeparator, StringComparison.Ordinal);
            if (separator >= 0)
            {
                pending.Remove(0, separator + 1);
                return JsonDocument.Parse(text[..separator]).RootElement.Clone();
            }

            var result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return default;
            }

            pending.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
        }
    }
}
