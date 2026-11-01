using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
using System.ServiceProcess;

namespace LanTouchPad;

internal static class Startup
{
    private static string UserSid => WindowsIdentity.GetCurrent().User!.Value;
    private static string TaskName => "LanTouchPad-" + UserSid;
    private static dynamic Connect()
    {
        dynamic service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!;
        service.Connect();
        return service;
    }

    public static bool Enabled
    {
        get
        {
            if (Installed)
            {
                using var receiver = new ServiceController(ReceiverService.Name);
                return receiver.StartType == ServiceStartMode.Automatic;
            }
            dynamic service = Connect();
            try
            {
                dynamic folder = service.GetFolder(@"\");
                try
                {
                    dynamic task = folder.GetTask(TaskName);
                    try { return (bool)task.Enabled; }
                    finally { Marshal.FinalReleaseComObject(task); }
                }
                catch (COMException e) when (e.HResult == unchecked((int)0x80070002)) { return false; }
                finally { Marshal.FinalReleaseComObject(folder); }
            }
            finally { Marshal.FinalReleaseComObject(service); }
        }
    }

    public static void Set(bool enabled)
    {
        if (Installed) Sc("config", ReceiverService.Name, "start=", enabled ? "auto" : "demand");
        SetTrayTask(enabled);
    }

    public static bool Installed
    {
        get
        {
            using var receiver = new ServiceController(ReceiverService.Name);
            try { _ = receiver.Status; return true; }
            catch (InvalidOperationException e) when (e.InnerException is Win32Exception { NativeErrorCode: 1060 }) { return false; }
        }
    }

    private static void Sc(params string[] arguments)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "sc.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd(), error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException($"Windows service operation failed ({process.ExitCode}): {output} {error}");
    }

    public static void Install()
    {
        if (!Installed)
            Sc("create", ReceiverService.Name, "binPath=", $"\"{Environment.ProcessPath}\" --service", "start=", "auto", "obj=", "LocalSystem", "DisplayName=", "LanTouchPad");
        else
            Sc("config", ReceiverService.Name, "binPath=", $"\"{Environment.ProcessPath}\" --service", "obj=", "LocalSystem");
        Sc("description", ReceiverService.Name, "LAN touchpad receiver for the local Windows console, including the sign-in screen.");
        Sc("failure", ReceiverService.Name, "reset=", "86400", "actions=", "restart/5000/restart/15000/restart/30000");
    }

    public static void StartService()
    {
        using var receiver = new ServiceController(ReceiverService.Name);
        if (receiver.Status == ServiceControllerStatus.StopPending) receiver.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
        receiver.Refresh();
        if (receiver.Status == ServiceControllerStatus.Stopped) receiver.Start();
        receiver.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
    }

    public static void StopService()
    {
        if (!Installed) return;
        using var receiver = new ServiceController(ReceiverService.Name);
        if (receiver.Status == ServiceControllerStatus.Stopped) return;
        if (receiver.Status != ServiceControllerStatus.StopPending) receiver.Stop();
        receiver.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
    }

    public static void Uninstall()
    {
        SetTrayTask(false);
        if (Installed) { StopService(); Sc("delete", ReceiverService.Name); }
    }

    private static void SetTrayTask(bool enabled)
    {
        dynamic service = Connect();
        try
        {
            dynamic folder = service.GetFolder(@"\");
            try
            {
                if (!enabled)
                {
                    try { folder.DeleteTask(TaskName, 0); }
                    catch (COMException e) when (e.HResult == unchecked((int)0x80070002)) { }
                }
                else
                {
                    dynamic definition = service.NewTask(0);
                    try
                    {
                        definition.RegistrationInfo.Description = "Open the LanTouchPad tray UI when this user signs in; the Windows service receives input independently.";
                        definition.Principal.UserId = UserSid;
                        definition.Principal.LogonType = 3; // InteractiveToken, no stored password.
                        definition.Principal.RunLevel = 1;
                        dynamic trigger = definition.Triggers.Create(9);
                        trigger.UserId = UserSid;
                        dynamic action = definition.Actions.Create(0);
                        action.Path = Environment.ProcessPath!;
                        action.Arguments = "--background";
                        action.WorkingDirectory = AppContext.BaseDirectory;
                        definition.Settings.ExecutionTimeLimit = "PT0S";
                        definition.Settings.DisallowStartIfOnBatteries = false;
                        definition.Settings.StopIfGoingOnBatteries = false;
                        definition.Settings.StartWhenAvailable = true;
                        definition.Settings.MultipleInstances = 2;
                        dynamic registered = folder.RegisterTaskDefinition(TaskName, definition, 6, UserSid, null, 3, null);
                        Marshal.FinalReleaseComObject(registered);
                        Marshal.FinalReleaseComObject(action);
                        Marshal.FinalReleaseComObject(trigger);
                    }
                    finally { Marshal.FinalReleaseComObject(definition); }
                }
                using var oldKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                oldKey?.DeleteValue("LanTouchPad", false);
            }
            finally { Marshal.FinalReleaseComObject(folder); }
        }
        finally { Marshal.FinalReleaseComObject(service); }
    }
}
