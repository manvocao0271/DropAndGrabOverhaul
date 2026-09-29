using BepInEx.Configuration;

namespace DropAndGrabOverhaul.Configuration
{
    public static class InputConfiguration
    {
        private static ConfigEntry<float> doubleTapWindowConfig = null!;
        private static ConfigEntry<float> forceDropHoldDurationConfig = null!;
        private static ConfigEntry<float> reservedSlotsHoldDurationConfig = null!;

        public static float DoubleTapWindow { get; private set; }
        public static float ForceDropHoldDuration { get; private set;}
        public static float ReservedSlotsHoldDuration { get; private set; }

        public static void Initialize(ConfigFile config)
        {
            doubleTapWindowConfig = config.Bind(
                section: "Input",
                key: "DoubleTapWindow",
                defaultValue: 0.2f,
                description: "Time window in seconds to detect double-tap on drop key."
            );

            forceDropHoldDurationConfig = config.Bind(
                section: "Input",
                key: "ForceDropHoldDuration",
                defaultValue: 0.2f,
                description: "Hold drop key for this many seconds to force drop all items, ignoring blacklisted items."
            );

            reservedSlotsHoldDurationConfig = config.Bind(
                section: "Input",
                key: "ReservedSlotsHoldDuration",
                defaultValue: 0.5f,
                description: "Only applies with ReservedItemSlotCore installed. Once the force drop has started, keep holding the drop key this many extra seconds to also drop the items in reserved item slots."
            );

            DoubleTapWindow = doubleTapWindowConfig.Value;
            ForceDropHoldDuration = forceDropHoldDurationConfig.Value;
            ReservedSlotsHoldDuration = reservedSlotsHoldDurationConfig.Value;
            Plugin.Log.LogInfo($"Double-tap window set to {DoubleTapWindow}s");
            Plugin.Log.LogInfo($"Force drop hold duration set to {ForceDropHoldDuration}s");
            Plugin.Log.LogInfo($"Reserved slots hold duration set to {ReservedSlotsHoldDuration}s");
        }
    }
}