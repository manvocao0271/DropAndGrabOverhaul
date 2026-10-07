using GameNetcodeStuff;

namespace DropAndGrabOverhaul.Inputs;

// Mirrors the checks vanilla's PlayerControllerB.Discard_performed makes before the drop key does
// anything. The mod polls the Discard action itself, so without these the key would also fire
// while typing in chat, in menus, mid-animation, etc. Written from the decompiled
// Discard_performed; re-sync it if a game update changes that method (CLAUDE.md gotcha #11).
internal static class DropGuard
{
    private const float SlotSwitchCooldownSeconds = 0.2f;
    private const int JetpackItemId = 13;

    // The drop key may do anything at all: tap, double-tap, force drop, desk auto-place.
    public static bool CanAcceptDropInput(PlayerControllerB player)
    {
        if (!player.IsOwner || !player.isPlayerControlled || (player.IsServer && !player.isHostPlayerObject))
            return false;

        if (player.isTypingChat || player.inTerminalMenu || player.inSpecialInteractAnimation || player.activatingItem)
            return false;

        if (player.quickMenuManager != null && player.quickMenuManager.isMenuOpen)
            return false;

        // Vanilla's handler (left running by DiscardPerformedPatch) owns the key in this state.
        if (IsControllerBuildModeStore())
            return false;

        GrabbableObject? held = player.currentlyHeldObjectServer;
        bool usingJetpackControls = (player.jetpackControls || player.disablingJetpackControls)
            && held != null && held.itemProperties.itemId == JetpackItemId;

        return !usingJetpackControls;
    }

    // The drop key means "store the object being placed": a controller player in ship build mode.
    public static bool IsControllerBuildModeStore()
    {
        StartOfRound? round = StartOfRound.Instance;
        ShipBuildModeManager? buildMode = ShipBuildModeManager.Instance;
        return round != null && buildMode != null && round.localPlayerUsingController && buildMode.InBuildMode;
    }

    // Vanilla cancels ship build mode only once its grab-animation and slot-switch-cooldown checks
    // pass (the rest are in CanAcceptDropInput). Deliberately not gated on throwingObject or
    // isHoldingObject: vanilla cancels before it looks at those.
    public static bool CanCancelBuildMode(PlayerControllerB player)
        => !player.isGrabbingObjectAnimation && player.timeSinceSwitchingSlots >= SlotSwitchCooldownSeconds;

    // The held-item half of vanilla's checks. Applies to a single tap only; drop-all waits these out itself.
    public static bool CanDropHeldItemNow(PlayerControllerB player)
    {
        return player.isHoldingObject
            && player.currentlyHeldObjectServer != null
            && !player.isGrabbingObjectAnimation
            && !player.throwingObject
            && player.timeSinceSwitchingSlots >= SlotSwitchCooldownSeconds;
    }
}
