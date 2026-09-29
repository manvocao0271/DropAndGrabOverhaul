using BepInEx.Logging;
using DropAndGrabOverhaul.Configuration;

namespace DropAndGrabOverhaul;

// Every log call in this mod goes through here instead of a ManualLogSource directly, so
// Configuration/LoggingConfiguration's LogLevels selection applies everywhere uniformly. This is
// separate from - and independent of - BepInEx's own global console/file log-level filter, which
// applies to every mod at once and isn't what LoggingConfiguration controls.
internal static class ModLog
{
    private static ManualLogSource? source;

    // Called first thing in Plugin.Awake(), before any config class runs.
    public static void Initialize(ManualLogSource logSource) => source = logSource;

    public static void Fatal(object data) => Write(LogLevel.Fatal, data);
    public static void Error(object data) => Write(LogLevel.Error, data);
    public static void Warning(object data) => Write(LogLevel.Warning, data);
    public static void Message(object data) => Write(LogLevel.Message, data);
    public static void Info(object data) => Write(LogLevel.Info, data);
    public static void Debug(object data) => Write(LogLevel.Debug, data);

    // Bypasses the LogLevels gate. Only for the line that reports what that gate resolved to
    // (see LoggingConfiguration.Initialize).
    public static void Announce(object data) => source?.LogInfo(data);

    private static void Write(LogLevel level, object data)
    {
        if (source == null || !LoggingConfiguration.IsEnabled(level))
            return;

        source.Log(level, data);
    }
}
