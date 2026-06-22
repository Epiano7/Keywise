namespace DesktopUsageAnalytics;

public sealed class UsageAggregator
{
    private readonly UsageStore store;
    private readonly object syncRoot = new();
    private readonly UsageSnapshot snapshot;
    private readonly DateTime sessionStartedUtc = DateTime.UtcNow;
    private DateTime? activeSinceUtc;
    private bool hasUnsavedChanges;

    public UsageAggregator(UsageStore store)
    {
        this.store = store;
        snapshot = store.Load();
        snapshot.AppLaunches += 1;
        if (snapshot.TrackingEnabled)
        {
            activeSinceUtc = DateTime.UtcNow;
        }

        Persist();
    }

    public UsageSnapshot Snapshot => snapshot;

    public bool TrackingEnabled
    {
        get
        {
            lock (syncRoot)
            {
                return snapshot.TrackingEnabled;
            }
        }
    }

    public bool HasUnsavedChanges
    {
        get
        {
            lock (syncRoot)
            {
                return hasUnsavedChanges;
            }
        }
    }

    public TimeSpan CurrentSessionDuration => DateTime.UtcNow - sessionStartedUtc;

    public TimeSpan PendingActiveDuration =>
        GetPendingActiveDuration();

    public IReadOnlyDictionary<string, long> GetCountersSnapshot()
    {
        lock (syncRoot)
        {
            return new Dictionary<string, long>(snapshot.Counters);
        }
    }

    public void SetTrackingEnabled(bool enabled)
    {
        lock (syncRoot)
        {
            if (enabled == snapshot.TrackingEnabled)
            {
                return;
            }

            if (enabled)
            {
                activeSinceUtc = DateTime.UtcNow;
            }
            else
            {
                FlushActiveTime();
                snapshot.PauseCount += 1;
            }

            snapshot.TrackingEnabled = enabled;
            PersistCore();
        }
    }

    public void Increment(InputBucket bucket)
    {
        lock (syncRoot)
        {
            if (!snapshot.TrackingEnabled)
            {
                return;
            }

            snapshot.Counters[bucket.Name] = snapshot.Counters.GetValueOrDefault(bucket.Name) + 1;

            var today = GetToday();
            if (bucket.Name.StartsWith("Key.", StringComparison.Ordinal))
            {
                today.KeyPresses += 1;
            }
            else if (bucket.Name == "Mouse.Left")
            {
                today.MouseLeft += 1;
            }
            else if (bucket.Name == "Mouse.Right")
            {
                today.MouseRight += 1;
            }
            else if (bucket.Name == "Mouse.Middle")
            {
                today.MouseMiddle += 1;
            }

            hasUnsavedChanges = true;
        }
    }

    public void ResetCounts()
    {
        lock (syncRoot)
        {
            snapshot.Counters.Clear();
            snapshot.Daily.Clear();
            snapshot.ActiveTrackingSeconds = 0;
            activeSinceUtc = snapshot.TrackingEnabled ? DateTime.UtcNow : null;
            PersistCore();
        }
    }

    public void DeleteLocalData()
    {
        lock (syncRoot)
        {
            snapshot.Counters.Clear();
            snapshot.Daily.Clear();
            snapshot.ActiveTrackingSeconds = 0;
            snapshot.AppLaunches = 0;
            snapshot.PauseCount = 0;
            snapshot.StartAtLogin = false;
            snapshot.TrackingEnabled = false;
            activeSinceUtc = null;
            hasUnsavedChanges = false;
            store.Delete();
        }
    }

    public void RestoreFromFile(string path)
    {
        lock (syncRoot)
        {
            var restored = store.LoadFromFile(path);
            CopySnapshot(restored, snapshot);
            activeSinceUtc = snapshot.TrackingEnabled ? DateTime.UtcNow : null;
            PersistCore();
        }
    }

    public void Persist()
    {
        lock (syncRoot)
        {
            PersistCore();
        }
    }

    private void PersistCore()
    {
        FlushActiveTime();
        store.Save(snapshot);
        hasUnsavedChanges = false;
    }

    private DailyUsageSnapshot GetToday()
    {
        var key = DateTime.Now.ToString("yyyy-MM-dd");
        if (!snapshot.Daily.TryGetValue(key, out var daily))
        {
            daily = new DailyUsageSnapshot();
            snapshot.Daily[key] = daily;
        }

        return daily;
    }

    private void FlushActiveTime()
    {
        if (activeSinceUtc is null)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var elapsed = Math.Max(0, (long)(now - activeSinceUtc.Value).TotalSeconds);
        if (elapsed == 0)
        {
            return;
        }

        snapshot.ActiveTrackingSeconds += elapsed;
        GetToday().ActiveTrackingSeconds += elapsed;
        activeSinceUtc = now;
    }

    private TimeSpan GetPendingActiveDuration()
    {
        lock (syncRoot)
        {
            return activeSinceUtc is null ? TimeSpan.Zero : DateTime.UtcNow - activeSinceUtc.Value;
        }
    }

    private static void CopySnapshot(UsageSnapshot source, UsageSnapshot destination)
    {
        destination.SchemaVersion = source.SchemaVersion;
        destination.TrackingEnabled = source.TrackingEnabled;
        destination.StartAtLogin = source.StartAtLogin;
        destination.StartMinimized = source.StartMinimized;
        destination.MinimizeToTrayOnClose = source.MinimizeToTrayOnClose;
        destination.Language = source.Language;
        destination.ActiveTrackingSeconds = source.ActiveTrackingSeconds;
        destination.AppLaunches = source.AppLaunches;
        destination.PauseCount = source.PauseCount;
        destination.Counters = new Dictionary<string, long>(source.Counters);
        destination.Daily = source.Daily.ToDictionary(
            item => item.Key,
            item => new DailyUsageSnapshot
            {
                ActiveTrackingSeconds = item.Value.ActiveTrackingSeconds,
                KeyPresses = item.Value.KeyPresses,
                MouseLeft = item.Value.MouseLeft,
                MouseRight = item.Value.MouseRight,
                MouseMiddle = item.Value.MouseMiddle
            });
    }
}
