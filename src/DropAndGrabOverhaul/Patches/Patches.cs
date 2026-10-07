using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using DropAndGrabOverhaul.Configuration;
using DropAndGrabOverhaul.Inputs;
using GameNetcodeStuff;
using HarmonyLib;

namespace DropAndGrabOverhaul.Patches;

// One class per patch, each with its own class-level [HarmonyPatch], or PatchAll silently skips it
// (CLAUDE.md gotcha #1).

// UpdateRunner can't be created from Plugin.Awake(): an early scene transition destroys it
// (gotcha #2). StartOfRound.Awake runs after that settles.
[HarmonyPatch(typeof(StartOfRound), "Awake")]
internal static class StartOfRoundPatch
{
    [HarmonyPostfix]
    private static void Postfix() => UpdateRunner.EnsureCreated();
}

// Replaces vanilla's drop-key handler so UpdateRunner owns drop behaviour; what vanilla did in
// there besides dropping is covered in CLAUDE.md gotcha #4. Vanilla runs untouched when the
// mod can't take over: no runner, no Discard action, or controller + ship build mode.
[HarmonyPatch(typeof(PlayerControllerB), "Discard_performed")]
internal static class DiscardPerformedPatch
{
    [HarmonyPrefix]
    private static bool Prefix()
    {
        if (!UpdateRunner.IsActive || !InputHandler.IsDropActionAvailable)
            return true;

        // Same condition DropGuard disables the mod's own input under, so exactly one handles each press.
        if (DropGuard.IsControllerBuildModeStore())
            return true;

        return false;
    }
}

// Overrides the interact cooldown on grabbable items with the configured GrabDelay.
[HarmonyPatch(typeof(InteractTrigger), "Interact")]
internal static class GrabCooldownPatch
{
    [HarmonyPrefix]
    private static void Prefix(InteractTrigger __instance)
    {
        if (__instance.GetComponentInParent<GrabbableObject>() != null)
            __instance.cooldownTime = GrabConfiguration.GrabDelay;
    }
}

// Shortens the two fixed waits in PlayerControllerB.GrabObject()'s coroutine, which are what gate
// grab spam (gotcha #3). Vanilla: WaitForSeconds(0.1f) and WaitForSeconds(grabObjectAnimationTime - 0.2f).
// Must stay public: the emitted IL calls GetCooldownDecrease from the game's own assembly.
[HarmonyPatch(typeof(PlayerControllerB), "GrabObject", MethodType.Enumerator)]
public static class GrabObjectDelayPatch
{
    private const float FirstWaitSeconds = 0.1f;
    private const float AnimationOffsetSeconds = 0.2f;
    private const int ExpectedPatchCount = 2;

    // How much shorter than vanilla's 0.2s the configured delay is; read live on every grab.
    public static float GetCooldownDecrease() => AnimationOffsetSeconds - GrabConfiguration.GrabDelay;

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo getDecrease = AccessTools.Method(typeof(GrabObjectDelayPatch), nameof(GetCooldownDecrease));

        var original = new List<CodeInstruction>(instructions);
        var patched = new List<CodeInstruction>(original.Count + 8);
        int patchedCount = 0;

        for (int i = 0; i < original.Count; i++)
        {
            CodeInstruction instruction = original[i];
            patched.Add(instruction);

            if (instruction.opcode != OpCodes.Ldc_R4 || instruction.operand is not float value || i + 1 >= original.Count)
                continue;

            OpCode next = original[i + 1].opcode;

            if (value == FirstWaitSeconds && next == OpCodes.Newobj)
            {
                // new WaitForSeconds(0.1f)  ->  new WaitForSeconds(0.1f - decrease / 2)
                patched.Add(new CodeInstruction(OpCodes.Call, getDecrease));
                patched.Add(new CodeInstruction(OpCodes.Ldc_R4, 2f));
                patched.Add(new CodeInstruction(OpCodes.Div));
                patched.Add(new CodeInstruction(OpCodes.Sub));
                patchedCount++;
            }
            else if (value == AnimationOffsetSeconds && next == OpCodes.Sub)
            {
                // Stack is [animationTime, 0.2f]: Sub collapses it to (animationTime - 0.2f), then
                // vanilla's own following Sub subtracts the decrease we push.
                patched.Add(new CodeInstruction(OpCodes.Sub));
                patched.Add(new CodeInstruction(OpCodes.Call, getDecrease));
                patchedCount++;
            }
        }

        if (patchedCount != ExpectedPatchCount)
            ModLog.Warning($"GrabObjectDelayPatch patched {patchedCount} delay checkpoint(s), expected {ExpectedPatchCount} - the game's GrabObject() IL probably changed, so the grab delay may not apply.");

        return patched;
    }
}
