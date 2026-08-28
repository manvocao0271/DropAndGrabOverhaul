using BepInEx.Configuration;

namespace DropAndGrabOverhaul.Configuration
{
    public static class ShipInventoryConfiguration
    {
        private static ConfigEntry<float> storeDelayLandedConfig = null!;
        private static ConfigEntry<float> storeDelayOrbitConfig = null!;

        public static float StoreDelayLanded { get; private set; }
        public static float StoreDelayOrbit { get; private set; }

        public static void Initialize(ConfigFile config)
        {
            storeDelayLandedConfig = config.Bind(
                section: "ShipInventoryUpdated",
                key: "StoreDelayLanded",
                defaultValue: 1f,
                configDescription: new ConfigDescription(
                    "Delay in seconds between each item stored into the ship chute while the ship has landed on a moon.",
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
            Plugin.Log.LogInfo($"Ship inventory chute store delay: {StoreDelayLanded}s landed, {StoreDelayOrbit}s in orbit");
        }
    }
}
