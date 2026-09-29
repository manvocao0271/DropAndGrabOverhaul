using BepInEx.Configuration;

namespace DropAndGrabOverhaul.Configuration;

internal static class InputConfiguration
{
    private static ConfigEntry<float> doubleTapWindow = null!;
    private static ConfigEntry<float> forceDropHoldDuration = null!;
    private static ConfigEntry<float> reservedSlotsHoldDuration = null!;

    // Read live off the entries, so changes made in-game apply immediately.
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

        ModLog.Info($"Double-tap window set to {DoubleTapWindow}s");
        ModLog.Info($"Force drop hold duration set to {ForceDropHoldDuration}s");
        ModLog.Info($"Reserved slots hold duration set to {ReservedSlotsHoldDuration}s");
    }
}
