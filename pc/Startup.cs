using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;

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
                        definition.RegistrationInfo.Description = "Start LanTouchPad with elevated input access when this user signs in.";
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
