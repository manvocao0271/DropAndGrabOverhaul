using BepInEx.Configuration;
using BepInEx.Logging;

namespace DropAndGrabOverhaul.Configuration
{
    // Single multi-select config entry for this mod's own log output, independent of BepInEx's
    // global console/file log-level filter (which applies to every mod at once, not just this
    // one). Read by Logging.cs at the project root, which every other file calls through instead
    // of Plugin.Log directly, so this applies everywhere uniformly.
    //
    // BepInEx.Logging.LogLevel (the same enum ManualLogSource itself uses) is [Flags], so binding
    // it directly - rather than one bool ConfigEntry per level - makes BepInEx's config UI (e.g.
    // ConfigurationManager) render this as one multi-select tag picker, exactly like its own
    // built-in [Logging.Console] LogLevels setting.
    //
    // Initialized first in Plugin.Awake(), before any other Configuration class, so their own
    // startup log lines are already correctly gated by whatever this resolves to.
    public static class LoggingConfiguration
    {
        private static ConfigEntry<LogLevel> logLevelsConfig = null!;

        public static void Initialize(ConfigFile config)
        {
            logLevelsConfig = config.Bind(
                section: "Logging",
                key: "LogLevels",
                defaultValue: LogLevel.Fatal | LogLevel.Error | LogLevel.Warning,
                description: "Which of this mod's own log levels to show. Multiple values can be set at the same time by separating them with , (e.g. Warning, Info)."
            );

            // Deliberately calls Plugin.Log directly rather than through Logging.Xxx: this class is
            // what those gates read, so there's no way to gate the one line that reports what they
            // resolved to. Always printing it means "why do I see no Info logs?" is answerable by
            // reading the log itself, whatever's currently selected.
            Plugin.Log.LogInfo($"Logging levels enabled: {logLevelsConfig.Value}");
        }

        // True if level is currently selected. Read live off the ConfigEntry (not cached) so a
        // change made in-game via ConfigurationManager takes effect immediately, no restart
        // needed. None is never "enabled" for anything - it's the zero value, so the bitwise AND
        // below is always 0 - matching how BepInEx's own picker treats it as inert rather than a
        // real level to toggle.
        public static bool IsEnabled(LogLevel level) => (logLevelsConfig.Value & level) != 0;
    }
}
