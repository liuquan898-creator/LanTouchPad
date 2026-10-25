using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

internal static class Program
{
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    private static int code;
    [STAThread]
    private static int Main()
    {
        Console.SetOut(new StreamWriter(Path.Combine(AppContext.BaseDirectory, "smoke-report.txt")) { AutoFlush = true });
        Console.SetError(Console.Out);
        ApplicationConfiguration.Initialize();
        using var form = new Form { Text = "LanTouchPad input verification - click here to start", Width = 600, Height = 320, StartPosition = FormStartPosition.CenterScreen, TopMost = true };
        var textbox = new TextBox { Multiline = true, Dock = DockStyle.Fill, Font = new Font("Microsoft YaHei UI", 16) };
        form.Controls.Add(textbox);
        form.Shown += async (_, _) =>
        {
            try
            {
                Console.WriteLine("Waiting for test window focus...");
                for (int i = 0; i < 900 && GetForegroundWindow() != form.Handle; i++) await Task.Delay(100);
                Check(GetForegroundWindow() == form.Handle, "test window is foreground");
                form.TopMost = false;
                textbox.Focus();
                await Verify(form, textbox); Console.WriteLine("PASS: all desktop integration checks");
            }
            catch (Exception e) { code = 1; Console.Error.WriteLine(e); }
            finally { form.Close(); }
        };
        Application.Run(form); return code;
    }
    private static void Check(bool result, string message) { if (!result) throw new Exception(message); Console.WriteLine("PASS: " + message); }
    private static async Task WaitFor(Func<bool> condition)
    {
        for (int i = 0; i < 60 && !condition(); i++) await Task.Delay(50);
    }
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
        Check(GetForegroundWindow() == form.Handle && textbox.Focused, "isolated input target has focus");
        await Send(socket, new { type = "text", text = "中文 Hello", id = "1" });
        Check((await Receive(socket)).GetProperty("type").GetString() == "textAck", "text delivery acknowledged");
        await Task.Delay(150);
        Check(textbox.Text == "中文 Hello", "Unicode text reached Windows textbox");
        await Send(socket, new { type = "key", key = "backspace" });
        await Task.Delay(150); Check(textbox.Text == "中文 Hell", "Backspace reached focused textbox");
        await Send(socket, new { type = "text", text = "\n第二行 😀", id = "2" }); await Receive(socket);
        await Task.Delay(150); Check(textbox.Text.Replace("\r", "") == "中文 Hell\n第二行 😀", "multiline and surrogate-pair Unicode input");

        using (var modal = new Form { Text = "Modal input verification", Width = 420, Height = 200, StartPosition = FormStartPosition.CenterParent })
        {
            var modalText = new TextBox { Dock = DockStyle.Top };
            modal.Controls.Add(modalText);
            Exception? modalError = null;
            modal.Shown += async (_, _) =>
            {
                try
                {
                    modal.Activate(); SetForegroundWindow(modal.Handle); modalText.Focus(); await Task.Delay(200);
                    Check(GetForegroundWindow() == modal.Handle && modalText.Focused, "elevated modal target has focus");
                    await Send(socket, new { type = "text", text = "modal input", id = "modal" });
                    await Receive(socket); await Task.Delay(150);
                    Check(modalText.Text == "modal input", "input reaches elevated modal dialog");
                    var previousCursor = Cursor.Position;
                    try
                    {
                        int modalClicks = 0;
                        modalText.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) modalClicks++; };
                        Cursor.Position = modalText.PointToScreen(new Point(20, 10));
                        await Send(socket, new { type = "move", dx = 5, dy = 0 });
                        await WaitFor(() => Cursor.Position != modalText.PointToScreen(new Point(20, 10)));
                        Check(Cursor.Position != modalText.PointToScreen(new Point(20, 10)), "pointer moves over elevated modal dialog");
                        await Send(socket, new { type = "click", button = "left" });
                        await WaitFor(() => modalClicks > 0);
                        Check(modalClicks == 1, $"click reaches elevated modal dialog (count={modalClicks})");
                    }
                    finally { Cursor.Position = previousCursor; }
                }
                catch (Exception error) { modalError = error; }
                finally { modal.Close(); }
            };
            modal.ShowDialog(form);
            if (modalError != null) throw modalError;
            form.Activate(); textbox.Focus();
        }

        var origin = Cursor.Position;
        try
        {
            var clipboard = Clipboard.GetDataObject();
            try
            {
                textbox.Text = "Select and automatically copy this text";
                textbox.Select(0,0);
                Cursor.Position = textbox.PointToScreen(new Point(3,10));
                await Send(socket, new { type = "button", button = "left", down = true });
                await Send(socket, new { type = "move", dx = 140, dy = 0 }); await Task.Delay(150);
                await Send(socket, new { type = "button", button = "left", down = false }); await Task.Delay(250);
                Check(textbox.SelectedText.Length > 0 && Clipboard.GetText() == textbox.SelectedText, "left drag selection automatically copies to clipboard");
                var copied = Clipboard.GetText();
                await Send(socket, new { type = "button", button = "left", down = true });
                await Send(socket, new { type = "move", dx = 40, dy = 0 });
                await Send(socket, new { type = "button", button = "left", down = false, copy = false }); await Task.Delay(200);
                Check(Clipboard.GetText() == copied, "canceled drag does not change clipboard");
            }
            finally { if (clipboard != null) { try { Clipboard.SetDataObject(clipboard, true); } catch (ExternalException) { } } }
            Cursor.Position = textbox.PointToScreen(new Point(100, 100));
            var before = Cursor.Position;
            await Send(socket, new { type = "move", dx = 15, dy = 10 }); await Task.Delay(150);
            Check(Cursor.Position != before, "pointer movement injected");
            Cursor.Position = before;
            await Task.Delay(SystemInformation.DoubleClickTime + 100);
            Check(GetForegroundWindow() == form.Handle, "click target remains foreground");
            int clicks = 0, rightClicks = 0, wheels = 0;
            textbox.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) clicks++; if (e.Button == MouseButtons.Right) rightClicks++; };
            textbox.MouseWheel += (_, _) => wheels++;
            await Send(socket, new { type = "click", button = "left" }); await WaitFor(() => clicks > 0);
            Check(clicks == 1, $"left click injected (count={clicks})");
            await Send(socket, new { type = "click", button = "right" }); await WaitFor(() => rightClicks > 0);
            Check(rightClicks == 1, $"right click injected (count={rightClicks})");
            // Dismiss the native context menu before testing scrolling.
            await Send(socket, new { type = "key", key = "escape" }); await Task.Delay(100);
            await Send(socket, new { type = "scroll", dy = 120 }); await WaitFor(() => wheels > 0);
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
