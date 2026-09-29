// using BepInEx.Logging;
// using DropAndGrabOverhaul.Configuration;

// namespace DropAndGrabOverhaul;

// // Every log call in this mod goes through here instead of Plugin.Log.LogXxx directly, so
// // Configuration/LoggingConfiguration's LogLevels selection applies everywhere uniformly. This is
// // separate from - and independent of - BepInEx's own global console/file log-level filter, which
// // applies to every mod at once and isn't what LoggingConfiguration controls.
// internal static class Logging
// {
//     public static void Fatal(object data)
//     {
//         if (LoggingConfiguration.IsEnabled(LogLevel.Fatal)) Plugin.Log.LogFatal(data);
//     }

//     public static void Error(object data)
//     {
//         if (LoggingConfiguration.IsEnabled(LogLevel.Error)) Plugin.Log.LogError(data);
//     }

//     public static void Warning(object data)
//     {
//         if (LoggingConfiguration.IsEnabled(LogLevel.Warning)) Plugin.Log.LogWarning(data);
//     }

//     public static void Message(object data)
//     {
//         if (LoggingConfiguration.IsEnabled(LogLevel.Message)) Plugin.Log.LogMessage(data);
//     }

//     public static void Info(object data)
//     {
//         if (LoggingConfiguration.IsEnabled(LogLevel.Info)) Plugin.Log.LogInfo(data);
//     }

//     public static void Debug(object data)
//     {
//         if (LoggingConfiguration.IsEnabled(LogLevel.Debug)) Plugin.Log.LogDebug(data);
//     }
// }
