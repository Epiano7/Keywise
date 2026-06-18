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
            "DesktopUsageAnalytics");
        DataPath = Path.Combine(DataDirectory, "usage.json");
    }

    public string DataDirectory { get; }

    public string DataPath { get; }

    public UsageSnapshot Load()
    {
        if (!File.Exists(DataPath))
        {
            return new UsageSnapshot();
        }

        try
        {
            var json = File.ReadAllText(DataPath);
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
