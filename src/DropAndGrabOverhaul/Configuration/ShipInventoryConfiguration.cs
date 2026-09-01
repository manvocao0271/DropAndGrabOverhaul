using BepInEx.Configuration;

namespace DropAndGrabOverhaul.Configuration
{
    public static class ShipInventoryConfiguration
    {
        private static ConfigEntry<float> storeDelayLandedConfig = null!;
        private static ConfigEntry<float> storeDelayOrbitConfig = null!;
        private static ConfigEntry<bool> stopOnJumpConfig = null!;

        public static float StoreDelayLanded { get; private set; }
        public static float StoreDelayOrbit { get; private set; }
        public static bool StopOnJump { get; private set; }

        public static void Initialize(ConfigFile config)
        {
            stopOnJumpConfig = config.Bind(
                section: "ShipInventoryUpdated",
                key: "StopOnJump",
                defaultValue: true,
                description: "Stop storing the rest of the inventory into the chute if the player jumps or falls (e.g. off the elevated ship)."
            );

            storeDelayLandedConfig = config.Bind(
                section: "ShipInventoryUpdated",
                key: "StoreDelayLanded",
                defaultValue: 1f,
                configDescription: new ConfigDescription(
                    "Delay in seconds between each item stored into the ship chute while the ship has landed on a moon (meant to be slow to avoid making the game too easy).",
                    new AcceptableValueRange<float>(0f, 5f)
                )
            );

            storeDelayOrbitConfig = config.Bind(
                section: "ShipInventoryUpdated",
                key: "StoreDelayOrbit",
                defaultValue: 0.2f,
                configDescription: new ConfigDescription(
                    "Delay in seconds between each item stored into the ship chute while the ship is in orbit. Matches the vanilla auto-sell delay by default.",
                    new AcceptableValueRange<float>(0f, 5f)
                )
            );

            StoreDelayLanded = storeDelayLandedConfig.Value;
            StoreDelayOrbit = storeDelayOrbitConfig.Value;
            StopOnJump = stopOnJumpConfig.Value;
            Plugin.Log.LogInfo($"Ship inventory chute store delay: {StoreDelayLanded}s landed, {StoreDelayOrbit}s in orbit; stop on jump: {StopOnJump}");
        }
    }
}
