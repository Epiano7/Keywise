namespace DesktopUsageAnalytics;

public sealed class UsageSnapshot
{
    public int SchemaVersion { get; set; } = 1;

    public bool TrackingEnabled { get; set; }

    public bool StartAtLogin { get; set; }

    public long ActiveTrackingSeconds { get; set; }

    public long AppLaunches { get; set; }

    public long PauseCount { get; set; }

    public Dictionary<string, long> Counters { get; set; } = [];

    public Dictionary<string, DailyUsageSnapshot> Daily { get; set; } = [];
}

public sealed class DailyUsageSnapshot
{
    public long ActiveTrackingSeconds { get; set; }

    public long KeyPresses { get; set; }

    public long MouseLeft { get; set; }

    public long MouseRight { get; set; }

    public long MouseMiddle { get; set; }
}
