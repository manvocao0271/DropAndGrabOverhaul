using BepInEx.Configuration;

namespace DropAndGrabOverhaul.Configuration;

internal static class GrabConfiguration
{
    private static ConfigEntry<float> grabDelay = null!;

    // Read live off the entry, so a change made in-game applies to the next grab.
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

        ModLog.Info($"Grab delay set to {GrabDelay}s");
    }
}
