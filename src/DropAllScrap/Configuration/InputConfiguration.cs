using BepInEx.Configuration;

namespace DropAllScrap.Configuration
{
    public static class InputConfiguration
    {
        private static ConfigEntry<float> doubleTapWindowConfig = null!;

        public static float DoubleTapWindow { get; private set; }

        public static void Initialize(ConfigFile config)
        {
            doubleTapWindowConfig = config.Bind(
                section: "Input",
                key: "DoubleTapWindow",
                defaultValue: 0.3f,
                description: "Time window in seconds to detect double-tap on drop key (G). Default: 0.3s"
            );

            DoubleTapWindow = doubleTapWindowConfig.Value;
            Plugin.Log.LogInfo($"Double-tap window set to {DoubleTapWindow}s");
        }
    }
}
