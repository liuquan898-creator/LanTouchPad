using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

internal static class Program
{
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    private static int code;
    [STAThread]
    private static int Main()
    {
        Console.SetOut(new StreamWriter(Path.Combine(AppContext.BaseDirectory, "smoke-report.txt")) { AutoFlush = true });
        Console.SetError(Console.Out);
        ApplicationConfiguration.Initialize();
        using var form = new Form { Text = "LanTouchPad input verification", Width = 600, Height = 320, StartPosition = FormStartPosition.CenterScreen };
        var textbox = new TextBox { Multiline = true, Dock = DockStyle.Fill, Font = new Font("Microsoft YaHei UI", 16) };
        form.Controls.Add(textbox);
        form.Shown += async (_, _) =>
        {
            try
            {
                Console.WriteLine("Waiting for test window focus...");
                for (int i = 0; i < 450 && Form.ActiveForm != form; i++) await Task.Delay(100);
                textbox.Focus();
                await Verify(form, textbox); Console.WriteLine("PASS: all desktop integration checks");
            }
            catch (Exception e) { code = 1; Console.Error.WriteLine(e); }
            finally { form.Close(); }
        };
        Application.Run(form); return code;
    }
    private static void Check(bool result, string message) { if (!result) throw new Exception(message); Console.WriteLine("PASS: " + message); }
    private static async Task Send(ClientWebSocket socket, object data) => await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(data), WebSocketMessageType.Text, true, CancellationToken.None);
    private static async Task<JsonElement> Receive(ClientWebSocket socket)
    {
        using var timeout = new CancellationTokenSource(4000);
        var bytes = new byte[65536]; var result = await socket.ReceiveAsync(bytes, timeout.Token);
        return JsonDocument.Parse(bytes.AsMemory(0, result.Count)).RootElement.Clone();
    }
    private static async Task Verify(Form form, TextBox textbox)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        var info = await http.GetStringAsync("http://127.0.0.1:45832/info");
        Check(info.Contains("LanTouchPad"), "HTTP discovery metadata");
        using var browser = new HttpRequestMessage(HttpMethod.Get, "http://127.0.0.1:45832/info");
        browser.Headers.Add("Origin", "https://example.com");
        Check((await http.SendAsync(browser)).StatusCode == HttpStatusCode.Forbidden, "browser Origin rejected");
        using var udp = new UdpClient(0);
        await udp.SendAsync(Encoding.UTF8.GetBytes("LANTOUCHPAD_DISCOVER_V1"), new IPEndPoint(IPAddress.Loopback, 45831));
        var discovery = await udp.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(3));
        Check(Encoding.UTF8.GetString(discovery.Buffer).Contains("LanTouchPad"), "UDP device discovery");

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri("ws://127.0.0.1:45832/ws"), CancellationToken.None);
        Check((await Receive(socket)).GetProperty("protocol").GetInt32() == 1, "WebSocket protocol handshake");
        using var second = new ClientWebSocket();
        bool rejected = false;
        try { await second.ConnectAsync(new Uri("ws://127.0.0.1:45832/ws"), CancellationToken.None); } catch (WebSocketException) { rejected = true; }
        Check(rejected, "second controller rejected");

        form.Activate(); SetForegroundWindow(form.Handle); textbox.Focus();
        await Task.Delay(350);
        Check(Form.ActiveForm == form && textbox.Focused, "isolated input target has focus");
        await Send(socket, new { type = "text", text = "中文 Hello", id = "1" });
        Check((await Receive(socket)).GetProperty("type").GetString() == "textAck", "text delivery acknowledged");
        await Task.Delay(150);
        Check(textbox.Text == "中文 Hello", "Unicode text reached Windows textbox");
        await Send(socket, new { type = "key", key = "backspace" });
        await Task.Delay(150); Check(textbox.Text == "中文 Hell", "Backspace reached focused textbox");
        await Send(socket, new { type = "text", text = "\n第二行 😀", id = "2" }); await Receive(socket);
        await Task.Delay(150); Check(textbox.Text.Replace("\r", "") == "中文 Hell\n第二行 😀", "multiline and surrogate-pair Unicode input");

        var origin = Cursor.Position;
        try
        {
            Cursor.Position = textbox.PointToScreen(new Point(100, 100));
            var before = Cursor.Position;
            await Send(socket, new { type = "move", dx = 15, dy = 10 }); await Task.Delay(150);
            Check(Cursor.Position != before, "pointer movement injected");
            Cursor.Position = before;
            int clicks = 0, rightClicks = 0, wheels = 0;
            textbox.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) clicks++; if (e.Button == MouseButtons.Right) rightClicks++; };
            textbox.MouseWheel += (_, _) => wheels++;
            await Send(socket, new { type = "click", button = "left" }); await Task.Delay(150);
            Check(clicks == 1, "left click injected");
            await Send(socket, new { type = "click", button = "right" }); await Task.Delay(150);
            Check(rightClicks == 1, "right click injected");
            // Dismiss the native context menu before testing scrolling.
            await Send(socket, new { type = "key", key = "escape" }); await Task.Delay(100);
            await Send(socket, new { type = "scroll", dy = 120 }); await Task.Delay(150);
            Check(wheels > 0, "wheel scrolling injected");
            await Send(socket, new { type = "button", button = "left", down = true }); await Task.Delay(100);
            Check((GetAsyncKeyState(1) & 0x8000) != 0, "drag button held");
            socket.Abort(); await Task.Delay(500);
            Check((GetAsyncKeyState(1) & 0x8000) == 0, "disconnect releases held button");
        }
        finally { Cursor.Position = origin; }

        using var reconnect = new ClientWebSocket();
        await reconnect.ConnectAsync(new Uri("ws://127.0.0.1:45832/ws"), CancellationToken.None); await Receive(reconnect);
        await reconnect.SendAsync(Encoding.UTF8.GetBytes("{"), WebSocketMessageType.Text, true, CancellationToken.None);
        Check((await Receive(reconnect)).GetProperty("type").GetString() == "error", "malformed messages handled without crashing");
        await Send(reconnect, new { type = "move", dx = "invalid", dy = 0 });
        Check((await Receive(reconnect)).GetProperty("type").GetString() == "error", "invalid input schema handled");
        await Send(reconnect, new { type = "text", text = new string('a', 4001), id = "oversize" });
        Check((await Receive(reconnect)).GetProperty("type").GetString() == "error", "oversize text rejected");
        await Send(reconnect, new { type = "ping" });
        Check((await Receive(reconnect)).GetProperty("type").GetString() == "pong", "service remains responsive after errors");
        await reconnect.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(3));
    }
}
