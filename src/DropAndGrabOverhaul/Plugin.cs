using BepInEx;
using BepInEx.Logging;
using BepInEx.Configuration;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using HarmonyLib;
using DropAndGrabOverhaul.Input;
using DropAndGrabOverhaul.Inventory;
using DropAndGrabOverhaul.Configuration;
using DropAndGrabOverhaul.Compatibility;
using GameNetcodeStuff;
using UnityEngine.InputSystem;

namespace DropAndGrabOverhaul;



[BepInAutoPlugin]
[BepInDependency("com.rune580.LethalCompanyInputUtils", BepInDependency.DependencyFlags.HardDependency)]
public partial class Plugin : BaseUnityPlugin
{
    internal static ManualLogSource Log { get; private set; } = null!;
    private static Harmony? harmonyInstance;
    private static UpdateRunner? runner;
    private static Coroutine? autoSellCoroutine;
    private static Coroutine? dropAllCoroutine;
    private static Coroutine? storeInChuteCoroutine;
    private static bool isPlacingOnCounter;
    private static bool isDroppingAll;

    private const float GrabAnimationWaitTimeout = 3f;

    private const float ChuteActivityGraceWindow = 0.5f;
    private static float lastChuteActivityTime = -999f;

    private void Awake()
    {
        Log = Logger;

        ItemBlacklist.Initialize(Config);
        InputConfiguration.Initialize(Config);
        GrabConfiguration.Initialize(Config);
        SellConfiguration.Initialize(Config);
        ShipInventoryConfiguration.Initialize(Config);

        harmonyInstance = new Harmony("com.github.manvocao0271.dropandgraboverhaul");
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

        if (ShipInventoryCompat.IsLoaded && ShipInventoryCompat.IsHoveringChute(player) && InputHandler.IsDropKeyPressed())
        {
            if (storeInChuteCoroutine == null)
            {
                object chuteHandle = ShipInventoryCompat.AcquireHoveredChute(player)!;
                storeInChuteCoroutine = runner!.StartCoroutine(StoreAllInChuteCoroutine(player, chuteHandle));
            }
            lastChuteActivityTime = Time.time;
            InputHandler.ResetDropKeyTracking();
            return;
        }

        if (Time.time - lastChuteActivityTime < ChuteActivityGraceWindow)
        {
            InputHandler.ResetDropKeyTracking();
            return;
        }

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
            if (Keyboard.current != null && Keyboard.current[Key.G].wasPressedThisFrame && player.currentlyHeldObjectServer != null)
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

        var itemsToDropList = new List<GrabbableObject>();
        for (int i = 0; i < player.ItemSlots.Length; i++)
        {
            if (player.ItemSlots[i] != null)
            {
                itemsToDropList.Add(player.ItemSlots[i]);
            }
        }

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
            for (int i = 0; i < player.ItemSlots.Length; i++)
            {
                if (player.ItemSlots[i] != null && player.ItemSlots[i].itemProperties.isScrap)
                {
                    string itemName = player.ItemSlots[i].itemProperties.itemName;

                    if (SellConfiguration.IsSellBlacklisted(itemName))
                    {
                        Plugin.Log.LogInfo($"Skipping sell-blacklisted item: {itemName}");
                        continue;
                    }

                    player.SwitchToItemSlot(i);
                    isPlacingOnCounter = true;
                    desk.PlaceItemOnCounter(player);
                    isPlacingOnCounter = false;
                    soldCount++;
                    Plugin.Log.LogInfo($"Sold item: {itemName}");
                    yield return new WaitForSeconds(0.2f);
                }
            }
            Plugin.Log.LogInfo($"Sold {soldCount} items total");
        }
        finally
        {
            autoSellCoroutine = null;
        }
    }

    private static System.Collections.IEnumerator StoreAllInChuteCoroutine(PlayerControllerB player, object chuteHandle)
    {
        try
        {
        int originalSlot = player.currentItemSlot;

        var processedItems = new HashSet<GrabbableObject>();
        while (true)
        {
            if (player.isPlayerDead)
            {
                Plugin.Log.LogInfo("Player died - stopping the rest of the chute auto-store sequence");
                break;
            }

            if (IsJumpingOrFalling(player))
            {
                Plugin.Log.LogInfo("Player jumped or is falling - stopping the rest of the chute auto-store sequence");
                break;
            }

            if (!player.isInHangarShipRoom)
            {
                Plugin.Log.LogInfo("Left the hangar ship room - stopping the rest of the chute auto-store sequence");
                break;
            }

            if (!ShipInventoryCompat.IsChuteHandleValid(chuteHandle))
            {
                Plugin.Log.LogWarning("Lost the chute reference mid-sequence - stopping the rest of the chute auto-store sequence");
                break;
            }

            List<(int Slot, GrabbableObject Item)> items = ShipInventoryCompat.GetStorableItems(player);

            GrabbableObject? item = null;
            int slot = -1;
            foreach ((int candidateSlot, GrabbableObject candidateItem) in items)
            {
                if (!processedItems.Contains(candidateItem))
                {
                    item = candidateItem;
                    slot = candidateSlot;
                    break;
                }
            }

            if (item == null)
                break;

            float grabWait = 0f;
            while (player.isGrabbingObjectAnimation && player.currentlyGrabbingObject == item && grabWait < GrabAnimationWaitTimeout)
            {
                yield return null;
                grabWait += Time.deltaTime;
            }

            processedItems.Add(item);
            string itemName = item.itemProperties.itemName;
            float itemProcessingStart = Time.time;

            if (player.currentlyHeldObjectServer != item)
                player.SwitchToItemSlot(slot);

            float cooldownWaitStart = Time.time;
            const float chuteCooldownTimeout = 3f;
            do
            {
                if (player.isPlayerDead)
                {
                    Plugin.Log.LogInfo("Player died - stopping the rest of the chute auto-store sequence");
                    goto stoppedEarly;
                }
                if (IsJumpingOrFalling(player))
                {
                    Plugin.Log.LogInfo("Player jumped or is falling - stopping the rest of the chute auto-store sequence");
                    goto stoppedEarly;
                }
                if (!player.isInHangarShipRoom)
                {
                    Plugin.Log.LogInfo("Left the hangar ship room - stopping the rest of the chute auto-store sequence");
                    goto stoppedEarly;
                }
                yield return null;
            }
            while (ShipInventoryCompat.IsChuteOnCooldown(chuteHandle) && Time.time - cooldownWaitStart < chuteCooldownTimeout);
            float cooldownWaitDuration = Time.time - cooldownWaitStart;

            if (ShipInventoryCompat.IsChuteOnCooldown(chuteHandle))
            {
                Plugin.Log.LogWarning($"Chute stayed on cooldown for {chuteCooldownTimeout:F0}s - skipping '{itemName}' for now rather than waiting longer.");
                continue;
            }

            ShipInventoryCompat.ChuteStoreAttempt attempt = ShipInventoryCompat.AttemptStore(chuteHandle, player, out System.Exception? storeException);

            if (attempt == ShipInventoryCompat.ChuteStoreAttempt.NotAllowed)
            {
                Plugin.Log.LogInfo($"Skipping '{itemName}' - the chute won't currently accept it (blacklisted, permission, or orbit setting).");
                continue;
            }

            if (attempt == ShipInventoryCompat.ChuteStoreAttempt.Threw)
            {
                Plugin.Log.LogWarning(
                    $"Storing '{itemName}' threw ({storeException!.GetType().Name}: {storeException.Message}) - " +
                    "it may already be counted in the ship inventory even though it's still in your hands, so check " +
                    "for a duplicate before storing it again rather than retrying.");
                continue;
            }

            const float despawnConfirmTimeout = 2f;
            float confirmWait = 0f;
            while (item != null && confirmWait < despawnConfirmTimeout)
            {
                yield return null;
                confirmWait += Time.deltaTime;
            }

            if (item != null)
            {
                Plugin.Log.LogWarning(
                    $"'{itemName}' hasn't left your hands {despawnConfirmTimeout:F0}s after being sent to the chute - " +
                    "it may already be counted in the ship inventory even though it's still here. Check for a " +
                    "duplicate before storing it again, or store it manually via the chute's [E] interact.");
                continue;
            }

            Plugin.Log.LogInfo($"Stored item into ship inventory chute: {itemName} (grab wait {grabWait:F2}s, cooldown wait {cooldownWaitDuration:F2}s, despawn confirm {confirmWait:F2}s, total {Time.time - itemProcessingStart:F2}s)");

            float pacingDelay = StartOfRound.Instance.shipHasLanded ? ShipInventoryConfiguration.StoreDelayLanded : ShipInventoryConfiguration.StoreDelayOrbit;
            float waited = 0f;
            while (waited < pacingDelay)
            {
                if (player.isPlayerDead)
                {
                    Plugin.Log.LogInfo("Player died - stopping the rest of the chute auto-store sequence");
                    goto stoppedEarly;
                }
                if (IsJumpingOrFalling(player))
                {
                    Plugin.Log.LogInfo("Player jumped or is falling - stopping the rest of the chute auto-store sequence");
                    goto stoppedEarly;
                }
                yield return null;
                waited += Time.deltaTime;
            }
        }

        stoppedEarly:
        try
        {
            if (player.currentItemSlot != originalSlot && player.currentlyHeldObjectServer == null)
            {
                player.SwitchToItemSlot(originalSlot);
            }
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"Restoring the original hotbar slot after chute auto-store threw ({e.GetType().Name}: {e.Message}) - harmless, just means the hotbar might rest on the last-processed slot instead.");
        }
        }
        finally
        {
            storeInChuteCoroutine = null;
            lastChuteActivityTime = Time.time;
        }
    }

    private static bool IsJumpingOrFalling(PlayerControllerB player)
    {
        return ShipInventoryConfiguration.StopOnJump && (player.isJumping || player.isFallingFromJump || player.isFallingNoJump);
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

        if (Keyboard.current != null && Keyboard.current[Key.G].wasPressedThisFrame)
        {
            Plugin.Log.LogInfo($"DiscardHeldObjectPrefix: suppressing vanilla drop on G press, frame: {Time.frameCount}");
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
        else if (ShipInventoryCompat.IsLoaded && ShipInventoryCompat.IsChuteTriggerInstance(__instance))
        {
            __instance.cooldownTime = StartOfRound.Instance.shipHasLanded
                ? ShipInventoryConfiguration.StoreDelayLanded
                : ShipInventoryConfiguration.StoreDelayOrbit;
        }
    }

    [HarmonyPatch(typeof(PlayerControllerB), "GrabObject", MethodType.Enumerator)]
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> GrabObjectTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo getDecrease = typeof(GrabConfiguration).GetMethod(nameof(GrabConfiguration.GetInteractionCooldownDecrease));
        List<CodeInstruction> instr = new List<CodeInstruction>(instructions);
        int patchedCount = 0;
        for (int i = 0; i < instr.Count; i++)
        {
            CodeInstruction instruction = instr[i];
            yield return instruction;

            if (instruction.opcode == OpCodes.Ldc_R4 && (float)instruction.operand == 0.1f
                && i + 1 < instr.Count && instr[i + 1].opcode == OpCodes.Newobj)
            {
                yield return new CodeInstruction(OpCodes.Call, getDecrease);
                yield return new CodeInstruction(OpCodes.Ldc_R4, 2f);
                yield return new CodeInstruction(OpCodes.Div);
                yield return new CodeInstruction(OpCodes.Sub);
                patchedCount++;
            }

            if (instruction.opcode == OpCodes.Ldc_R4 && (float)instruction.operand == 0.2f
                && i + 1 < instr.Count && instr[i + 1].opcode == OpCodes.Sub)
            {
                yield return new CodeInstruction(OpCodes.Sub);
                yield return new CodeInstruction(OpCodes.Call, getDecrease);
                patchedCount++;
            }
        }

        Plugin.Log.LogInfo($"GrabObjectTranspiler patched {patchedCount} delay checkpoint(s)");
    }
}