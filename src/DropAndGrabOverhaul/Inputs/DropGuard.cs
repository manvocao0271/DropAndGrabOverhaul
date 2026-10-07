using GameNetcodeStuff;

namespace DropAndGrabOverhaul.Inputs;

// Mirrors the checks vanilla's PlayerControllerB.Discard_performed makes before it lets the drop
// key do anything. The mod polls the Discard action itself (see InputHandler) instead of going
// through that handler, so without these the drop key would also fire while typing in chat, in the
// pause menu or terminal, mid-animation, and so on. Written from the decompiled Discard_performed;
// if a game update changes that method, this is the place to re-sync.
internal static class DropGuard
{
    // Same threshold vanilla uses for "just switched slots".
    private const float SlotSwitchCooldownSeconds = 0.2f;

    // The jetpack's item id, which vanilla special-cases in Discard_performed.
    private const int JetpackItemId = 13;

    /// <summary>
    /// True if the local player is in a state where the drop key may do anything at all (any
    /// gesture: tap, double-tap, force drop, desk auto-sell). False while typing in chat, in a
    /// menu or the terminal, in a special interaction, using an item, dead, or when this isn't the
    /// locally controlled player.
    /// </summary>
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

    /// <summary>
    /// True while the drop key means "store the object being placed" - a controller player in
    /// ship build mode. Vanilla handles that itself, so the mod stays out of the way.
    /// </summary>
    public static bool IsControllerBuildModeStore()
    {
        StartOfRound? round = StartOfRound.Instance;
        ShipBuildModeManager? buildMode = ShipBuildModeManager.Instance;
        return round != null && buildMode != null && round.localPlayerUsingController && buildMode.InBuildMode;
    }

    /// <summary>
    /// True if a single tap may drop the currently held item right now - the held-item half of
    /// vanilla's checks. Not applied to drop-all, which waits these states out itself.
    /// </summary>
    public static bool CanDropHeldItemNow(PlayerControllerB player)
    {
        return player.isHoldingObject
            && player.currentlyHeldObjectServer != null
            && !player.isGrabbingObjectAnimation
            && !player.throwingObject
            && player.timeSinceSwitchingSlots >= SlotSwitchCooldownSeconds;
    }
}
