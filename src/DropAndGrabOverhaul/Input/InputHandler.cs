using UnityEngine.InputSystem;
using DropAndGrabOverhaul.Configuration;
using System.Runtime.CompilerServices;

namespace DropAndGrabOverhaul.Input
{
    public static class InputHandler
    {
        private static float lastDropKeyPressTime = -999f;
        private static float dropKeyHoldStartTime = -999f;
        private static int dropKeyPressCount = 0;

        // Set by ResetDropKeyTracking while the drop key is still held down when an earlier
        // branch (desk auto-sell) stops consuming it - see the comment there.
        // Blocks IsForceDropHeld/IsDoubleTapDrop from acting on that same still-held press until it
        // is genuinely released, so a hold that continues a moment too long past leaving that
        // branch can't accumulate into an unintended drop-all.
        private static bool suppressUntilKeyReleased = false;

        // Vanilla's own drop key is the "Discard" action in the global InputSystem.actions asset
        // (PlayerControllerB subscribes to it in OnEnable). Polling that same action means the
        // mod follows whatever the player has rebound it to in the vanilla keybinds menu, with
        // no separate mod keybind that could drift out of sync. If the action can't be found
        // (e.g. a future game update renames it) this logs once and every check reports "not
        // pressed", so the mod does nothing and vanilla's drop behaves normally.
        private const string DropActionName = "Discard";
        private static InputAction? dropAction;
        private static bool loggedMissingDropAction = false;

        private static InputAction? GetDropAction()
        {
            if (dropAction != null)
                return dropAction;

            var actions = InputSystem.actions;
            if (actions != null)
                dropAction = actions.FindAction(DropActionName);

            if (dropAction != null)
            {
                Plugin.Log.LogInfo(
                    $"Using vanilla '{DropActionName}' action as the drop key (currently bound to: {dropAction.GetBindingDisplayString()})");
            }
            else if (!loggedMissingDropAction)
            {
                loggedMissingDropAction = true;
                Plugin.Log.LogError(
                    $"Could not find vanilla input action '{DropActionName}' - drop-key features are disabled.");
            }

            return dropAction;
        }

        // True only on the frame the drop key went down.
        public static bool WasDropKeyPressedThisFrame()
        {
            return GetDropAction()?.WasPressedThisFrame() ?? false;
        }

        public static bool IsDropKeyPressed()
        {
            return GetDropAction()?.IsPressed() ?? false;
        }

        public static bool IsDoubleTapDrop()
        {
            if (suppressUntilKeyReleased)
            {
                if (IsDropKeyPressed())
                    return false;
                suppressUntilKeyReleased = false;
            }

            float doubleTapWindow = InputConfiguration.DoubleTapWindow;

            if (WasDropKeyPressedThisFrame())
            {
                float timeSinceLastPress = UnityEngine.Time.time - lastDropKeyPressTime;

                // Check if within double-tap window
                if (timeSinceLastPress < doubleTapWindow)
                {
                    // Second tap within window - this is a double-tap!
                    dropKeyPressCount++;
                    if (dropKeyPressCount >= 2)
                    {
                        // Reset for next double-tap
                        lastDropKeyPressTime = -999f;
                        dropKeyPressCount = 0;
                        return true;
                    }
                }
                else
                {
                    // Outside window, reset counter
                    dropKeyPressCount = 1;
                    lastDropKeyPressTime = UnityEngine.Time.time;
                }
            }

            // Reset counter if window expires
            if (UnityEngine.Time.time - lastDropKeyPressTime > doubleTapWindow)
            {
                dropKeyPressCount = 0;
            }

            return false;
        }

        public static bool IsForceDropHeld()
        {
            if (suppressUntilKeyReleased)
            {
                if (IsDropKeyPressed())
                    return false;
                suppressUntilKeyReleased = false;
            }

            float forceDropDuration = InputConfiguration.ForceDropHoldDuration;

            // Check if drop key is currently held
            if (IsDropKeyPressed())
            {
                // If just pressed, record the time
                if (dropKeyHoldStartTime < 0)
                {
                    dropKeyHoldStartTime = UnityEngine.Time.time;
                }

                // Check if held long enough
                float holdDuration = UnityEngine.Time.time - dropKeyHoldStartTime;
                if (holdDuration >= forceDropDuration)
                {
                    return true;
                }
            }
            else
            {
                // Key released, reset
                dropKeyHoldStartTime = -999f;
            }

            return false;
        }

        // IsForceDropHeld/IsDoubleTapDrop are only called once RunUpdate falls through to the
        // generic drop-all handling - while the desk auto-sell branch is consuming the same
        // drop-key press instead, dropKeyHoldStartTime/lastDropKeyPressTime never
        // get updated and go stale. Resetting them here isn't enough on its own though: if the
        // player keeps physically holding the drop key for even a moment after that branch stops applying
        // (e.g. stepping away from the counter without letting go), a fresh
        // dropKeyHoldStartTime starts counting immediately and can genuinely reach
        // ForceDropHoldDuration (as short as 0.2s) a moment later - a real, unintended force-drop,
        // not a stale-timestamp artifact. Also flags suppressUntilKeyReleased if the key is still
        // down right now, so IsForceDropHeld/IsDoubleTapDrop won't count anything from this same
        // held press at all until it's been genuinely released first.
        public static void ResetDropKeyTracking()
        {
            dropKeyHoldStartTime = -999f;
            lastDropKeyPressTime = -999f;
            dropKeyPressCount = 0;
            if (IsDropKeyPressed())
                suppressUntilKeyReleased = true;
        }
    }
}