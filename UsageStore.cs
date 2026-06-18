using System.IO;
using System.Text.Json;

namespace DesktopUsageAnalytics;

public sealed class UsageStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public UsageStore()
    {
        DataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Keywise");
        DataPath = Path.Combine(DataDirectory, "usage.json");
        LegacyDataPaths =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kounter", "usage.json"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopUsageAnalytics", "usage.json")
        ];
    }

    public string DataDirectory { get; }

    public string DataPath { get; }

    private string[] LegacyDataPaths { get; }

    public UsageSnapshot Load()
    {
        var path = File.Exists(DataPath)
            ? DataPath
            : LegacyDataPaths.FirstOrDefault(File.Exists);
        if (path is null)
        {
            return new UsageSnapshot();
        }

        if (!File.Exists(path))
        {
            return new UsageSnapshot();
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<UsageSnapshot>(json, JsonOptions) ?? new UsageSnapshot();
        }
        catch
        {
            return new UsageSnapshot();
        }
    }

    public void Save(UsageSnapshot snapshot)
    {
        Directory.CreateDirectory(DataDirectory);
        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        File.WriteAllText(DataPath, json);
    }

    public void Delete()
    {
        if (File.Exists(DataPath))
        {
            File.Delete(DataPath);
        }
    }
}
