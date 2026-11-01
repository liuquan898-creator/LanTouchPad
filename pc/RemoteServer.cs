using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace LanTouchPad;

internal sealed class RemoteServer
{
    public const int Port = 45832, DiscoveryPort = 45831;
    private readonly int controlPort, discoveryPort;
    private readonly string listenHost;
    private readonly bool allowManagement;
    private CancellationTokenSource? activeController;
    private volatile bool paused;
    public RemoteServer(int controlPort = Port, int discoveryPort = DiscoveryPort, string listenHost = "0.0.0.0", bool allowManagement = false)
    {
        this.controlPort = controlPort; this.discoveryPort = discoveryPort; this.listenHost = listenHost;
        this.allowManagement = allowManagement;
    }
    public bool Running => app != null;
    public string? Peer { get; private set; }
    public event Action? StatusChanged;
    private WebApplication? app;
    private UdpClient? udp;
    private CancellationTokenSource? cancellation;
    private Task? discoveryTask;
    private readonly SemaphoreSlim controller = new(1, 1);
    private byte[] Description => JsonSerializer.SerializeToUtf8Bytes(new { app = "LanTouchPad", protocol = 1, name = Environment.MachineName, port = controlPort, version = typeof(Program).Assembly.GetName().Version?.ToString() });

