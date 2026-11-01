using System.ServiceProcess;
using System.Security.Principal;

namespace LanTouchPad;

internal sealed class ReceiverService : ServiceBase
{
    public const string Name = "LanTouchPad";
    internal const string StopEvent = @"Global\LanTouchPad.ReceiverStop";
    private readonly CancellationTokenSource stopping = new();
    private Task? monitor;
    public ReceiverService() { ServiceName = Name; CanShutdown = true; }
    protected override void OnStart(string[] args) => monitor = Task.Run(Monitor);
    protected override void OnStop()
    {
        stopping.Cancel();
        monitor?.GetAwaiter().GetResult();
    }
    protected override void OnShutdown() => OnStop();

    private async Task Monitor()
    {
        using var signal = new EventWaitHandle(false, EventResetMode.ManualReset, StopEvent);
        ConsoleSessionProcess? receiver = null;
        try
        {
            while (!stopping.IsCancellationRequested)
            {
                uint session = ConsoleSessionProcess.WTSGetActiveConsoleSessionId();
                if (receiver != null && (receiver.Session != session || receiver.Exited))
                {
                    receiver.Stop(signal); receiver.Dispose(); receiver = null;
                }
                if (receiver == null && session is not (0 or uint.MaxValue))
                {
                    try
                    {
                        signal.Reset();
                        receiver = new ConsoleSessionProcess(session);
                        InputDiagnostics.Record("console-receiver-started", new { session });
                    }
                    catch (Exception error) { InputDiagnostics.Record("console-receiver-failed", new { message = error.Message }); }
                }
                await Task.Delay(2000, stopping.Token);
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
        finally
        {
            if (receiver != null) { receiver.Stop(signal); receiver.Dispose(); }
        }
    }

    public static async Task RunReceiver()
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (!identity.IsSystem) throw new InvalidOperationException("The session receiver must be started by the Windows service.");
        using var signal = EventWaitHandle.OpenExisting(StopEvent);
        var server = new RemoteServer(allowManagement: true);
        try
        {
            await server.StartAsync();
            await Task.Run(() => signal.WaitOne());
        }
        finally { await server.StopAsync(); }
    }
}
