using DropAndGrabOverhaul.Inputs;
using GameNetcodeStuff;
using HarmonyLib;

namespace DropAndGrabOverhaul.Patches;

// Replaces vanilla's drop-key handler outright so the mod fully owns drop behaviour (see
// UpdateRunner). PlayerControllerB.Discard_performed is the only subscriber to the "Discard" input
// action and the only thing that turns that key into a drop, so skipping it is enough - no need
// to intercept DiscardHeldObject itself, which other code (the mod's own drops, other mods) also
// calls.
//
// What vanilla did in there besides dropping, and where it lives now:
//   - guard checks (typing in chat, menus, animations...)   -> Inputs/DropGuard
//   - ShipBuildModeManager.CancelBuildMode() on each press   -> UpdateRunner.Update
//   - put a held item that is over the counter on the counter -> UpdateRunner.DropHeldItem
//   - controller + ship build mode: store the object          -> left to vanilla (below)
//
// Vanilla's handler runs untouched whenever the mod can't take over: the runner isn't alive, or
// the Discard action couldn't be resolved (so vanilla's drop key never goes dead).
[HarmonyPatch(typeof(PlayerControllerB), "Discard_performed")]
internal static class DiscardPerformedPatch
{
    [HarmonyPrefix]
    private static bool Prefix()
    {
        if (!UpdateRunner.IsActive || !InputHandler.IsDropActionAvailable)
            return true;

        // Same condition the mod's own input is disabled under (DropGuard), so exactly one of
        // the two handles each press.
        if (DropGuard.IsControllerBuildModeStore())
            return true;

        return false;
    }
}
