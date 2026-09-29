using DropAndGrabOverhaul.Configuration;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DropAndGrabOverhaul.Inputs;

internal static class InputHandler
{
    private static readonly DropGestureTracker tracker = new();

    // Vanilla's own drop key is the "Discard" action in the global InputSystem.actions asset
    // (PlayerControllerB subscribes to it in OnEnable). Polling that same action means the
    // mod follows whatever the player has rebound it to in the vanilla keybinds menu, with
    // no separate mod keybind that could drift out of sync. If the action can't be found
    // (e.g. a future game update renames it) this logs once and every check reports "not
    // pressed", so the mod does nothing and vanilla's drop behaves normally.
    private const string DropActionName = "Discard";
    private static InputAction? dropAction;
    private static bool loggedMissingDropAction;

    private static InputAction? GetDropAction()
    {
        if (dropAction != null)
            return dropAction;

        var actions = InputSystem.actions;
        if (actions != null)
            dropAction = actions.FindAction(DropActionName);

        if (dropAction != null)
        {
            ModLog.Info(
                $"Using vanilla '{DropActionName}' action as the drop key (currently bound to: {dropAction.GetBindingDisplayString()})");
        }
        else if (!loggedMissingDropAction)
        {
            loggedMissingDropAction = true;
            ModLog.Error(
                $"Could not find vanilla input action '{DropActionName}' - drop-key features are disabled.");
        }

        return dropAction;
    }

    // True only on the frame the drop key went down.
    public static bool WasDropKeyPressedThisFrame() => GetDropAction()?.WasPressedThisFrame() ?? false;

    public static bool IsDropKeyPressed() => GetDropAction()?.IsPressed() ?? false;

    // The one per-frame entry point for gesture detection; call it at most once per frame.
    public static DropGesture Poll()
    {
        var timings = new DropTimings(
            InputConfiguration.DoubleTapWindow,
            InputConfiguration.ForceDropHoldDuration,
            InputConfiguration.ReservedSlotsHoldDuration);

        return tracker.Update(Time.time, WasDropKeyPressedThisFrame(), IsDropKeyPressed(), in timings);
    }

    // Call while something else (desk auto-sell) is consuming the drop key instead of Poll: Poll
    // isn't running then, so its tap/hold timestamps would go stale. If the key is still down
    // right now it also blocks gestures until it has been genuinely released - otherwise a hold
    // that continues a moment past leaving that branch (e.g. stepping away from the counter
    // without letting go) would start a fresh hold and could reach a real, unintended force drop.
    public static void ResetDropKeyTracking() => tracker.Reset(IsDropKeyPressed());
}
