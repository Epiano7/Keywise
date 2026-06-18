using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace DesktopUsageAnalytics;

public sealed record UpdateCheckResult(bool IsUpdateAvailable, string CurrentVersion, string? LatestVersion, string? ReleaseUrl);

public static class UpdateService
{
    private const string LatestReleaseApiUrl = "https://api.github.com/repos/Epiano7/Keywise/releases/latest";
    private const string ReleasesUrl = "https://github.com/Epiano7/Keywise/releases";

    public static async Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        var currentVersion = GetCurrentVersion();
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Keywise-update-check");
        using var response = await httpClient.GetAsync(LatestReleaseApiUrl, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var latestTag = root.GetProperty("tag_name").GetString();
        var releaseUrl = root.TryGetProperty("html_url", out var urlElement)
            ? urlElement.GetString()
            : ReleasesUrl;

        var latestVersionText = latestTag?.TrimStart('v', 'V');
        var hasUpdate = Version.TryParse(currentVersion, out var currentVersionValue)
            && Version.TryParse(latestVersionText, out var latestVersionValue)
            && latestVersionValue > currentVersionValue;

        return new UpdateCheckResult(hasUpdate, currentVersion, latestTag, releaseUrl);
    }

    public static void OpenReleasesPage(string? releaseUrl = null)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = releaseUrl ?? ReleasesUrl,
            UseShellExecute = true
        });
    }

    private static string GetCurrentVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version is null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }
}
