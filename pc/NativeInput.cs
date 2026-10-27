using System.ComponentModel;
using System.Runtime.InteropServices;

namespace LanTouchPad;

internal sealed class NativeInput
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Input { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyInput Key;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput { public int X, Y; public uint Data, Flags, Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)]
    private struct KeyInput { public ushort VirtualKey, Scan; public uint Flags, Time; public UIntPtr Extra; }
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] input, int size);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
    private double remainderX, remainderY, remainderScroll;
    private double leftDragDistance;
    private Point lastPosition;
    private bool trackingPosition, absoluteFallback;
    private double unconfirmedX, unconfirmedY, movementGain = 1;
    private long lastProgress, lastMovement;
    private bool copyLeftDrag = true;
    private readonly HashSet<string> held = [];
    public const string KeyNames = "enter,backspace,tab,escape,delete,left,right,up,down,home,end";
    private static readonly Dictionary<string, ushort> Keys = new()
    { ["enter"] = 0x0D, ["backspace"] = 8, ["tab"] = 9, ["escape"] = 0x1B, ["delete"] = 0x2E, ["left"] = 0x25, ["up"] = 0x26, ["right"] = 0x27, ["down"] = 0x28, ["home"] = 0x24, ["end"] = 0x23 };

    private static void Send(params Input[] values)
    {
        uint accepted = SendInput((uint)values.Length, values, Marshal.SizeOf<Input>());
        if (accepted != values.Length)
        {
            int error = Marshal.GetLastWin32Error();
            InputDiagnostics.Record("send-input-rejected", new { accepted, requested = values.Length, error });
            throw new Win32Exception(error, "Windows 暂时拒绝输入，详细状态已记录到电脑端诊断日志。");
        }
    }
    private static Input Mouse(uint flags, int x = 0, int y = 0, uint data = 0) => new() { Type = 0, Data = new() { Mouse = new() { X = x, Y = y, Data = data, Flags = flags } } };
    private static Input Key(ushort key, ushort scan, uint flags) => new() { Type = 1, Data = new() { Key = new() { VirtualKey = key, Scan = scan, Flags = flags } } };
    public void Move(double x, double y)
    {
        remainderX += Math.Clamp(x, -2000, 2000); remainderY += Math.Clamp(y, -2000, 2000);
        int dx = (int)remainderX, dy = (int)remainderY;
        remainderX -= dx; remainderY -= dy;
        if (dx != 0 || dy != 0)
        {
            if (!GetCursorPos(out var point)) throw CursorError("read-cursor");
            long now = Environment.TickCount64;
            if (!trackingPosition || now - lastMovement > 1000)
            {
                trackingPosition = true;
                absoluteFallback = false;
                unconfirmedX = unconfirmedY = 0;
                lastPosition = point;
                lastProgress = now;
            }
            if (point.X != lastPosition.X || point.Y != lastPosition.Y)
            {
                double requested = Math.Sqrt(unconfirmedX * unconfirmedX + unconfirmedY * unconfirmedY);
                double observed = Math.Sqrt(Math.Pow(point.X - (double)lastPosition.X, 2) + Math.Pow(point.Y - (double)lastPosition.Y, 2));
                if (!absoluteFallback && requested >= 4 && observed / requested is >= 0.5 and <= 8)
                    movementGain = movementGain * .5 + (observed / requested) * .5;
                absoluteFallback = false;
                unconfirmedX = unconfirmedY = 0;
                lastProgress = now;
            }
            lastPosition = point;
            lastMovement = now;
            if (!absoluteFallback)
            {
                // Preserve Windows pointer acceleration and the original relative movement path.
                Send(Mouse(1, dx, dy));
                unconfirmedX += dx; unconfirmedY += dy;
                if (now - lastProgress >= 100 && Math.Abs(unconfirmedX) + Math.Abs(unconfirmedY) >= 6)
                {
                    absoluteFallback = true;
                    InputDiagnostics.Record("cursor-recovery", new { movementGain });
                    PositionCursor(point, unconfirmedX * movementGain, unconfirmedY * movementGain);
                    unconfirmedX = unconfirmedY = 0;
                }
            }
            else PositionCursor(point, dx * movementGain, dy * movementGain);
            if (absoluteFallback && GetCursorPos(out var recovered)) lastPosition = recovered;
            if (held.Contains("left")) leftDragDistance += Math.Sqrt((double)dx * dx + (double)dy * dy);
        }
    }
    private void PositionCursor(Point point, double dx, double dy)
    {
        int left = GetSystemMetrics(76), top = GetSystemMetrics(77);
        int width = GetSystemMetrics(78), height = GetSystemMetrics(79);
        if (width <= 0 || height <= 0) throw new InvalidOperationException("无法读取桌面范围。");
        int targetX = (int)Math.Clamp((long)point.X + (long)Math.Round(Math.Clamp(dx, -2000, 2000)), left, (long)left + width - 1);
        int targetY = (int)Math.Clamp((long)point.Y + (long)Math.Round(Math.Clamp(dy, -2000, 2000)), top, (long)top + height - 1);
        if (!SetCursorPos(targetX, targetY)) throw CursorError("position-cursor");
        if (!GetCursorPos(out point)) throw CursorError("read-positioned-cursor");
        if (point.X != targetX || point.Y != targetY)
            InputDiagnostics.Record("cursor-constrained", new { targetX, targetY, actualX = point.X, actualY = point.Y });
        int absoluteX = (int)(((2L * (point.X - (long)left) + 1) * 65536) / (2L * width));
        int absoluteY = (int)(((2L * (point.Y - (long)top) + 1) * 65536) / (2L * height));
        Send(Mouse(0xE001, Math.Clamp(absoluteX, 0, 65535), Math.Clamp(absoluteY, 0, 65535)));
    }
    private static Win32Exception CursorError(string operation)
    {
        int error = Marshal.GetLastWin32Error();
        InputDiagnostics.Record(operation, new { error });
        return new Win32Exception(error, "无法访问当前输入桌面的光标，详细状态已记录到电脑端诊断日志。");
    }
    public void Scroll(double value)
    {
        remainderScroll += Math.Clamp(value, -2000, 2000);
        int delta = (int)remainderScroll; remainderScroll -= delta;
        if (delta != 0) Send(Mouse(0x0800, data: unchecked((uint)delta)));
    }
    public void Button(string button, bool down, bool copyAfterDrag = true)
    {
        if (button is not ("left" or "right")) throw new ArgumentException("Unknown button");
        if (down ? held.Contains(button) : !held.Contains(button)) return;
        if (button == "left" && down) copyLeftDrag = CanCopyDrag();
        var events = new List<Input> { Mouse(button == "left" ? (down ? 2u : 4u) : (down ? 8u : 16u)) };
        if (button == "left" && !down && copyAfterDrag && copyLeftDrag && leftDragDistance >= 6)
        {
            // Keep mouse-up and Copy in the same ordered Windows input batch.
            bool ctrlHeld = (GetAsyncKeyState(0x11) & 0x8000) != 0;
            if (!ctrlHeld) events.Add(Key(0x11, 0, 0));
            events.Add(Key(0x43, 0, 0)); events.Add(Key(0x43, 0, 2));
            if (!ctrlHeld) events.Add(Key(0x11, 0, 2));
        }
        Send(events.ToArray());
        if (down) held.Add(button); else held.Remove(button);
        if (button == "left") leftDragDistance = 0;
    }
    private static bool CanCopyDrag()
    {
        if (!GetCursorPos(out var point)) return true;
        var window = GetAncestor(WindowFromPoint(point), 2);
        if (window == IntPtr.Zero) return true;
        var position = new IntPtr(((point.Y & 0xffff) << 16) | (point.X & 0xffff));
        if (SendMessageTimeout(window, 0x84, IntPtr.Zero, position, 2, 50, out var hit) == IntPtr.Zero) return true;
        long area = hit.ToInt64();
        return area != 2 && area is not (>= 10 and <= 17);
    }
    public void Click(string button)
    {
        if (button is not ("left" or "right")) throw new ArgumentException("Unknown button");
        if (held.Contains(button)) return;
        Send(Mouse(button == "left" ? 2u : 8u), Mouse(button == "left" ? 4u : 16u));
    }
    public void Press(string name)
    {
        if (!Keys.TryGetValue(name, out var key)) throw new ArgumentException("Unknown key");
        var extended = key is >= 0x21 and <= 0x2E ? 1u : 0u;
        Send(Key(key, 0, extended), Key(key, 0, extended | 2));
    }
    public void Text(string text)
    {
        var values = new List<Input>();
        foreach (char ch in text.Replace("\r\n", "\n").Replace('\r', '\n'))
        {
            if (ch is '\n' or '\t') { ushort vk = ch == '\n' ? (ushort)13 : (ushort)9; values.Add(Key(vk, 0, 0)); values.Add(Key(vk, 0, 2)); }
            else { values.Add(Key(0, ch, 4)); values.Add(Key(0, ch, 6)); }
        }
        if (values.Count > 0) Send(values.ToArray());
    }
    public void ReleaseAll()
    {
        foreach (var button in held.ToArray()) { try { Button(button, false, false); } catch { held.Remove(button); } }
        remainderX = remainderY = remainderScroll = leftDragDistance = 0;
        trackingPosition = absoluteFallback = false;
        unconfirmedX = unconfirmedY = 0;
    }
}
