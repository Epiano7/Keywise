using Microsoft.Win32;
using System.Windows;
using System.Windows.Threading;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace DesktopUsageAnalytics;

public partial class MainWindow : Window
{
    private readonly UsageStore store = new();
    private readonly UsageAggregator aggregator;
    private readonly StartupManager startupManager = new();
    private readonly DispatcherTimer refreshTimer = new();
    private readonly Forms.NotifyIcon trayIcon;
    private readonly WindowsInputMonitor inputMonitor;

    public MainWindow()
    {
        aggregator = new UsageAggregator(store);
        InitializeComponent();
        ThemeManager.Apply(Resources);
        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
        inputMonitor = new WindowsInputMonitor(bucket =>
        {
            Dispatcher.Invoke(() =>
            {
                aggregator.Increment(bucket);
                RefreshDashboard();
            });
        });

        DataPathText.Text = store.DataPath;
        StartAtLoginCheckBox.IsChecked = startupManager.IsEnabled;
        SelectLanguage(aggregator.Snapshot.Language);

        trayIcon = new Forms.NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "Keywise",
            Visible = true,
            ContextMenuStrip = BuildTrayMenu()
        };
        trayIcon.DoubleClick += (_, _) => ShowDashboard();

        refreshTimer.Interval = TimeSpan.FromSeconds(1);
        refreshTimer.Tick += (_, _) => RefreshDashboard();
        refreshTimer.Start();

