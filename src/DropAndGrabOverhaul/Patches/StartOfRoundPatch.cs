using HarmonyLib;

namespace DropAndGrabOverhaul.Patches;

// The UpdateRunner can't be created from Plugin.Awake(): anything created there is destroyed by
// an early scene transition (CLAUDE.md gotcha #2). StartOfRound.Awake runs after that settles.
[HarmonyPatch(typeof(StartOfRound), "Awake")]
internal static class StartOfRoundPatch
{
    [HarmonyPostfix]
    private static void Postfix() => UpdateRunner.EnsureCreated();
}
