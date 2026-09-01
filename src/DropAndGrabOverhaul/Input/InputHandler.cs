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
        // branch (chute auto-store, desk auto-sell) stops consuming it - see the comment there.
        // Blocks IsForceDropHeld/IsDoubleTapDrop from acting on that same still-held press until it
        // is genuinely released, so a hold that continues a moment too long past leaving that
        // branch can't accumulate into an unintended drop-all.
        private static bool suppressUntilKeyReleased = false;

        public static bool IsDoubleTapDrop()
        {
            if (Keyboard.current == null)
                return false;

            if (suppressUntilKeyReleased)
            {
                if (Keyboard.current[Key.G].isPressed)
                    return false;
                suppressUntilKeyReleased = false;
            }

            float doubleTapWindow = InputConfiguration.DoubleTapWindow;

            // Detect the drop key (G by default in Lethal Company)
            Key dropKey = Key.G;
            
            if (Keyboard.current[dropKey].wasPressedThisFrame)
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
            if (Keyboard.current == null) return false;

            Key dropKey = Key.G;

            if (suppressUntilKeyReleased)
            {
                if (Keyboard.current[dropKey].isPressed)
                    return false;
                suppressUntilKeyReleased = false;
            }

            float forceDropDuration = InputConfiguration.ForceDropHoldDuration;

            // Check if drop key is currently held
            if (Keyboard.current[dropKey].isPressed)
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

        public static bool IsDropKeyPressed()
        {
            if (Keyboard.current == null) return false;
            return Keyboard.current[Key.G].isPressed;
        }

        // IsForceDropHeld/IsDoubleTapDrop are only called once RunUpdate falls through to the
        // generic drop-all handling - while an earlier branch (chute auto-store, desk auto-sell)
        // is consuming the same G press instead, dropKeyHoldStartTime/lastDropKeyPressTime never
        // get updated and go stale. Resetting them here isn't enough on its own though: if the
        // player keeps physically holding G for even a moment after that branch stops applying
        // (e.g. stepping out of the chute's hover range without letting go), a fresh
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
            if (Keyboard.current != null && Keyboard.current[Key.G].isPressed)
                suppressUntilKeyReleased = true;
        }
    }
}