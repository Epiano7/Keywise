using Microsoft.Win32;
using System.Windows;
using System.Windows.Media;

namespace DesktopUsageAnalytics;

public static class ThemeManager
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static bool UseDarkTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        var value = key?.GetValue("AppsUseLightTheme");
        return value is int intValue && intValue == 0;
    }

    public static void Apply(ResourceDictionary resources)
    {
        if (UseDarkTheme())
        {
            SetDark(resources);
        }
        else
        {
            SetLight(resources);
        }
    }

    private static void SetLight(ResourceDictionary resources)
    {
        Set(resources, "AppBackground", "#EEF3F7");
        Set(resources, "HeaderBackground", "#111827");
        Set(resources, "HeaderBorder", "#253247");
        Set(resources, "Panel", "#FFFFFF");
        Set(resources, "PanelAlt", "#F7FAFC");
        Set(resources, "Line", "#D6E0EA");
        Set(resources, "Ink", "#111827");
        Set(resources, "Muted", "#607084");
        Set(resources, "Accent", "#0F8B8D");
        Set(resources, "AccentStrong", "#0B6F73");
        Set(resources, "AccentSoft", "#E1F4F2");
        Set(resources, "AccentInk", "#12494B");
        Set(resources, "ButtonBg", "#FAFCFE");
        Set(resources, "ButtonFg", "#172033");
        Set(resources, "ButtonBorder", "#C7D2DE");
        Set(resources, "DangerSoft", "#FFF1F2");
        Set(resources, "WarningSoft", "#FFF8E1");
        Set(resources, "WarningBorder", "#E8BE3E");
        Set(resources, "WarningInk", "#4E3A00");
        Set(resources, "StatusBg", "#203149");
        Set(resources, "StatusBorder", "#3A516F");
    }

    private static void SetDark(ResourceDictionary resources)
    {
        Set(resources, "AppBackground", "#0E1117");
        Set(resources, "HeaderBackground", "#090D13");
        Set(resources, "HeaderBorder", "#202A3A");
        Set(resources, "Panel", "#161B23");
        Set(resources, "PanelAlt", "#1D2430");
        Set(resources, "Line", "#2C3645");
        Set(resources, "Ink", "#EAF0F7");
        Set(resources, "Muted", "#9AA8BA");
        Set(resources, "Accent", "#36C2B4");
        Set(resources, "AccentStrong", "#19A99C");
        Set(resources, "AccentSoft", "#123532");
        Set(resources, "AccentInk", "#A8F2EA");
        Set(resources, "ButtonBg", "#202938");
        Set(resources, "ButtonFg", "#EDF3FA");
        Set(resources, "ButtonBorder", "#37465A");
        Set(resources, "DangerSoft", "#34171F");
        Set(resources, "WarningSoft", "#302915");
        Set(resources, "WarningBorder", "#A17C24");
        Set(resources, "WarningInk", "#FFE8A3");
        Set(resources, "StatusBg", "#162033");
        Set(resources, "StatusBorder", "#33435B");
    }

    private static void Set(ResourceDictionary resources, string key, string color)
    {
        resources[key] = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
    }
}
