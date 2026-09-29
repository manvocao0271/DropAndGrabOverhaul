using DropAndGrabOverhaul.Configuration;
using HarmonyLib;

namespace DropAndGrabOverhaul.Patches;

// Overrides the interact cooldown on grabbable items with the configured GrabDelay.
[HarmonyPatch(typeof(InteractTrigger), "Interact")]
internal static class GrabCooldownPatch
{
    [HarmonyPrefix]
    private static void Prefix(InteractTrigger __instance)
    {
        if (__instance.GetComponentInParent<GrabbableObject>() != null)
            __instance.cooldownTime = GrabConfiguration.GrabDelay;
    }
}
