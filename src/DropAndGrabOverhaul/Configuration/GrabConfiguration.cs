using BepInEx.Configuration;
using DropAndGrabOverhaul;

namespace DropAndGrabOverhaul.Configuration
{
    public static class GrabConfiguration
    {
        private static ConfigEntry<float> grabDelayConfig = null!;

        public static float GrabDelay { get; private set; }

        public static void Initialize(ConfigFile config)
        {
            grabDelayConfig = config.Bind(
                section: "Grab",
                key: "GrabDelay",
                defaultValue: 0.01f,
                configDescription: new ConfigDescription(
                    "Delay in seconds between grabbing items from the ground. Vanilla default is 0.2 seconds.",
                    new AcceptableValueRange<float>(0.01f, 0.2f)
                )
            );

            GrabDelay = grabDelayConfig.Value;
            Logging.Info($"Grab delay set to {GrabDelay}s");
        }

        // How much shorter than vanilla's 0.2s the configured delay is; subtracted from the
        // GrabObject() coroutine's waits by the transpiler in Plugin.cs.
        public static float GetInteractionCooldownDecrease()
        {
            float clampedDelay = System.Math.Min(System.Math.Max(GrabDelay, 0.01f), 0.2f);
            return 0.2f - clampedDelay;
        }
    }
}