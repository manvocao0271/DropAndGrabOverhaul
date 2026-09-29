using System;
using DropAndGrabOverhaul.Inputs;
using GameNetcodeStuff;
using HarmonyLib;
using UnityEngine;

namespace DropAndGrabOverhaul.Patches;

// While one of these scopes is open, DiscardHeldObjectPatch lets vanilla's DiscardHeldObject run.
// This is how the mod's own drops (single tap, drop-all, auto-sell) get past the suppression
// below. A scope is exception-safe (dispose in a using) and nestable.
internal static class VanillaDiscard
{
    private static int allowDepth;

    public static bool IsAllowed => allowDepth > 0;

    public static Scope Allow()
    {
        allowDepth++;
        return default;
    }

    public readonly struct Scope : IDisposable
    {
        public void Dispose()
        {
            if (allowDepth > 0)
                allowDepth--;
        }
    }
}

// Suppresses vanilla's own drop-key-triggered discard so the mod fully owns drop behaviour
// (see UpdateRunner). The check is a heuristic - "the drop key went down this frame" - so any
// other caller of DiscardHeldObject on that same frame is suppressed too. That is fine in
// practice, but it is the first place to look if another mod's drop ever stops working.
[HarmonyPatch(typeof(PlayerControllerB), "DiscardHeldObject")]
internal static class DiscardHeldObjectPatch
{
    [HarmonyPrefix]
    private static bool Prefix()
    {
        if (VanillaDiscard.IsAllowed)
            return true;

        if (InputHandler.WasDropKeyPressedThisFrame())
        {
            ModLog.Info($"DiscardHeldObjectPatch: suppressing vanilla drop on drop-key press, frame: {Time.frameCount}");
            return false;
        }

        return true;
    }
}
