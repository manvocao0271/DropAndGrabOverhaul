using BepInEx;
using BepInEx.Logging;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using HarmonyLib;
using DropAndGrabOverhaul.Input;
using DropAndGrabOverhaul.Inventory;
using DropAndGrabOverhaul.Configuration;
using GameNetcodeStuff;

namespace DropAndGrabOverhaul;

[BepInAutoPlugin]
public partial class Plugin : BaseUnityPlugin
{
    internal static ManualLogSource Log { get; private set; } = null!;
    private static Harmony? harmonyInstance;
    private static UpdateRunner? runner;
    private static Coroutine? autoSellCoroutine;
    private static Coroutine? dropAllCoroutine;
    private static bool isPlacingOnCounter;
    private static bool isDroppingAll;

    private void Awake()
    {
        Log = Logger;

        ItemBlacklist.Initialize(Config);
        InputConfiguration.Initialize(Config);
        GrabConfiguration.Initialize(Config);
        SellConfiguration.Initialize(Config);

        harmonyInstance = new Harmony(Id);
        harmonyInstance.PatchAll(typeof(Plugin));

        Log.LogInfo($"Plugin {Name} is loaded!");
    }

    private sealed class UpdateRunner : MonoBehaviour
    {
        private void Update()
        {
            Plugin.RunUpdate();
        }
    }

    private static void RunUpdate()
    {
        PlayerControllerB? player = StartOfRound.Instance?.localPlayerController;
        if (player == null)
            return;

        DepositItemsDesk? desk = UnityEngine.Object.FindObjectOfType<DepositItemsDesk>();
        bool atDesk = desk != null && desk.triggerScript != null && player.hoveringOverTrigger == desk.triggerScript;

        if (SellConfiguration.AutoSellInventory && atDesk && InputHandler.IsDropKeyPressed() && desk != null)
        {
            if (autoSellCoroutine == null)
                autoSellCoroutine = runner!.StartCoroutine(AutoSellInventoryCoroutine(player, desk));
            InputHandler.ResetDropKeyTracking();
            return;
        }

        if (atDesk && SellConfiguration.AutoSellInventory)
        {
            InputHandler.ResetDropKeyTracking();
            return;
        }

        bool isForceDropping = InputHandler.IsForceDropHeld();

        bool isDoubleTap = !isForceDropping && InputHandler.IsDoubleTapDrop();

        if (!isForceDropping && !isDoubleTap)
        {
            if (InputHandler.WasDropKeyPressedThisFrame() && player.currentlyHeldObjectServer != null)
            {
                Plugin.Log.LogInfo("Single tap detected - dropping held item immediately");
                isDroppingAll = true;
                player.DiscardHeldObject();
                isDroppingAll = false;
            }
            return;
        }

        Plugin.Log.LogInfo(isForceDropping ? "Force drop detected - dropping ALL items (ignoring blacklist)" : "Double-tap drop detected - dropping all items");

        if (player.ItemSlots == null || player.ItemSlots.Length == 0)
        {
            Plugin.Log.LogInfo("No item slots available");
            return;
        }

        var itemsToDropList = InventoryAccessor.GetDroppableItems(player);

        if (itemsToDropList.Count == 0)
        {
            Plugin.Log.LogInfo("No items to drop");
            return;
        }

        if (dropAllCoroutine == null)
            dropAllCoroutine = runner!.StartCoroutine(DropAllItemsCoroutine(player, itemsToDropList, isForceDropping));
    }

    private static System.Collections.IEnumerator DropAllItemsCoroutine(PlayerControllerB player, List<GrabbableObject> itemsToDrop, bool isForceDropping)
    {
        try
        {
            int originalSlot = player.currentItemSlot;

            int droppedCount = 0;
            foreach (GrabbableObject item in itemsToDrop)
            {
                if (item != null)
                {
                    string itemName = item.itemProperties.itemName;

                    int slotIndex = System.Array.IndexOf(player.ItemSlots, item);

                    if (!isForceDropping && ItemBlacklist.IsBlacklisted(itemName))
                    {
                        Plugin.Log.LogInfo($"Skipping blacklisted item: {itemName}");
                        continue;
                    }

                    player.SwitchToItemSlot(slotIndex);
                    isDroppingAll = true;
                    player.DiscardHeldObject();
                    isDroppingAll = false;
                    droppedCount++;
                    Plugin.Log.LogInfo($"Dropped item: {itemName}");

                    float waitStart = UnityEngine.Time.time;
                    while (player.currentlyHeldObjectServer != null && UnityEngine.Time.time - waitStart < 2f)
                    {
                        yield return null;
                    }

                    if (player.currentlyHeldObjectServer != null)
                    {
                        Plugin.Log.LogWarning($"Timed out waiting for '{itemName}' to finish dropping over the network - stopping drop-all early to avoid desyncing the rest.");
                        break;
                    }
                }
            }

            if (player.currentItemSlot != originalSlot && player.currentlyHeldObjectServer == null)
            {
                player.SwitchToItemSlot(originalSlot);
            }

            Plugin.Log.LogInfo($"Dropped {droppedCount} items total");

        }
        finally
        {
            dropAllCoroutine = null;
        }
    }

