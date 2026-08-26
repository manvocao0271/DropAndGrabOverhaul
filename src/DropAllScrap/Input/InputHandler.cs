using UnityEngine.InputSystem;

namespace DropAllScrap.Input
{
    public static class InputHandler
    {
        private static float lastDropKeyPressTime = -999f;
        private static int dropKeyPressCount = 0;
        private const float DOUBLE_TAP_WINDOW = 0.3f; // Window to detect double-tap (in seconds)

        public static bool IsDoubleTapDrop()
        {
            if (Keyboard.current == null)
                return false;

            // Detect the drop key (G by default in Lethal Company)
            Key dropKey = Key.G;
            
            if (Keyboard.current[dropKey].wasPressedThisFrame)
            {
                float timeSinceLastPress = UnityEngine.Time.time - lastDropKeyPressTime;

                // Check if within double-tap window
                if (timeSinceLastPress < DOUBLE_TAP_WINDOW)
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
            if (UnityEngine.Time.time - lastDropKeyPressTime > DOUBLE_TAP_WINDOW)
            {
                dropKeyPressCount = 0;
            }

            return false;
        }
    }
}