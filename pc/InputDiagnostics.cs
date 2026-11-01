using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace LanTouchPad;

internal static class InputDiagnostics
{
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct CursorInfo { public int Size, Flags; public IntPtr Cursor; public Point Position; }
    [DllImport("user32.dll")] private static extern bool GetCursorInfo(ref CursorInfo info);
    [DllImport("user32.dll")] private static extern bool GetClipCursor(out Rect rect);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr OpenDesktop(string name, uint flags, bool inherit, uint access);
    [DllImport("user32.dll")] private static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetUserObjectInformation(IntPtr obj, int index, StringBuilder name, int length, out int needed);
    private static readonly object Gate = new();
    private static long lastRecord = long.MinValue;
    private static readonly string LogDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LanTouchPad");
    public static string LogPath => Path.Combine(LogDirectory, "input-diagnostics.jsonl");

    public static object Capture()
    {
        var cursor = new CursorInfo { Size = Marshal.SizeOf<CursorInfo>() };
        bool cursorRead = GetCursorInfo(ref cursor), clipRead = GetClipCursor(out var clip);
        GetWindowThreadProcessId(GetForegroundWindow(), out var foregroundId);
        string? foreground = null;
        try { using var process = Process.GetProcessById((int)foregroundId); foreground = process.ProcessName; }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
        var desktop = OpenInputDesktop(0, false, 0x81);
        int desktopError = desktop == IntPtr.Zero ? Marshal.GetLastWin32Error() : 0;
        var desktopName = new StringBuilder(256);
        if (desktop != IntPtr.Zero)
        {
            try { GetUserObjectInformation(desktop, 2, desktopName, 512, out _); }
            finally { CloseDesktop(desktop); }
        }
        using var identity = WindowsIdentity.GetCurrent();
        var secureDesktop = OpenDesktop("Winlogon", 0, false, 0x10000000);
        bool secureDesktopAccess = secureDesktop != IntPtr.Zero;
        int secureDesktopError = secureDesktopAccess ? 0 : Marshal.GetLastWin32Error();
        if (secureDesktopAccess) CloseDesktop(secureDesktop);
        return new
        {
            time = DateTimeOffset.Now,
            version = typeof(Program).Assembly.GetName().Version?.ToString(),
            logPath = LogPath,
            system = identity.IsSystem,
            secureDesktopAccess, secureDesktopError,
            session = Process.GetCurrentProcess().SessionId,
            administrator = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator),
            desktop = desktopName.ToString(), desktopError,
            foregroundProcess = foreground, foregroundPid = foregroundId,
            cursorRead, cursorFlags = cursor.Flags, x = cursor.Position.X, y = cursor.Position.Y,
            clipRead, clipLeft = clip.Left, clipTop = clip.Top, clipRight = clip.Right, clipBottom = clip.Bottom,
        };
    }

    public static void Record(string operation, object? details = null)
    {
        lock (Gate)
        {
            long now = Environment.TickCount64;
            if (lastRecord != long.MinValue && now - lastRecord < 2000) return;
            lastRecord = now;
            try
            {
                Directory.CreateDirectory(LogDirectory);
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 1_048_576)
                    File.Move(LogPath, LogPath + ".previous", true);
                File.AppendAllText(LogPath, JsonSerializer.Serialize(new { operation, details, state = Capture() }) + Environment.NewLine);
            }
            catch (Exception) { }
        }
    }
}
