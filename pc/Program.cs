using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.Http.Json;
using System.ServiceProcess;
using System.Text.Json;

namespace LanTouchPad;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Contains("--service")) { ServiceBase.Run(new ReceiverService()); return; }
        if (args.Contains("--session-receiver"))
        {
            try { ReceiverService.RunReceiver().GetAwaiter().GetResult(); }
            catch (Exception error) { InputDiagnostics.Record("receiver-failed", new { message = error.Message }); Environment.ExitCode = 1; }
            return;
        }
        if (args.Length == 1 && args[0] is "--install-service" or "--uninstall-service" or "--stop-service" or "--start-service" or "--enable-startup" or "--disable-startup")
        {
            try
            {
                switch (args[0])
                {
                    case "--install-service": Startup.Install(); break;
                    case "--uninstall-service": Startup.Uninstall(); break;
                    case "--stop-service": Startup.StopService(); break;
                    case "--start-service": Startup.StartService(); break;
                    case "--enable-startup": Startup.Set(true); break;
                    case "--disable-startup": Startup.Set(false); break;
                }
            }
            catch (Exception error)
            {
                InputDiagnostics.Record("service-configuration-failed", new { command = args[0], message = error.Message });
                Environment.ExitCode = 1;
            }
            return;
        }
        using var mutex = new Mutex(true, "Local\\LanTouchPad.Desktop", out var first);
        using var show = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\LanTouchPad.Show");
        if (!first) { if (!args.Contains("--background")) show.Set(); return; }
        ApplicationConfiguration.Initialize();
        using var form = new MainForm(args.Contains("--background"));
        var registration = ThreadPool.RegisterWaitForSingleObject(show, (_, _) => form.ShowFromSignal(), null, Timeout.Infinite, false);
        try { Application.Run(form); } finally { registration.Unregister(null); }
    }
}

internal sealed class MainForm : Form
{
    private readonly RemoteServer server = new();
    private readonly bool serviceMode = Startup.Installed;
    private readonly HttpClient serviceClient = new() { BaseAddress = new Uri("http://127.0.0.1:45832"), Timeout = TimeSpan.FromSeconds(2) };
    private readonly System.Windows.Forms.Timer servicePoll = new() { Interval = 2000 };
    private bool receiverRunning, polling;
    private string? receiverPeer;
    private readonly NotifyIcon tray;
    private readonly Label status = new() { AutoSize = true };
    private readonly Label addresses = new() { AutoSize = true, MaximumSize = new Size(500, 0) };
    private readonly Label peer = new() { AutoSize = true };
    private readonly Button toggle = new() { Text = "暂停接收", AutoSize = true };
    private readonly CheckBox startup = new() { Text = "开机自动启动", AutoSize = true };
    private bool exiting;

