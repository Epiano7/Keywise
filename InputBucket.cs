namespace DesktopUsageAnalytics;

public enum MouseButtonBucket
{
    Left,
    Right,
    Middle
}

public sealed record InputBucket(string Name)
{
    public static InputBucket Key(string keyName) => new($"Key.{keyName}");

    public static InputBucket Mouse(MouseButtonBucket button) => new($"Mouse.{button}");
}
