using Microsoft.Win32;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using Media = System.Windows.Media;

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

    private static readonly HeatmapKeySpec[][] KeyboardHeatmapLayout =
    [
        [new("Escape", "Esc", 58), new("F1", "F1"), new("F2", "F2"), new("F3", "F3"), new("F4", "F4"), new("F5", "F5"), new("F6", "F6"), new("F7", "F7"), new("F8", "F8"), new("F9", "F9"), new("F10", "F10"), new("F11", "F11"), new("F12", "F12")],
        [new("Oem3", "`"), new("D1", "1"), new("D2", "2"), new("D3", "3"), new("D4", "4"), new("D5", "5"), new("D6", "6"), new("D7", "7"), new("D8", "8"), new("D9", "9"), new("D0", "0"), new("OemMinus", "-"), new("OemPlus", "="), new("Back", "Back", 86)],
        [new("Tab", "Tab", 76), new("Q", "Q"), new("W", "W"), new("E", "E"), new("R", "R"), new("T", "T"), new("Y", "Y"), new("U", "U"), new("I", "I"), new("O", "O"), new("P", "P"), new("OemOpenBrackets", "["), new("Oem6", "]"), new("Oem5", "\\", 70)],
        [new("Capital", "Caps", 90), new("A", "A"), new("S", "S"), new("D", "D"), new("F", "F"), new("G", "G"), new("H", "H"), new("J", "J"), new("K", "K"), new("L", "L"), new("Oem1", ";"), new("Oem7", "'"), new("Return", "Enter", 104)],
        [new("LeftShift", "Shift", 112), new("Z", "Z"), new("X", "X"), new("C", "C"), new("V", "V"), new("B", "B"), new("N", "N"), new("M", "M"), new("OemComma", ","), new("OemPeriod", "."), new("Oem2", "/"), new("RightShift", "Shift", 126)],
        [new("LeftCtrl", "Ctrl", 76), new("LWin", "Win", 66), new("LeftAlt", "Alt", 66), new("Space", "Space", 330), new("RightAlt", "Alt", 66), new("Apps", "Menu", 76), new("RightCtrl", "Ctrl", 76)]
    ];

    private readonly UsageStore store = new();
    private readonly UsageAggregator aggregator;
    private readonly StartupManager startupManager = new();
    private readonly Stopwatch sessionClock = Stopwatch.StartNew();
    private readonly DispatcherTimer refreshTimer = new();
    private readonly Forms.NotifyIcon trayIcon;
    private readonly WindowsInputMonitor inputMonitor;
    private DateTime lastAutoSaveUtc = DateTime.UtcNow;
    private bool isExiting;

    private sealed record HeatmapKeySpec(string CounterName, string Label, double Width = 54);

    private sealed record HeatmapRow(IReadOnlyList<HeatmapKey> Keys);

    private sealed record HeatmapKey(string Label, string CountText, double Width, Media.Brush Background, Media.Brush Foreground);

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
        StartMinimizedCheckBox.IsChecked = aggregator.Snapshot.StartMinimized;
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
        UpdateNavButtons();

        if (ShouldStartMinimized())
        {
            Loaded += (_, _) => Hide();
        }
    }

    private static bool ShouldStartMinimized() =>
        Environment.GetCommandLineArgs().Any(arg =>
            string.Equals(arg, "--minimized", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(arg, "/minimized", StringComparison.OrdinalIgnoreCase));

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
            Dispatcher.Invoke(UpdateNavButtons);
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
            RefreshHeatmap();
        }
        else if (MainTabs?.SelectedIndex == 2)
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
        startupManager.SetEnabled(enabled, aggregator.Snapshot.StartMinimized);
        aggregator.Snapshot.StartAtLogin = startupManager.IsEnabled;
        aggregator.Persist();
        StartAtLoginCheckBox.IsChecked = startupManager.IsEnabled;
    }

    private void StartMinimizedCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        aggregator.Snapshot.StartMinimized = StartMinimizedCheckBox.IsChecked == true;
        if (startupManager.IsEnabled)
        {
            startupManager.SetEnabled(true, aggregator.Snapshot.StartMinimized);
        }

        aggregator.Persist();
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
        if (!ReferenceEquals(sender, MainTabs))
        {
            return;
        }

        UpdateNavButtons();
        if (MainTabs?.SelectedIndex == 1)
        {
            RefreshHeatmap();
        }
        else if (MainTabs?.SelectedIndex == 2)
        {
            RefreshAllInputsList();
        }
    }

    private void DashboardNavButton_Click(object sender, RoutedEventArgs e) => SelectTab(0);

    private void HeatmapNavButton_Click(object sender, RoutedEventArgs e) => SelectTab(1);

    private void InputsNavButton_Click(object sender, RoutedEventArgs e) => SelectTab(2);

    private void PrivacyNavButton_Click(object sender, RoutedEventArgs e) => SelectTab(3);

    private void SettingsNavButton_Click(object sender, RoutedEventArgs e) => SelectTab(4);

    private void SelectTab(int index)
    {
        MainTabs.SelectedIndex = index;
        UpdateNavButtons();
    }

    private void UpdateNavButtons()
    {
        if (DashboardNavButton is null)
        {
            return;
        }

        SetNavButtonState(DashboardNavButton, MainTabs.SelectedIndex == 0);
        SetNavButtonState(HeatmapNavButton, MainTabs.SelectedIndex == 1);
        SetNavButtonState(InputsNavButton, MainTabs.SelectedIndex == 2);
        SetNavButtonState(PrivacyNavButton, MainTabs.SelectedIndex == 3);
        SetNavButtonState(SettingsNavButton, MainTabs.SelectedIndex == 4);
    }

    private void SetNavButtonState(System.Windows.Controls.Button button, bool selected)
    {
        button.Background = GetBrush(selected ? "AccentSoft" : "ButtonBg");
        button.BorderBrush = GetBrush(selected ? "Accent" : "ButtonBorder");
        button.Foreground = GetBrush(selected ? "AccentInk" : "ButtonFg");
    }

    private Media.Brush GetBrush(string resourceName) =>
        Resources[resourceName] as Media.Brush ?? Media.Brushes.Transparent;

    private void RefreshHeatmap()
    {
        if (KeyboardHeatmapRows is null)
        {
            return;
        }

        var counters = aggregator.GetCountersSnapshot();
        var highestCount = KeyboardHeatmapLayout
            .SelectMany(row => row)
            .Select(spec => counters.GetValueOrDefault($"Key.{spec.CounterName}"))
            .DefaultIfEmpty(0)
            .Max();

        var rows = KeyboardHeatmapLayout
            .Select(row => new HeatmapRow(row
                .Select(spec =>
                {
                    var count = counters.GetValueOrDefault($"Key.{spec.CounterName}");
                    var ratio = highestCount <= 0 ? 0 : (double)count / highestCount;
                    return new HeatmapKey(
                        spec.Label,
                        count > 0 ? count.ToString("N0") : string.Empty,
                        spec.Width,
                        BuildHeatBrush(ratio),
                        GetBrush(ratio > 0.62 ? "AccentInk" : "Ink"));
                })
                .ToList()))
            .ToList();

        KeyboardHeatmapRows.ItemsSource = rows;
        HeatmapSummaryText.Text = highestCount > 0
            ? $"Darker keys are used more. Highest key count: {highestCount:N0}."
            : "No keyboard data yet. The heatmap fills in as aggregate key counts are collected.";
    }

    private Media.Brush BuildHeatBrush(double ratio)
    {
        if (ratio <= 0)
        {
            return GetBrush("PanelAlt");
        }

        var low = GetResourceColor("AccentSoft", Media.Color.FromRgb(28, 77, 74));
        var high = GetResourceColor("Accent", Media.Color.FromRgb(61, 210, 198));
        var mixed = LerpColor(low, high, Math.Pow(Math.Clamp(ratio, 0, 1), 0.72));
        var brush = new Media.SolidColorBrush(mixed);
        brush.Freeze();
        return brush;
    }

    private Media.Color GetResourceColor(string resourceName, Media.Color fallback) =>
        Resources[resourceName] is Media.SolidColorBrush brush ? brush.Color : fallback;

    private static Media.Color LerpColor(Media.Color low, Media.Color high, double amount) =>
        Media.Color.FromRgb(
            (byte)(low.R + (high.R - low.R) * amount),
            (byte)(low.G + (high.G - low.G) * amount),
            (byte)(low.B + (high.B - low.B) * amount));

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