    private static System.Collections.IEnumerator AutoSellInventoryCoroutine(PlayerControllerB player, DepositItemsDesk desk)
    {
        try
        {
            int soldCount = 0;
            foreach ((int slot, GrabbableObject item) in InventoryAccessor.GetItemSlots(player))
            {
                // GetItemSlots snapshots the inventory once up front, but this loop yields
                // between sales - re-confirm the slot still holds this exact item before
                // acting on it, in case something else changed slot contents mid-sequence.
                if (player.ItemSlots[slot] != item || !item.itemProperties.isScrap)
                    continue;

                string itemName = item.itemProperties.itemName;

                if (SellConfiguration.IsSellBlacklisted(itemName))
                {
                    Plugin.Log.LogInfo($"Skipping sell-blacklisted item: {itemName}");
                    continue;
                }

                player.SwitchToItemSlot(slot);
                isPlacingOnCounter = true;
                desk.PlaceItemOnCounter(player);
                isPlacingOnCounter = false;
                soldCount++;
                Plugin.Log.LogInfo($"Sold item: {itemName}");
                yield return new WaitForSeconds(0.2f);
            }
            Plugin.Log.LogInfo($"Sold {soldCount} items total");
        }
        finally
        {
            autoSellCoroutine = null;
        }
    }

    [HarmonyPatch(typeof(StartOfRound), "Awake")]
    [HarmonyPostfix]
    private static void StartOfRoundAwakePostfix()
    {
        if (runner != null)
            return;

        GameObject runnerObject = new GameObject("DropAndGrabOverhaulRunner");
        UnityEngine.Object.DontDestroyOnLoad(runnerObject);
        runner = runnerObject.AddComponent<UpdateRunner>();

        Plugin.Log.LogInfo("UpdateRunner created via StartOfRound.Awake postfix");
    }

    [HarmonyPatch(typeof(PlayerControllerB), "DiscardHeldObject")]
    [HarmonyPrefix]
    private static bool DiscardHeldObjectPrefix(PlayerControllerB __instance)
    {
        if (isPlacingOnCounter || isDroppingAll)
            return true;

        if (InputHandler.WasDropKeyPressedThisFrame())
        {
            Plugin.Log.LogInfo(
                $"DiscardHeldObjectPrefix: suppressing vanilla drop on drop-key press, frame: {Time.frameCount}");
            return false;
        }

        return true;
    }

    [HarmonyPatch(typeof(InteractTrigger), "Interact")]
    [HarmonyPrefix]
    private static void InteractTriggerInteractPrefix(InteractTrigger __instance)
    {
        if (__instance.GetComponentInParent<GrabbableObject>() != null)
        {
            __instance.cooldownTime = GrabConfiguration.GrabDelay;
        }
    }

    [HarmonyPatch(typeof(PlayerControllerB), "GrabObject", MethodType.Enumerator)]
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> GrabObjectTranspiler(
        IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo getDecrease =
            typeof(GrabConfiguration).GetMethod(
                nameof(GrabConfiguration.GetInteractionCooldownDecrease));

        List<CodeInstruction> instr = new List<CodeInstruction>(instructions);
        int patchedCount = 0;

        for (int i = 0; i < instr.Count; i++)
        {
            CodeInstruction instruction = instr[i];
            yield return instruction;

            if (instruction.opcode == OpCodes.Ldc_R4 &&
                (float)instruction.operand == 0.1f &&
                i + 1 < instr.Count &&
                instr[i + 1].opcode == OpCodes.Newobj)
            {
                yield return new CodeInstruction(OpCodes.Call, getDecrease);
                yield return new CodeInstruction(OpCodes.Ldc_R4, 2f);
                yield return new CodeInstruction(OpCodes.Div);
                yield return new CodeInstruction(OpCodes.Sub);
                patchedCount++;
            }

            if (instruction.opcode == OpCodes.Ldc_R4 &&
                (float)instruction.operand == 0.2f &&
                i + 1 < instr.Count &&
                instr[i + 1].opcode == OpCodes.Sub)
            {
                yield return new CodeInstruction(OpCodes.Sub);
                yield return new CodeInstruction(OpCodes.Call, getDecrease);
                patchedCount++;
            }
        }

        Plugin.Log.LogInfo(
            $"GrabObjectTranspiler patched {patchedCount} delay checkpoint(s)");
    }
}