using BepInEx.Configuration;

namespace DropAndGrabOverhaul.Configuration
{
    public static class GrabConfiguration
    {
        private static ConfigEntry<bool> removeGrabCooldownConfig = null!;

        public static bool RemoveGrabCooldown { get; private set; }

        public static void Initialize(ConfigFile config)
        {
            removeGrabCooldownConfig = config.Bind(
                section: "Grab",
                key: "RemoveGrabCooldown",
                defaultValue: false,
                description: "If true, removes the cooldown delay between grabbing items from the ground."
            );

            RemoveGrabCooldown = removeGrabCooldownConfig.Value;
            Plugin.Log.LogInfo($"Remove grab cooldown: {RemoveGrabCooldown}");
        }
    }
}