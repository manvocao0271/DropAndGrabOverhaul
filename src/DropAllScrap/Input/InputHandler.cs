using UnityEngine.InputSystem;
using DropAllScrap.Configuration;
using System.Runtime.CompilerServices;

namespace DropAllScrap.Input
{
    public static class InputHandler
    {
        private static float lastDropKeyPressTime = -999f;
        private static float dropKeyHoldStartTime = -999f;
        private static int dropKeyPressCount = 0;

        public static bool IsDoubleTapDrop()
        {
            if (Keyboard.current == null)
                return false;

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
    }
}