using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace LanTouchPad;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Contains("--enable-startup")) { Startup.Set(true); return; }
        if (args.Contains("--disable-startup")) { Startup.Set(false); return; }
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
    private readonly NotifyIcon tray;
    private readonly Label status = new() { AutoSize = true };
    private readonly Label addresses = new() { AutoSize = true, MaximumSize = new Size(500, 0) };
    private readonly Label peer = new() { AutoSize = true };
    private readonly Button toggle = new() { Text = "暂停接收", AutoSize = true };
    private readonly CheckBox startup = new() { Text = "开机自动启动", AutoSize = true };
    private bool exiting;

    public MainForm(bool background)
    {
        Text = "LanTouchPad 1.0.1";
        ClientSize = new Size(560, 390);
        MinimumSize = new Size(560, 430);
        Font = new Font("Microsoft YaHei UI", 10);
        BackColor = Color.FromArgb(246, 248, 250);
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application;
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(28), WrapContents = false, AutoScroll = true };
        layout.Controls.Add(new Label { Text = "LanTouchPad", Font = new Font(Font.FontFamily, 23, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 0, 0, 18) });
        foreach (var control in new Control[] { status, addresses, peer, startup, toggle })
        { control.Margin = new Padding(0, 0, 0, 15); layout.Controls.Add(control); }
        layout.Controls.Add(new Label { Text = "仅限可信局域网 · 无需配对验证\n关闭窗口后继续在托盘运行", AutoSize = true, ForeColor = Color.DimGray });
        Controls.Add(layout);
        startup.Checked = Startup.Enabled;
        startup.CheckedChanged += (_, _) => { try { Startup.Set(startup.Checked); } catch (Exception e) { MessageBox.Show(e.Message, "无法修改开机启动"); } };
        var menu = new ContextMenuStrip();
        menu.Items.Add("打开 LanTouchPad", null, (_, _) => ShowWindow());
        menu.Items.Add("退出", null, (_, _) => { exiting = true; Close(); });
        tray = new NotifyIcon { Icon = Icon, Text = "LanTouchPad", ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => ShowWindow();
        server.StatusChanged += () => { if (IsHandleCreated && !IsDisposed) BeginInvoke(UpdateStatus); };
        toggle.Click += async (_, _) =>
        {
            toggle.Enabled = false;
            if (server.Running) await server.StopAsync(); else await StartServer();
            UpdateStatus(); toggle.Enabled = true;
        };
        Shown += async (_, _) => { if (background) Hide(); await StartServer(); };
        FormClosing += (_, e) => { if (!exiting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
        FormClosed += (_, _) => { server.StopAsync().GetAwaiter().GetResult(); tray.Dispose(); };
        UpdateStatus();
    }

    private void ShowWindow() { Show(); WindowState = FormWindowState.Normal; Activate(); }
    public void ShowFromSignal() { if (IsHandleCreated && !IsDisposed) BeginInvoke(ShowWindow); }
    private async Task StartServer()
    {
        try { await server.StartAsync(); }
        catch (Exception e) { MessageBox.Show($"启动失败，请检查端口 {RemoteServer.Port} / {RemoteServer.DiscoveryPort}。\n{e.Message}", "LanTouchPad"); }
        UpdateStatus();
    }
    private void UpdateStatus()
    {
        status.Text = server.Running ? "● 正在接收局域网连接" : "● 已暂停";
        status.ForeColor = server.Running ? Color.FromArgb(0, 130, 94) : Color.DimGray;
        addresses.Text = "本机地址：" + string.Join(" / ", NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address))
            .Select(a => a.Address.ToString()));
        peer.Text = server.Peer == null ? "手机：等待连接" : $"手机：{server.Peer}";
        toggle.Text = server.Running ? "暂停接收" : "开始接收";
    }
}
