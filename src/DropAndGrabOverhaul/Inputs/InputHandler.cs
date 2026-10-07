using DropAndGrabOverhaul.Configuration;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DropAndGrabOverhaul.Inputs;

internal static class InputHandler
{
    private static readonly DropGestureTracker tracker = new();

    // The drop key is vanilla's "Discard" action, so it follows the player's rebinds (CLAUDE.md
    // gotcha #9). If it can't be found this logs once and every check reports "not pressed".
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

    // DiscardPerformedPatch checks this so vanilla's drop key keeps working if the action is missing.
    public static bool IsDropActionAvailable => GetDropAction() != null;

    public static bool WasDropKeyPressedThisFrame() => GetDropAction()?.WasPressedThisFrame() ?? false;

    public static bool IsDropKeyPressed() => GetDropAction()?.IsPressed() ?? false;

    // Call at most once per frame.
    public static DropGesture Poll()
    {
        var timings = new DropTimings(
            InputConfiguration.DoubleTapWindow,
            InputConfiguration.ForceDropHoldDuration,
            InputConfiguration.ReservedSlotsHoldDuration);

        return tracker.Update(Time.time, WasDropKeyPressedThisFrame(), IsDropKeyPressed(), in timings);
    }

    // Call while something else (desk auto-sell) consumes the key instead of Poll. If the key is
    // still down it also blocks gestures until released, so a hold that carries on past the
    // counter can't turn into an unintended force drop.
    public static void ResetDropKeyTracking() => tracker.Reset(IsDropKeyPressed());
}
