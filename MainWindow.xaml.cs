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

        trayIcon = new Forms.NotifyIcon
        {
            Icon = Drawing.SystemIcons.Application,
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

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        aggregator.Persist();
        SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
        inputMonitor.Dispose();
        trayIcon.Visible = false;
        trayIcon.Dispose();
    }
}
