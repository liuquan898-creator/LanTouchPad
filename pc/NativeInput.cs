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
    private double remainderX, remainderY, remainderScroll;
    private double leftDragDistance;
    private readonly HashSet<string> held = [];
    public const string KeyNames = "enter,backspace,tab,escape,delete,left,right,up,down,home,end";
    private static readonly Dictionary<string, ushort> Keys = new()
    { ["enter"] = 0x0D, ["backspace"] = 8, ["tab"] = 9, ["escape"] = 0x1B, ["delete"] = 0x2E, ["left"] = 0x25, ["up"] = 0x26, ["right"] = 0x27, ["down"] = 0x28, ["home"] = 0x24, ["end"] = 0x23 };

    private static void Send(params Input[] values)
    {
        if (SendInput((uint)values.Length, values, Marshal.SizeOf<Input>()) != values.Length)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows 拒绝输入，请检查目标程序是否以管理员身份运行。");
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
            Send(Mouse(1, dx, dy));
            if (held.Contains("left")) leftDragDistance += Math.Sqrt((double)dx * dx + (double)dy * dy);
        }
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
        var events = new List<Input> { Mouse(button == "left" ? (down ? 2u : 4u) : (down ? 8u : 16u)) };
        if (button == "left" && !down && copyAfterDrag && leftDragDistance >= 6)
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
    public void Click(string button) { Button(button, true); Button(button, false); }
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
    }
}
