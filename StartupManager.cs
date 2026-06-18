using System.IO;
using System.Runtime.InteropServices;

namespace DesktopUsageAnalytics;

public sealed class StartupManager
{
    public string ShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup),
        "Keywise.lnk");

    public bool IsEnabled => File.Exists(ShortcutPath);

    public void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            CreateShortcut();
        }
        else if (File.Exists(ShortcutPath))
        {
            File.Delete(ShortcutPath);
        }
    }

    private void CreateShortcut()
    {
        var targetPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(ShortcutPath)!);
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null)
        {
            return;
        }

        object? shell = null;
        object? shortcut = null;
        try
        {
            shell = Activator.CreateInstance(shellType);
            shortcut = shellType.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, [ShortcutPath]);
            var shortcutType = shortcut!.GetType();
            shortcutType.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, shortcut, [targetPath]);
            shortcutType.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty, null, shortcut, [Path.GetDirectoryName(targetPath)]);
            shortcutType.InvokeMember("Description", System.Reflection.BindingFlags.SetProperty, null, shortcut, ["Keywise"]);
            shortcutType.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, shortcut, []);
        }
        finally
        {
            if (shortcut is not null)
            {
                Marshal.FinalReleaseComObject(shortcut);
            }

            if (shell is not null)
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }
    }
}