    internal static bool IsLocal(IPAddress? address)
    {
        if (address == null) return false;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return true;
        var b = address.GetAddressBytes();
        return b.Length == 4 && (b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254));
    }

    public async Task StartAsync()
    {
        if (Running) return;
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://{listenHost}:{controlPort}");
        var next = builder.Build();
        var stop = new CancellationTokenSource();
        next.UseWebSockets();
        next.Use(async (ctx, following) =>
        {
            if (!IsLocal(ctx.Connection.RemoteIpAddress) || ctx.Request.Headers.ContainsKey("Origin")) { ctx.Response.StatusCode = 403; return; }
            await following(ctx);
        });
        next.MapGet("/info", () => Results.Bytes(Description, "application/json"));
        next.MapGet("/diagnostics", (HttpContext ctx) =>
            ctx.Connection.RemoteIpAddress is { } ip && IPAddress.IsLoopback(ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip)
                ? Results.Json(InputDiagnostics.Capture())
                : Results.StatusCode(403));
        if (allowManagement)
        {
            next.MapGet("/receiver/state", (HttpContext ctx) => IsLoopback(ctx)
                ? Results.Json(new { running = !paused, peer = Peer }) : Results.StatusCode(403));
            next.MapPost("/receiver/pause", (HttpContext ctx) =>
            {
                if (!IsLoopback(ctx)) return Results.StatusCode(403);
                paused = true;
                lock (controller) activeController?.Cancel();
                return Results.Ok();
            });
            next.MapPost("/receiver/resume", (HttpContext ctx) =>
            {
                if (!IsLoopback(ctx)) return Results.StatusCode(403);
                paused = false;
                return Results.Ok();
            });
        }
        next.Map("/ws", ctx => HandleSocket(ctx, stop.Token));
        try
        {
            udp = new UdpClient(new IPEndPoint(IPAddress.Parse(listenHost), discoveryPort));
            await next.StartAsync(stop.Token);
            app = next; cancellation = stop;
            discoveryTask = Task.Run(() => Discover(udp, stop.Token));
            InputDiagnostics.Record("receiver-started", new { movement = "relative-input-with-stall-recovery" });
            StatusChanged?.Invoke();
        }
        catch { udp?.Dispose(); udp = null; stop.Dispose(); await next.DisposeAsync(); throw; }
    }

    private async Task Discover(UdpClient socket, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var packet = await socket.ReceiveAsync(token);
                if (IsLocal(packet.RemoteEndPoint.Address) && Encoding.UTF8.GetString(packet.Buffer) == "LANTOUCHPAD_DISCOVER_V1")
                    await socket.SendAsync(Description, packet.RemoteEndPoint, token);
            }
        }
        catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or SocketException) { }
    }

    private async Task HandleSocket(HttpContext context, CancellationToken stopping)
    {
        if (paused) { context.Response.StatusCode = 503; return; }
        if (!context.WebSockets.IsWebSocketRequest) { context.Response.StatusCode = 400; return; }
        if (!await controller.WaitAsync(0, stopping)) { context.Response.StatusCode = 409; return; }
        using var input = new DesktopInput();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stopping, context.RequestAborted);
        lock (controller)
        {
            activeController = linked;
            if (paused) linked.Cancel();
        }
        WebSocket? socket = null;
        try
        {
            socket = await context.WebSockets.AcceptWebSocketAsync();
            Peer = context.Connection.RemoteIpAddress?.ToString(); StatusChanged?.Invoke();
            await socket.SendAsync(Description, WebSocketMessageType.Text, true, linked.Token);
            var buffer = new byte[65536];
            long window = Environment.TickCount64; int count = 0;
            while (socket.State == WebSocketState.Open)
            {
                int length = 0; WebSocketReceiveResult result;
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(linked.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                do
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer, length, buffer.Length - length), timeout.Token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", linked.Token);
                        return;
                    }
                    length += result.Count;
                    if (length == buffer.Length || result.MessageType != WebSocketMessageType.Text) return;
                } while (!result.EndOfMessage);
                if (Environment.TickCount64 - window >= 1000) { window = Environment.TickCount64; count = 0; }
                if (++count > 500) return;
                try
                {
                    using var document = JsonDocument.Parse(buffer.AsMemory(0, length));
                    var root = document.RootElement;
                    var type = root.GetProperty("type").GetString();
                    switch (type)
                    {
                        case "ping": await Reply(socket, new { type = "pong" }, linked.Token); break;
                        case "move": input.Move(Number(root, "dx"), Number(root, "dy")); break;
                        case "scroll": input.Scroll(Number(root, "dy")); break;
                        case "click": input.Click(root.GetProperty("button").GetString()!); break;
                        case "button": input.Button(root.GetProperty("button").GetString()!, root.GetProperty("down").GetBoolean(), !root.TryGetProperty("copy", out var copy) || copy.GetBoolean()); break;
                        case "release": input.ReleaseAll(); break;
                        case "key": input.Press(root.GetProperty("key").GetString()!); break;
                        case "text":
                            var text = root.GetProperty("text").GetString()!;
                            var id = root.GetProperty("id").GetString();
                            if (text.Length > 4000) throw new ArgumentException("Text is too long");
                            input.Text(text);
                            await Reply(socket, new { type = "textAck", id }, linked.Token);
                            break;
                        default: throw new ArgumentException("Unknown message");
                    }
                }
                catch (Exception e) when (e is JsonException or ArgumentException or KeyNotFoundException or InvalidOperationException or System.ComponentModel.Win32Exception or NullReferenceException)
                { await Reply(socket, new { type = "error", message = e.Message }, linked.Token); }
            }
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException or IOException) { }
        finally
        {
            input.ReleaseAll();
            lock (controller) activeController = null;
            if (socket != null && socket.State != WebSocketState.Closed) socket.Abort();
            socket?.Dispose(); Peer = null; controller.Release(); StatusChanged?.Invoke();
        }
    }
    private static bool IsLoopback(HttpContext context) => context.Connection.RemoteIpAddress is { } ip &&
        IPAddress.IsLoopback(ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip);
    private static double Number(JsonElement root, string key)
    {
        double value = root.GetProperty(key).GetDouble();
        return double.IsFinite(value) ? value : throw new ArgumentException("Non-finite movement");
    }
    private static Task Reply(WebSocket socket, object value, CancellationToken token) => socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(value), WebSocketMessageType.Text, true, token);
    public async Task StopAsync()
    {
        var current = app; if (current == null) return;
        app = null; cancellation!.Cancel(); udp?.Dispose();
        if (discoveryTask != null) await discoveryTask.ConfigureAwait(false);
        await current.StopAsync().ConfigureAwait(false); await current.DisposeAsync().ConfigureAwait(false);
        cancellation.Dispose(); cancellation = null; Peer = null; StatusChanged?.Invoke();
    }
}
