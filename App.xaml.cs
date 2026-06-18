namespace DesktopUsageAnalytics;

public partial class App : System.Windows.Application
{
    private static Mutex? appMutex;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        appMutex = new Mutex(true, "KeywiseAppMutex", out var createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        base.OnStartup(e);
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        appMutex?.ReleaseMutex();
        appMutex?.Dispose();
        base.OnExit(e);
    }
}
