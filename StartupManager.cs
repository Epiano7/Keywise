using System.IO;

namespace DesktopUsageAnalytics;

public sealed class StartupManager
{
    public string ShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup),
        "Keywise.lnk");

    public bool IsEnabled => File.Exists(ShortcutPath);

    public void SetEnabled(bool enabled)
    {
        if (!enabled && File.Exists(ShortcutPath))
        {
            File.Delete(ShortcutPath);
        }

        // Creating the .lnk belongs in the installer phase so the prototype
        // cannot silently register itself for startup.
    }
}
