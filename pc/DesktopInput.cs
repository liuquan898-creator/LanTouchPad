using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace LanTouchPad;

// A dedicated thread can follow the input desktop without owning any windows or hooks.
internal sealed class DesktopInput : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetThreadDesktop(IntPtr desktop);
    [DllImport("user32.dll")] private static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern IntPtr GetThreadDesktop(uint thread);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetUserObjectInformation(IntPtr handle, int index, StringBuilder value, int length, out int needed);
    private readonly BlockingCollection<(Action<NativeInput> Action, TaskCompletionSource Completion)> commands = new(32);
    private readonly Thread worker;

    public DesktopInput()
    {
        worker = new Thread(Run) { IsBackground = true, Name = "LanTouchPad desktop input" };
        worker.Start();
    }

    private void Run()
    {
        var input = new NativeInput();
        var originalDesktop = GetThreadDesktop(GetCurrentThreadId());
        IntPtr desktop = IntPtr.Zero;
        string? name = null;
        try
        {
            foreach (var command in commands.GetConsumingEnumerable())
            {
                try
                {
                    var next = OpenInputDesktop(0, false, 0x10000000);
                    if (next == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "无法访问当前输入桌面。");
                    var buffer = new StringBuilder(256);
                    if (!GetUserObjectInformation(next, 2, buffer, buffer.Capacity * 2, out _))
                    {
                        int error = Marshal.GetLastWin32Error();
                        CloseDesktop(next);
                        throw new Win32Exception(error);
                    }
                    if (!string.Equals(name, buffer.ToString(), StringComparison.OrdinalIgnoreCase))
                    {
                        input.ReleaseAll();
                        if (!SetThreadDesktop(next))
                        {
                            int error = Marshal.GetLastWin32Error();
                            CloseDesktop(next);
                            throw new Win32Exception(error, "无法切换输入桌面。");
                        }
                        if (desktop != IntPtr.Zero) CloseDesktop(desktop);
                        desktop = next;
                        name = buffer.ToString();
                        InputDiagnostics.Record("input-desktop-changed", new { desktop = name });
                    }
                    else CloseDesktop(next);
                    command.Action(input);
                    command.Completion.SetResult();
                }
                catch (Exception error) { command.Completion.SetException(error); }
            }
        }
        finally
        {
            input.ReleaseAll();
            if (desktop != IntPtr.Zero)
            {
                SetThreadDesktop(originalDesktop);
                CloseDesktop(desktop);
            }
        }
    }

    private void Invoke(Action<NativeInput> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        commands.Add((action, completion));
        completion.Task.GetAwaiter().GetResult();
    }
    public void Move(double x, double y) => Invoke(input => input.Move(x, y));
    public void Scroll(double value) => Invoke(input => input.Scroll(value));
    public void Click(string button) => Invoke(input => input.Click(button));
    public void Button(string button, bool down, bool copy) => Invoke(input => input.Button(button, down, copy));
    public void Press(string key) => Invoke(input => input.Press(key));
    public void Text(string text) => Invoke(input => input.Text(text));
    public void ReleaseAll()
    {
        try { Invoke(input => input.ReleaseAll()); }
        catch (Win32Exception) { /* Disposal also releases on the previously attached desktop. */ }
    }
    public void Dispose()
    {
        commands.CompleteAdding();
        worker.Join();
        commands.Dispose();
    }
}
