using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace Troly.WinAgent.App;

/// <summary>
/// Custom entry point (DISABLE_XAML_GENERATED_MAIN) implementing single-instance
/// activation. WinUI 3 apps are multi-instanced by default; we register a key with
/// AppInstance and redirect later launches to the running instance BEFORE creating
/// any window (per Windows App SDK guidance).
/// </summary>
public static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        if (!IsPrimaryInstance())
        {
            // Activation was redirected to the already-running instance; exit quietly.
            return 0;
        }

        Application.Start(p =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
        return 0;
    }

    private static bool IsPrimaryInstance()
    {
        var keyInstance = AppInstance.FindOrRegisterForKey("Troly.WinAgent.SingleInstance");
        if (keyInstance.IsCurrent)
        {
            return true;
        }

        var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        keyInstance.RedirectActivationToAsync(activation).AsTask().GetAwaiter().GetResult();
        return false;
    }
}
