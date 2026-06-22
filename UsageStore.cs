using System.IO;
using System.Text.Json;

namespace DesktopUsageAnalytics;

public sealed class UsageStore
{
    private const int MaxRotatingBackups = 20;

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
        BackupDataPath = Path.Combine(DataDirectory, "usage.previous.json");
        RotatingBackupDirectory = Path.Combine(DataDirectory, "backups");
        DailyBackupDirectory = Path.Combine(DataDirectory, "daily-backups");
        LegacyDataPaths =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kounter", "usage.json"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopUsageAnalytics", "usage.json")
        ];
    }

    public string DataDirectory { get; }

    public string DataPath { get; }

    public string RotatingBackupDirectory { get; }

    public string DailyBackupDirectory { get; }

    public string? LastLoadMessage { get; private set; }

    public DateTime? LastSaveUtc { get; private set; }

    private string BackupDataPath { get; }

    private string[] LegacyDataPaths { get; }

    private List<string> LoadedLegacyPaths { get; } = [];

    public UsageSnapshot Load()
    {
        var snapshots = new List<UsageSnapshot>();
        LoadedLegacyPaths.Clear();
        LastLoadMessage = null;
        var primarySnapshot = TryLoad(DataPath, out var primaryLoadFailed, quarantineOnError: true);
        if (primarySnapshot is not null)
        {
            snapshots.Add(primarySnapshot);
        }
        else if (primaryLoadFailed)
        {
            var backupSnapshot = TryLoad(BackupDataPath, out _, quarantineOnError: false);
            if (backupSnapshot is not null)
            {
                snapshots.Add(backupSnapshot);
                LastLoadMessage = "Recovered from usage.previous.json because usage.json could not be read.";
            }
        }

        foreach (var path in LegacyDataPaths)
        {
            var snapshot = TryLoad(path, out _, quarantineOnError: false);
            if (snapshot is not null)
            {
                snapshots.Add(snapshot);
                LoadedLegacyPaths.Add(path);
            }
        }

        if (snapshots.Count == 0)
        {
            return new UsageSnapshot();
        }

        return MergeSnapshots(snapshots);
    }

    public void Save(UsageSnapshot snapshot)
    {
        Directory.CreateDirectory(DataDirectory);
        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        var tempPath = Path.Combine(DataDirectory, $"usage-{Guid.NewGuid():N}.tmp");
        File.WriteAllText(tempPath, json);
        ReplaceDataFile(tempPath);
        LastSaveUtc = DateTime.UtcNow;
        ArchiveLoadedLegacyFiles();
        PruneRotatingBackups();
    }

    public UsageSnapshot LoadFromFile(string path)
    {
        var snapshot = TryLoad(path, out var loadFailed, quarantineOnError: false);
        if (snapshot is null)
        {
            throw new InvalidDataException(loadFailed
                ? "That file is not a readable Keywise usage backup."
                : "That file does not exist.");
        }

        return snapshot;
    }

    public void ExportTo(string destinationPath)
    {
        if (!File.Exists(DataPath))
        {
            throw new FileNotFoundException("Keywise has not created a usage data file yet.", DataPath);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        File.Copy(DataPath, destinationPath, overwrite: true);
    }

    public UsageStoreHealth GetHealth()
    {
        var backupFiles = Directory.Exists(RotatingBackupDirectory)
            ? Directory.GetFiles(RotatingBackupDirectory, "*.json")
            : [];
        var dailyFiles = Directory.Exists(DailyBackupDirectory)
            ? Directory.GetFiles(DailyBackupDirectory, "*.json")
            : [];
        var errorFiles = Directory.Exists(Path.Combine(DataDirectory, "load-errors"))
            ? Directory.GetFiles(Path.Combine(DataDirectory, "load-errors"), "*.json")
            : [];

        var allBackupFiles = backupFiles.Concat(dailyFiles).ToList();
        var newestBackupUtc = allBackupFiles.Count == 0
            ? (DateTime?)null
            : allBackupFiles.Select(path => File.GetLastWriteTimeUtc(path)).Max();

        return new UsageStoreHealth(
            DataPath,
            File.Exists(DataPath) ? new FileInfo(DataPath).Length : 0,
            LastSaveUtc,
            newestBackupUtc,
            backupFiles.Length,
            dailyFiles.Length,
            errorFiles.Length,
            LastLoadMessage);
    }

    public void Delete()
    {
        if (File.Exists(DataPath))
        {
            File.Delete(DataPath);
        }
    }

    private void ArchiveLoadedLegacyFiles()
    {
        if (LoadedLegacyPaths.Count == 0)
        {
            return;
        }

        var backupDirectory = Path.Combine(DataDirectory, "migration-backups");
        Directory.CreateDirectory(backupDirectory);
        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");

        foreach (var path in LoadedLegacyPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToList())
        {
            if (!File.Exists(path))
            {
                continue;
            }

            var sourceDirectoryName = new DirectoryInfo(Path.GetDirectoryName(path)!).Name;
            var destination = Path.Combine(backupDirectory, $"{sourceDirectoryName}-usage-{timestamp}.json");
            var counter = 1;
            while (File.Exists(destination))
            {
                destination = Path.Combine(backupDirectory, $"{sourceDirectoryName}-usage-{timestamp}-{counter}.json");
                counter += 1;
            }

            try
            {
                File.Move(path, destination);
            }
            catch
            {
                File.Copy(path, destination, overwrite: false);
                try
                {
                    File.Delete(path);
                }
                catch
                {
                    // Keep the backup even if Windows refuses cleanup of the legacy file.
                }
            }
        }

        LoadedLegacyPaths.Clear();
    }

    private UsageSnapshot? TryLoad(string path, out bool loadFailed, bool quarantineOnError)
    {
        loadFailed = false;
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<UsageSnapshot>(json, JsonOptions);
        }
        catch
        {
            loadFailed = true;
            if (quarantineOnError)
            {
                QuarantineUnreadableFile(path);
            }

            return null;
        }
    }

    private void ReplaceDataFile(string tempPath)
    {
        if (!File.Exists(DataPath))
        {
            File.Move(tempPath, DataPath);
            return;
        }

        try
        {
            CreateRotatingBackup();
            CreateDailyBackup();
            if (File.Exists(BackupDataPath))
            {
                File.Delete(BackupDataPath);
            }

            File.Replace(tempPath, DataPath, BackupDataPath, ignoreMetadataErrors: true);
        }
        catch
        {
            var fallbackBackupPath = CreateUniqueBackupPath("save-backups", "usage-before-save", ".json");
            File.Copy(DataPath, fallbackBackupPath, overwrite: false);
            File.Move(tempPath, DataPath, overwrite: true);
        }
    }

    private void CreateRotatingBackup()
    {
        if (!File.Exists(DataPath))
        {
            return;
        }

        var destination = CreateUniqueBackupPath("backups", "usage", ".json");
        File.Copy(DataPath, destination, overwrite: false);
    }

    private void CreateDailyBackup()
    {
        if (!File.Exists(DataPath))
        {
            return;
        }

        Directory.CreateDirectory(DailyBackupDirectory);
        var destination = Path.Combine(DailyBackupDirectory, $"usage-{DateTime.Now:yyyy-MM-dd}.json");
        if (!File.Exists(destination))
        {
            File.Copy(DataPath, destination, overwrite: false);
        }
    }

    private void PruneRotatingBackups()
    {
        if (!Directory.Exists(RotatingBackupDirectory))
        {
            return;
        }

        var backups = Directory.GetFiles(RotatingBackupDirectory, "*.json")
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ToList();

        foreach (var backup in backups.Skip(MaxRotatingBackups))
        {
            try
            {
                backup.Delete();
            }
            catch
            {
                // Backup pruning is best effort; never risk the active usage file for cleanup.
            }
        }
    }

    private void QuarantineUnreadableFile(string path)
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            var destination = CreateUniqueBackupPath("load-errors", $"{Path.GetFileNameWithoutExtension(path)}-unreadable", Path.GetExtension(path));
            File.Move(path, destination);
        }
        catch
        {
            // If Windows refuses the move, leave the original file untouched for manual recovery.
        }
    }

    private string CreateUniqueBackupPath(string folderName, string baseName, string extension)
    {
        var backupDirectory = Path.Combine(DataDirectory, folderName);
        Directory.CreateDirectory(backupDirectory);
        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var destination = Path.Combine(backupDirectory, $"{baseName}-{timestamp}{extension}");
        var counter = 1;
        while (File.Exists(destination))
        {
            destination = Path.Combine(backupDirectory, $"{baseName}-{timestamp}-{counter}{extension}");
            counter += 1;
        }

        return destination;
    }

    private static UsageSnapshot MergeSnapshots(List<UsageSnapshot> snapshots)
    {
        var primary = snapshots
            .OrderByDescending(GetUsageScore)
            .ThenByDescending(snapshot => snapshot.ActiveTrackingSeconds)
            .First();
        var current = snapshots.First();

        var merged = new UsageSnapshot
        {
            SchemaVersion = Math.Max(primary.SchemaVersion, current.SchemaVersion),
            TrackingEnabled = current.TrackingEnabled,
            StartAtLogin = current.StartAtLogin,
            StartMinimized = current.StartMinimized,
            MinimizeToTrayOnClose = current.MinimizeToTrayOnClose,
            Language = string.IsNullOrWhiteSpace(current.Language) ? primary.Language : current.Language,
            ActiveTrackingSeconds = primary.ActiveTrackingSeconds,
            AppLaunches = Math.Max(primary.AppLaunches, current.AppLaunches),
            PauseCount = Math.Max(primary.PauseCount, current.PauseCount),
            Counters = new Dictionary<string, long>(primary.Counters),
            Daily = CloneDaily(primary.Daily)
        };

        if (!ReferenceEquals(primary, current) && GetUsageScore(primary) > GetUsageScore(current))
        {
            AddCounts(merged, current);
        }

        return merged;
    }

    private static long GetUsageScore(UsageSnapshot snapshot) =>
        snapshot.ActiveTrackingSeconds + snapshot.Counters.Values.Sum() + snapshot.Daily.Values.Sum(day => day.KeyPresses + day.MouseLeft + day.MouseRight + day.MouseMiddle);

    private static Dictionary<string, DailyUsageSnapshot> CloneDaily(Dictionary<string, DailyUsageSnapshot> source) =>
        source.ToDictionary(
            item => item.Key,
            item => new DailyUsageSnapshot
            {
                ActiveTrackingSeconds = item.Value.ActiveTrackingSeconds,
                KeyPresses = item.Value.KeyPresses,
                MouseLeft = item.Value.MouseLeft,
                MouseRight = item.Value.MouseRight,
                MouseMiddle = item.Value.MouseMiddle
            });

    private static void AddCounts(UsageSnapshot merged, UsageSnapshot additional)
    {
        merged.ActiveTrackingSeconds += additional.ActiveTrackingSeconds;
        foreach (var item in additional.Counters)
        {
            merged.Counters[item.Key] = merged.Counters.GetValueOrDefault(item.Key) + item.Value;
        }

        foreach (var item in additional.Daily)
        {
            if (!merged.Daily.TryGetValue(item.Key, out var day))
            {
                day = new DailyUsageSnapshot();
                merged.Daily[item.Key] = day;
            }

            day.ActiveTrackingSeconds += item.Value.ActiveTrackingSeconds;
            day.KeyPresses += item.Value.KeyPresses;
            day.MouseLeft += item.Value.MouseLeft;
            day.MouseRight += item.Value.MouseRight;
            day.MouseMiddle += item.Value.MouseMiddle;
        }
    }
}

public sealed record UsageStoreHealth(
    string DataPath,
    long DataFileBytes,
    DateTime? LastSaveUtc,
    DateTime? NewestBackupUtc,
    int RotatingBackupCount,
    int DailyBackupCount,
    int LoadErrorCount,
    string? LastLoadMessage);
