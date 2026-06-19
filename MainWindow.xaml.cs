using Microsoft.Win32;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace DesktopUsageAnalytics;

public partial class MainWindow : Window
{
    private static readonly string[] KnownKeyboardInputs =
    [
        "Back", "Tab", "Return", "Escape", "Space", "Capital",
        "LeftShift", "RightShift", "LeftCtrl", "RightCtrl", "LeftAlt", "RightAlt",
        "LWin", "RWin", "Apps", "Insert", "Delete", "Home", "End", "PageUp", "PageDown",
        "Left", "Right", "Up", "Down", "PrintScreen", "Scroll", "Pause",
        "D0", "D1", "D2", "D3", "D4", "D5", "D6", "D7", "D8", "D9",
        "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M",
        "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z",
        "NumPad0", "NumPad1", "NumPad2", "NumPad3", "NumPad4", "NumPad5", "NumPad6", "NumPad7", "NumPad8", "NumPad9",
        "Multiply", "Add", "Subtract", "Decimal", "Divide",
        "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12",
        "Oem1", "OemPlus", "OemComma", "OemMinus", "OemPeriod", "Oem2", "Oem3", "OemOpenBrackets", "Oem5", "Oem6", "Oem7"
    ];

    private static readonly string[] KnownMouseInputs = ["Left", "Right", "Middle"];

    private readonly UsageStore store = new();
    private readonly UsageAggregator aggregator;
    private readonly StartupManager startupManager = new();
    private readonly Stopwatch sessionClock = Stopwatch.StartNew();
    private readonly DispatcherTimer refreshTimer = new();
    private readonly Forms.NotifyIcon trayIcon;
    private readonly WindowsInputMonitor inputMonitor;
    private DateTime lastAutoSaveUtc = DateTime.UtcNow;
    private bool isExiting;

    public MainWindow()
    {
        aggregator = new UsageAggregator(store);
        InitializeComponent();
        ThemeManager.Apply(Resources);
        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
        inputMonitor = new WindowsInputMonitor(bucket =>
        {
            aggregator.Increment(bucket);
        });

        DataPathText.Text = store.DataPath;
        StartAtLoginCheckBox.IsChecked = startupManager.IsEnabled;
        MinimizeToTrayCheckBox.IsChecked = aggregator.Snapshot.MinimizeToTrayOnClose;
        SelectLanguage(aggregator.Snapshot.Language);
        InputSortComboBox.SelectedIndex = 0;

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
            isExiting = true;
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
        var counters = aggregator.GetCountersSnapshot();
        var keys = counters
            .Where(item => item.Key.StartsWith("Key.", StringComparison.Ordinal))
            .OrderByDescending(item => item.Value)
            .ToList();
        var mouseTotal = counters.GetValueOrDefault("Mouse.Left") + counters.GetValueOrDefault("Mouse.Right") + counters.GetValueOrDefault("Mouse.Middle");
        var keyTotal = keys.Sum(item => item.Value);
        var activeSeconds = snapshot.ActiveTrackingSeconds + (long)aggregator.PendingActiveDuration.TotalSeconds;

        StatusText.Text = aggregator.TrackingEnabled ? "Tracking enabled" : "Paused";
        TrackingToggleButton.Content = aggregator.TrackingEnabled ? "Pause tracking" : "Enable tracking";
        ActiveTimeText.Text = FormatDuration(TimeSpan.FromSeconds(activeSeconds));
        SessionText.Text = FormatDuration(sessionClock.Elapsed);
        SessionCountText.Text = snapshot.AppLaunches.ToString("N0");
        KeyTotalText.Text = keyTotal.ToString("N0");
        MouseTotalText.Text = mouseTotal.ToString("N0");
        MaybeAutoSave();
        if (MainTabs?.SelectedIndex == 1)
        {
            RefreshAllInputsList();
        }
    }

    private void MaybeAutoSave()
    {
        if (!aggregator.HasUnsavedChanges || DateTime.UtcNow - lastAutoSaveUtc < TimeSpan.FromSeconds(15))
        {
            return;
        }

        aggregator.Persist();
        lastAutoSaveUtc = DateTime.UtcNow;
    }

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

    private void MinimizeToTrayCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        aggregator.Snapshot.MinimizeToTrayOnClose = MinimizeToTrayCheckBox.IsChecked == true;
        aggregator.Persist();
    }

    private void SelectLanguage(string language)
    {
        foreach (var item in LanguageComboBox.Items.OfType<System.Windows.Controls.ComboBoxItem>())
        {
            if (item.IsEnabled && string.Equals(item.Tag?.ToString(), language, StringComparison.OrdinalIgnoreCase))
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

    private void InputSortComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        RefreshAllInputsList();
    }

    private void MainTabs_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (MainTabs?.SelectedIndex == 1)
        {
            RefreshAllInputsList();
        }
    }

    private void RefreshAllInputsList()
    {
        if (AllInputsList is null || InputSortComboBox is null)
        {
            return;
        }

        var counters = aggregator.GetCountersSnapshot();
        var rows = KnownKeyboardInputs
            .Select(name => new InputUsageRow(name, "Keyboard", counters.GetValueOrDefault($"Key.{name}")))
            .Concat(KnownMouseInputs.Select(name => new InputUsageRow(name, "Mouse", counters.GetValueOrDefault($"Mouse.{name}"))))
            .ToList();

        var sort = (InputSortComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag?.ToString() ?? "desc";
        rows = sort switch
        {
            "asc" => rows.OrderBy(row => row.Count).ThenBy(row => row.Name).ToList(),
            "name" => rows.OrderBy(row => row.Name).ToList(),
            _ => rows.OrderByDescending(row => row.Count).ThenBy(row => row.Name).ToList()
        };

        AllInputsList.ItemsSource = rows;
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
                $"A newer Keywise release is available.\n\nInstalled: {result.CurrentVersion}\nLatest: {result.LatestVersion}\n\nDownload, install, and restart Keywise now?",
                "Keywise update available",
                MessageBoxButton.YesNo);
            if (downloadUpdate == MessageBoxResult.Yes)
            {
                CheckForUpdatesButton.IsEnabled = false;
                UpdateProgressBar.Value = 0;
                UpdateProgressBar.Visibility = Visibility.Visible;
                UpdateStatusText.Text = "Downloading update...";
                var progress = new Progress<double>(value =>
                {
                    UpdateProgressBar.Value = value;
                    UpdateStatusText.Text = $"Downloading update... {value:0}%";
                });
                var installerPath = await UpdateService.DownloadInstallerAsync(result, progress);
                UpdateStatusText.Text = "Installing update. Keywise will close and reopen.";
                UpdateService.RunInstallerAndExit(installerPath);
                return;
            }

            UpdateService.OpenReleasesPage(result.ReleaseUrl);
        }
        catch (Exception ex)
        {
            CheckForUpdatesButton.IsEnabled = true;
            UpdateProgressBar.Visibility = Visibility.Collapsed;
            UpdateStatusText.Text = "";
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
        if (!isExiting && aggregator.Snapshot.MinimizeToTrayOnClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        aggregator.Persist();
        SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
        inputMonitor.Dispose();
        trayIcon.Visible = false;
        trayIcon.Dispose();
    }

    private sealed record InputUsageRow(string Name, string Kind, long Count);
}
