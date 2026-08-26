using UnityEngine;

namespace DropAllScrap.Input
{
    public static class InputHandler
    {
        public static bool IsActivationKeyPressed()
        {
            return UnityEngine.Input.GetKeyDown(Plugin.ActivationKey.Value);
        }
    }
}