        StartInputMonitor();
        RefreshDashboard();
    }

    private static Drawing.Icon LoadTrayIcon()
    {
        if (!string.IsNullOrWhiteSpace(Environment.ProcessPath))
        {
            return Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath) ?? Drawing.SystemIcons.Application;
        }

        return Drawing.SystemIcons.Application;
    }

    private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.VisualStyle)
        {
            Dispatcher.Invoke(() => ThemeManager.Apply(Resources));
        }
    }

    private Forms.ContextMenuStrip BuildTrayMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open dashboard", null, (_, _) => ShowDashboard());
        menu.Items.Add("Pause/resume tracking", null, (_, _) =>
        {
            aggregator.SetTrackingEnabled(!aggregator.TrackingEnabled);
            RefreshDashboard();
        });
        menu.Items.Add("Save counts", null, (_, _) => aggregator.Persist());
        menu.Items.Add("Quit", null, (_, _) =>
        {
            trayIcon.Visible = false;
            Close();
        });
        return menu;
    }

    private void StartInputMonitor()
    {
        if (!inputMonitor.Start())
        {
            System.Windows.MessageBox.Show(
                inputMonitor.LastError ?? "Unable to install global input hooks.",
                "Keywise");
        }
    }

    private void ShowDashboard()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void RefreshDashboard()
    {
        var snapshot = aggregator.Snapshot;
        var keys = snapshot.Counters
            .Where(item => item.Key.StartsWith("Key.", StringComparison.Ordinal))
            .OrderByDescending(item => item.Value)
            .ToList();
        var mouseTotal = GetCounter("Mouse.Left") + GetCounter("Mouse.Right") + GetCounter("Mouse.Middle");
        var keyTotal = keys.Sum(item => item.Value);
        var activeSeconds = snapshot.ActiveTrackingSeconds + (long)aggregator.CurrentSessionDuration.TotalSeconds;
        var activeHours = Math.Max(activeSeconds / 3600.0, 0.001);

        StatusText.Text = aggregator.TrackingEnabled ? "Tracking enabled" : "Paused";
        TrackingToggleButton.Content = aggregator.TrackingEnabled ? "Pause tracking" : "Enable tracking";
        ActiveTimeText.Text = FormatDuration(TimeSpan.FromSeconds(activeSeconds));
        SessionText.Text = FormatDuration(aggregator.CurrentSessionDuration);
        KeyTotalText.Text = keyTotal.ToString("N0");
        MouseTotalText.Text = mouseTotal.ToString("N0");
        LeftClickText.Text = GetCounter("Mouse.Left").ToString("N0");
        RightClickText.Text = GetCounter("Mouse.Right").ToString("N0");
        KeysPerHourText.Text = (keyTotal / activeHours).ToString("N0");
        TopKeyCountText.Text = $"{keys.Count:N0} tracked";
        TopKeysList.ItemsSource = keys.Take(8).Select(item => $"{item.Key.Replace("Key.", "")}: {item.Value:N0}");
    }

    private long GetCounter(string name) => aggregator.Snapshot.Counters.GetValueOrDefault(name);

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours}h {duration.Minutes}m";
        }

        return $"{Math.Max(0, duration.Minutes)}m {duration.Seconds}s";
    }

    private void TrackingToggleButton_Click(object sender, RoutedEventArgs e)
    {
        aggregator.SetTrackingEnabled(!aggregator.TrackingEnabled);
        RefreshDashboard();
    }

    private void SimulateA_Click(object sender, RoutedEventArgs e)
    {
        aggregator.Increment(InputBucket.Key("A"));
        RefreshDashboard();
    }

    private void SimulateEnter_Click(object sender, RoutedEventArgs e)
    {
        aggregator.Increment(InputBucket.Key("Enter"));
        RefreshDashboard();
    }

    private void SimulateLeft_Click(object sender, RoutedEventArgs e)
    {
        aggregator.Increment(InputBucket.Mouse(MouseButtonBucket.Left));
        RefreshDashboard();
    }

    private void SimulateRight_Click(object sender, RoutedEventArgs e)
    {
        aggregator.Increment(InputBucket.Mouse(MouseButtonBucket.Right));
        RefreshDashboard();
    }

    private void SaveNow_Click(object sender, RoutedEventArgs e)
    {
        aggregator.Persist();
        System.Windows.MessageBox.Show("Aggregate counters saved locally.", "Keywise");
    }

    private void ResetCounts_Click(object sender, RoutedEventArgs e)
    {
        if (System.Windows.MessageBox.Show("Reset aggregate counts and active tracking time?", "Reset counts", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
        {
            aggregator.ResetCounts();
            RefreshDashboard();
        }
    }

    private void DeleteLocalData_Click(object sender, RoutedEventArgs e)
    {
        if (System.Windows.MessageBox.Show("Delete all local prototype data?", "Delete local data", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
        {
            aggregator.DeleteLocalData();
            RefreshDashboard();
        }
    }

    private void StartAtLoginCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        var enabled = StartAtLoginCheckBox.IsChecked == true;
        startupManager.SetEnabled(enabled);
        aggregator.Snapshot.StartAtLogin = startupManager.IsEnabled;
        aggregator.Persist();
        StartAtLoginCheckBox.IsChecked = startupManager.IsEnabled;
    }

    private void SelectLanguage(string language)
    {
        foreach (var item in LanguageComboBox.Items.OfType<System.Windows.Controls.ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), language, StringComparison.OrdinalIgnoreCase))
            {
                LanguageComboBox.SelectedItem = item;
                return;
            }
        }

        LanguageComboBox.SelectedIndex = 0;
    }

    private void LanguageComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (LanguageComboBox.SelectedItem is not System.Windows.Controls.ComboBoxItem item)
        {
            return;
        }

        aggregator.Snapshot.Language = item.Tag?.ToString() ?? "en-US";
        aggregator.Persist();
    }

    private async void CheckForUpdates_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = await UpdateService.CheckForUpdatesAsync();
            if (!result.IsUpdateAvailable)
            {
                System.Windows.MessageBox.Show(
                    $"Keywise is up to date.\n\nInstalled: {result.CurrentVersion}\nLatest: {result.LatestVersion ?? "unknown"}",
                    "Keywise");
                return;
            }

            var downloadUpdate = System.Windows.MessageBox.Show(
                $"A newer Keywise release is available.\n\nInstalled: {result.CurrentVersion}\nLatest: {result.LatestVersion}\n\nDownload the installer and close Keywise to start the update?",
                "Keywise update available",
                MessageBoxButton.YesNo);
            if (downloadUpdate == MessageBoxResult.Yes)
            {
                var installerPath = await UpdateService.DownloadInstallerAsync(result);
                UpdateService.RunInstallerAndExit(installerPath);
                return;
            }

            UpdateService.OpenReleasesPage(result.ReleaseUrl);
        }
        catch (Exception ex)
        {
            var openReleases = System.Windows.MessageBox.Show(
                $"Keywise could not check GitHub for updates.\n\n{ex.Message}\n\nOpen the releases page instead?",
                "Keywise update check failed",
                MessageBoxButton.YesNo);
            if (openReleases == MessageBoxResult.Yes)
            {
                UpdateService.OpenReleasesPage();
            }
        }
    }

    private void OpenReleases_Click(object sender, RoutedEventArgs e)
    {
        UpdateService.OpenReleasesPage();
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        aggregator.Persist();
        SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
        inputMonitor.Dispose();
        trayIcon.Visible = false;
        trayIcon.Dispose();
    }
}
