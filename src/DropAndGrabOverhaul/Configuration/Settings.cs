using BepInEx.Configuration;
using BepInEx.Logging;

namespace DropAndGrabOverhaul.Configuration;

// Values are read live off their ConfigEntry, so changes made in-game apply immediately.

internal static class LoggingConfiguration
{
    private static ConfigEntry<LogLevel>? logLevelsConfig;

    // A [Flags] LogLevel renders as a multi-select in BepInEx's config UI, like its own LogLevels.
    public static void Initialize(ConfigFile config)
    {
        logLevelsConfig = config.Bind(
            section: "Logging",
            key: "LogLevels",
            defaultValue: LogLevel.Fatal | LogLevel.Error | LogLevel.Warning,
            description: "Which of this mod's own log levels to show. Multiple values can be set at the same time by separating them with , (e.g. Warning, Info)."
        );

        ModLog.Announce($"Logging levels enabled: {logLevelsConfig.Value}");
    }

    public static bool IsEnabled(LogLevel level) => logLevelsConfig != null && (logLevelsConfig.Value & level) != 0;
}

internal static class InputConfiguration
{
    private static ConfigEntry<float> doubleTapWindow = null!;
    private static ConfigEntry<float> forceDropHoldDuration = null!;
    private static ConfigEntry<float> reservedSlotsHoldDuration = null!;

    public static float DoubleTapWindow => doubleTapWindow.Value;
    public static float ForceDropHoldDuration => forceDropHoldDuration.Value;
    public static float ReservedSlotsHoldDuration => reservedSlotsHoldDuration.Value;

    public static void Initialize(ConfigFile config)
    {
        doubleTapWindow = config.Bind(
            section: "Input",
            key: "DoubleTapWindow",
            defaultValue: 0.2f,
            description: "Time window in seconds to detect double-tap on drop key."
        );

        forceDropHoldDuration = config.Bind(
            section: "Input",
            key: "ForceDropHoldDuration",
            defaultValue: 0.5f,
            description: "Hold drop key for this many seconds to force drop all items, ignoring blacklisted items."
        );

        reservedSlotsHoldDuration = config.Bind(
            section: "Input",
            key: "ReservedSlotsHoldDuration",
            defaultValue: 0.5f,
            description: "Only applies with ReservedItemSlotCore installed. Once the force drop has started, keep holding the drop key this many extra seconds to also drop the items in reserved item slots."
        );
    }
}

internal static class GrabConfiguration
{
    private static ConfigEntry<float> grabDelay = null!;

    public static float GrabDelay => grabDelay.Value;

    public static void Initialize(ConfigFile config)
    {
        grabDelay = config.Bind(
            section: "Grab",
            key: "GrabDelay",
            defaultValue: 0.01f,
            configDescription: new ConfigDescription(
                "Delay in seconds between grabbing items from the ground. Vanilla default is 0.2 seconds.",
                new AcceptableValueRange<float>(0.01f, 0.2f)
            )
        );
    }
}

// Items that double-tap drop skips (force drop ignores this list).
internal static class ItemBlacklist
{
    private static ItemNameList items = null!;

    public static void Initialize(ConfigFile config)
    {
        items = new ItemNameList(
            config,
            section: "Items",
            key: "BlacklistedItems",
            description: "Comma-separated list of item names that should NOT be dropped (Walkie-talkie, Flashlight, Shovel, etc.)");
    }

    public static bool IsBlacklisted(string itemName) => items.Contains(itemName);
}

internal static class SellConfiguration
{
    private static ConfigEntry<bool> autoSellInventory = null!;
    private static ItemNameList sellBlacklist = null!;

    public static bool AutoSellInventory => autoSellInventory.Value;

    public static void Initialize(ConfigFile config)
    {
        autoSellInventory = config.Bind(
            section: "Sell",
            key: "AutoSellInventory",
            defaultValue: true,
            description: "Pressing the drop key once while at the company counter will automatically put all sellable items in the inventory on the counter."
        );

        sellBlacklist = new ItemNameList(
            config,
            section: "Sell",
            key: "SellBlacklistedItems",
            description: "Comma-separated list of item names that should NOT be automatically sold (Walkie-talkie, Flashlight, Shovel, etc.)");
    }

    public static bool IsSellBlacklisted(string itemName) => sellBlacklist.Contains(itemName);
}
