namespace DesktopUsageAnalytics;

public sealed class UsageAggregator
{
    private readonly UsageStore store;
    private readonly UsageSnapshot snapshot;
    private DateTime? activeSinceUtc;

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

    public bool TrackingEnabled => snapshot.TrackingEnabled;

    public TimeSpan CurrentSessionDuration =>
        activeSinceUtc is null ? TimeSpan.Zero : DateTime.UtcNow - activeSinceUtc.Value;

    public void SetTrackingEnabled(bool enabled)
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
        Persist();
    }

    public void Increment(InputBucket bucket)
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

        Persist();
    }

    public void ResetCounts()
    {
        snapshot.Counters.Clear();
        snapshot.Daily.Clear();
        snapshot.ActiveTrackingSeconds = 0;
        activeSinceUtc = snapshot.TrackingEnabled ? DateTime.UtcNow : null;
        Persist();
    }

    public void DeleteLocalData()
    {
        snapshot.Counters.Clear();
        snapshot.Daily.Clear();
        snapshot.ActiveTrackingSeconds = 0;
        snapshot.AppLaunches = 0;
        snapshot.PauseCount = 0;
        snapshot.StartAtLogin = false;
        snapshot.TrackingEnabled = false;
        activeSinceUtc = null;
        store.Delete();
    }

    public void Persist()
    {
        FlushActiveTime();
        store.Save(snapshot);
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
}