    public MainForm(bool background)
    {
        Text = "LanTouchPad 1.0.4";
        ClientSize = new Size(560, 390);
        MinimumSize = new Size(560, 430);
        Font = new Font("Microsoft YaHei UI", 10);
        BackColor = Color.FromArgb(246, 248, 250);
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application;
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(28), WrapContents = false, AutoScroll = true };
        layout.Controls.Add(new Label { Text = "LanTouchPad", Font = new Font(Font.FontFamily, 23, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 0, 0, 18) });
        foreach (var control in new Control[] { status, addresses, peer, startup, toggle })
        { control.Margin = new Padding(0, 0, 0, 15); layout.Controls.Add(control); }
        layout.Controls.Add(new Label { Text = serviceMode ? "系统服务接收 · 支持登录及锁屏界面\n仅限可信局域网 · 无需配对验证" : "仅限可信局域网 · 无需配对验证\n安装系统服务后支持登录及锁屏界面", AutoSize = true, ForeColor = Color.DimGray });
        Controls.Add(layout);
        startup.Checked = Startup.Enabled;
        startup.CheckedChanged += (_, _) => { try { Startup.Set(startup.Checked); } catch (Exception e) { MessageBox.Show(e.Message, "无法修改开机启动"); } };
        var menu = new ContextMenuStrip();
        menu.Items.Add("打开 LanTouchPad", null, (_, _) => ShowWindow());
        menu.Items.Add("输入诊断日志", null, async (_, _) =>
        {
            try
            {
                string path = InputDiagnostics.LogPath;
                if (serviceMode)
                {
                    var diagnostics = await serviceClient.GetFromJsonAsync<JsonElement>("/diagnostics");
                    path = diagnostics.GetProperty("logPath").GetString()!;
                }
                if (File.Exists(path))
                {
                    var start = new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "notepad.exe")) { UseShellExecute = true };
                    start.ArgumentList.Add(path);
                    System.Diagnostics.Process.Start(start);
                }
            }
            catch (Exception e) { MessageBox.Show(e.Message, "无法打开诊断日志"); }
        });
        menu.Items.Add("退出并停止接收", null, async (_, _) =>
        {
            try
            {
                if (serviceMode) await Task.Run(Startup.StopService);
                exiting = true; Close();
            }
            catch (Exception error) { MessageBox.Show(error.Message, "无法停止接收"); }
        });
        tray = new NotifyIcon { Icon = Icon, Text = "LanTouchPad", ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => ShowWindow();
        server.StatusChanged += () => { if (IsHandleCreated && !IsDisposed) BeginInvoke(UpdateStatus); };
        toggle.Click += async (_, _) =>
        {
            toggle.Enabled = false;
            try
            {
                if (serviceMode)
                {
                    await Task.Run(Startup.StartService);
                    using var response = await serviceClient.PostAsync(receiverRunning ? "/receiver/pause" : "/receiver/resume", null);
                    response.EnsureSuccessStatusCode();
                    await PollService();
                }
                else if (server.Running) await server.StopAsync(); else await StartServer();
            }
            catch (Exception error) { MessageBox.Show(error.Message, "无法切换接收状态"); }
            finally { UpdateStatus(); toggle.Enabled = true; }
        };
        servicePoll.Tick += async (_, _) => await PollService();
        Shown += async (_, _) => { if (background) Hide(); await StartServer(); if (serviceMode) servicePoll.Start(); };
        FormClosing += (_, e) => { if (!exiting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
        FormClosed += (_, _) => { servicePoll.Dispose(); serviceClient.Dispose(); server.StopAsync().GetAwaiter().GetResult(); tray.Dispose(); };
        UpdateStatus();
    }

    private void ShowWindow() { Show(); WindowState = FormWindowState.Normal; Activate(); }
    public void ShowFromSignal() { if (IsHandleCreated && !IsDisposed) BeginInvoke(ShowWindow); }
    private async Task StartServer()
    {
        try
        {
            if (serviceMode)
            {
                await Task.Run(Startup.StartService);
                for (int i = 0; i < 20 && !await PollService(); i++) await Task.Delay(500);
            }
            else await server.StartAsync();
        }
        catch (Exception e) { MessageBox.Show($"启动失败，请检查端口 {RemoteServer.Port} / {RemoteServer.DiscoveryPort}。\n{e.Message}", "LanTouchPad"); }
        UpdateStatus();
    }
    private async Task<bool> PollService()
    {
        if (polling || IsDisposed) return false;
        polling = true;
        try
        {
            var state = await serviceClient.GetFromJsonAsync<JsonElement>("/receiver/state");
            if (IsDisposed) return false;
            receiverRunning = state.GetProperty("running").GetBoolean();
            receiverPeer = state.GetProperty("peer").GetString();
            return true;
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or ObjectDisposedException or JsonException)
        { receiverRunning = false; receiverPeer = null; return false; }
        finally { polling = false; if (!IsDisposed) UpdateStatus(); }
    }
    private void UpdateStatus()
    {
        bool running = serviceMode ? receiverRunning : server.Running;
        string? connectedPeer = serviceMode ? receiverPeer : server.Peer;
        status.Text = running ? "● 正在接收局域网连接" : "● 已暂停或服务未就绪";
        status.ForeColor = running ? Color.FromArgb(0, 130, 94) : Color.DimGray;
        addresses.Text = "本机地址：" + string.Join(" / ", NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address))
            .Select(a => a.Address.ToString()));
        peer.Text = connectedPeer == null ? "手机：等待连接" : $"手机：{connectedPeer}";
        toggle.Text = running ? "暂停接收" : "开始接收";
    }
}
