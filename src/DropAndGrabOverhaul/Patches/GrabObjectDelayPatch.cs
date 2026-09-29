using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using DropAndGrabOverhaul.Configuration;
using GameNetcodeStuff;
using HarmonyLib;

namespace DropAndGrabOverhaul.Patches;

// Shortens the two fixed waits inside PlayerControllerB.GrabObject()'s coroutine, which are what
// actually gate grab spam (CLAUDE.md gotcha #3). Vanilla:
//
//   yield return new WaitForSeconds(0.1f);
//   ...
//   yield return new WaitForSeconds(grabObjectAnimationTime - 0.2f);
//
// The transpiler finds those two constants and subtracts a configured amount from each.
//
// Deliberately public: the IL emitted below calls GetCooldownDecrease from inside the game's
// own assembly, so both the class and the method are kept public.
[HarmonyPatch(typeof(PlayerControllerB), "GrabObject", MethodType.Enumerator)]
public static class GrabObjectDelayPatch
{
    private const float FirstWaitSeconds = 0.1f;
    private const float AnimationOffsetSeconds = 0.2f;
    private const int ExpectedPatchCount = 2;

    // How much shorter than vanilla's 0.2s the configured delay is. Read live on every grab, so
    // a GrabDelay change made in-game applies immediately.
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
                // Stack is [animationTime, 0.2f]. Sub collapses it to (animationTime - 0.2f), then
                // we push the decrease and let vanilla's own following Sub subtract it too:
                // new WaitForSeconds(animationTime - 0.2f)  ->  ... (animationTime - 0.2f) - decrease
                patched.Add(new CodeInstruction(OpCodes.Sub));
                patched.Add(new CodeInstruction(OpCodes.Call, getDecrease));
                patchedCount++;
            }
        }

        if (patchedCount == ExpectedPatchCount)
            ModLog.Info($"GrabObjectDelayPatch patched {patchedCount} delay checkpoint(s)");
        else
            ModLog.Warning($"GrabObjectDelayPatch patched {patchedCount} delay checkpoint(s), expected {ExpectedPatchCount} - the game's GrabObject() IL probably changed, so the grab delay may not apply.");

        return patched;
    }
}
