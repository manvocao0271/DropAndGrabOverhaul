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
                var loopAbort = GetChuteAbortReason(player, chuteHandle);
                if (loopAbort is { } loopReason)
                {
                    LogChuteAbort(loopReason, " - stopping the rest of the chute auto-store sequence");
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
                while (player.isGrabbingObjectAnimation &&
                       player.currentlyGrabbingObject == item &&
                       grabWait < GrabAnimationWaitTimeout)
                {
                    yield return null;
                    grabWait += Time.deltaTime;
                }

                if (item == null)
                {
                    Plugin.Log.LogInfo("Item was destroyed while waiting for its grab animation to finish - skipping it.");
                    continue;
                }

                processedItems.Add(item);

                string itemName = item.itemProperties.itemName;
                float itemProcessingStart = Time.time;

                // item can be destroyed out from under us during any of the yields below
                // (e.g. it despawns for an unrelated reason). currentlyHeldObjectServer also
                // reads as "null-ish" once that happens, so a plain equality check against
                // item could look like a match even though there's nothing left to store -
                // item's own liveness has to be part of every one of these checks.
                bool IsEquipped() => item != null && player.currentlyHeldObjectServer == item;

                // Make sure the intended item is actually equipped before
                // invoking ShipInventoryUpdated's chute interaction.
                if (!IsEquipped())
                {
                    player.SwitchToItemSlot(slot);

                    const float equipTimeout = 2f;
                    float equipWait = 0f;

                    while (!IsEquipped() && equipWait < equipTimeout)
                    {
                        var equipAbort = GetChuteAbortReason(player, chuteHandle);
                        if (equipAbort is { } equipReason)
                        {
                            LogChuteAbort(equipReason, " while equipping item for chute storage");
                            goto stoppedEarly;
                        }

                        yield return null;
                        equipWait += Time.deltaTime;
                    }

                    if (!IsEquipped())
                    {
                        Plugin.Log.LogWarning(
                            $"Could not equip '{itemName}' in slot {slot} within {equipTimeout:F1}s - " +
                            "skipping it rather than invoking the chute with the wrong held item.");
                        continue;
                    }
                }

                float cooldownWaitStart = Time.time;
                const float chuteCooldownTimeout = 3f;

                do
                {
                    var cooldownAbort = GetChuteAbortReason(player, chuteHandle);
                    if (cooldownAbort is { } cooldownReason)
                    {
                        LogChuteAbort(cooldownReason, " - stopping the rest of the chute auto-store sequence");
                        goto stoppedEarly;
                    }

                    yield return null;
                }
                while (ShipInventoryCompat.IsChuteOnCooldown(chuteHandle) &&
                       Time.time - cooldownWaitStart < chuteCooldownTimeout);

                float cooldownWaitDuration = Time.time - cooldownWaitStart;

                if (ShipInventoryCompat.IsChuteOnCooldown(chuteHandle))
                {
                    Plugin.Log.LogWarning(
                        $"Chute stayed on cooldown for {chuteCooldownTimeout:F0}s - " +
                        $"skipping '{itemName}' for now rather than waiting longer.");
                    continue;
                }

                // Re-check immediately before invoking the interaction. The cooldown wait
                // above can run for several real seconds, and nothing here blocks the
                // player's own input during that time - if they manually switch slots or
                // grab something else while we're waiting, this is what catches the drift
                // before we hand the wrong item to the chute. (currentlyHeldObjectServer
                // itself is set synchronously and locally by SwitchToItemSlot, with no
                // network round-trip involved - the risk here is the player acting during
                // our own wait, not a sync delay on ShipInventoryUpdated's side.)
                if (!IsEquipped())
                {
                    Plugin.Log.LogWarning(
                        $"'{itemName}' is no longer the currently held item immediately before chute storage - " +
                        "skipping it to avoid storing the wrong item.");
                    continue;
                }

                ShipInventoryCompat.ChuteStoreAttempt attempt =
                    ShipInventoryCompat.AttemptStore(
                        chuteHandle,
                        player,
                        item,
                        out System.Exception? storeException);

                if (attempt == ShipInventoryCompat.ChuteStoreAttempt.NotAllowed)
                {
                    Plugin.Log.LogInfo(
                        $"Skipping '{itemName}' - the chute won't currently accept it right now " +
                        "(e.g. not enough inventory space, a blacklisted item, or another eligibility check).");
                    continue;
                }

                if (attempt == ShipInventoryCompat.ChuteStoreAttempt.Threw)
                {
                    Plugin.Log.LogWarning(
                        $"Storing '{itemName}' threw " +
                        $"({storeException!.GetType().Name}: {storeException.Message}) - " +
                        "it may already be counted in the ship inventory even though it's still in your hands, " +
                        "so check for a duplicate before storing it again rather than retrying.");
                    continue;
                }

                // AttemptStore returning Called only confirms Interact() ran without
                // throwing - ShipInventoryUpdated's underlying InteractTrigger has several
                // paths (mid-animation, cooldown boundary, etc.) where it silently declines
                // to fire its own storage callback at all. The despawn wait below, not this
                // return value, is the real confirmation that something actually happened.
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
                        "it may already be counted in the ship inventory even though it's still here. " +
                        "Check for a duplicate before storing it again, or store it manually via the chute's [E] interact.");
                    continue;
                }

                Plugin.Log.LogInfo(
                    $"Stored item into ship inventory chute: {itemName} " +
                    $"(grab wait {grabWait:F2}s, cooldown wait {cooldownWaitDuration:F2}s, " +
                    $"despawn confirm {confirmWait:F2}s, total {Time.time - itemProcessingStart:F2}s)");

                float pacingDelay = StartOfRound.Instance.shipHasLanded
                    ? ShipInventoryConfiguration.StoreDelayLanded
                    : ShipInventoryConfiguration.StoreDelayOrbit;

                float waited = 0f;

                while (waited < pacingDelay)
                {
                    var pacingAbort = GetChuteAbortReason(player, chuteHandle);
                    if (pacingAbort is { } pacingReason)
                    {
                        LogChuteAbort(pacingReason, " - stopping the rest of the chute auto-store sequence");
                        goto stoppedEarly;
                    }

                    yield return null;
                    waited += Time.deltaTime;
                }
            }

        stoppedEarly:

            try
            {
                if (player.currentItemSlot != originalSlot &&
                    player.currentlyHeldObjectServer == null)
                {
                    player.SwitchToItemSlot(originalSlot);
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning(
                    $"Restoring the original hotbar slot after chute auto-store threw " +
                    $"({e.GetType().Name}: {e.Message}) - harmless, just means the hotbar might rest " +
                    "on the last-processed slot instead.");
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
        return ShipInventoryConfiguration.StopOnJump &&
               (player.isJumping ||
                player.isFallingFromJump ||
                player.isFallingNoJump);
    }

    // Shared by every wait loop in StoreAllInChuteCoroutine (the main loop, the equip
    // wait, the cooldown wait, and the pacing wait) so the abort conditions - and their
    // log wording - only need to be maintained in one place. Previously each loop
    // duplicated its own copy of these checks, and only the outer loop checked chute
    // validity at all.
    private static (string Message, bool IsWarning)? GetChuteAbortReason(PlayerControllerB player, object chuteHandle)
    {
        if (player.isPlayerDead)
            return ("Player died", false);

        if (IsJumpingOrFalling(player))
            return ("Player jumped or is falling", false);

        if (!player.isInHangarShipRoom)
            return ("Left the hangar ship room", false);

        if (!ShipInventoryCompat.IsChuteHandleValid(chuteHandle))
            return ("Lost the chute reference mid-sequence", true);

        return null;
    }

    private static void LogChuteAbort((string Message, bool IsWarning) reason, string suffix)
    {
        string message = reason.Message + suffix;

        if (reason.IsWarning)
            Plugin.Log.LogWarning(message);
        else
            Plugin.Log.LogInfo(message);
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
            Plugin.Log.LogInfo(
                $"DiscardHeldObjectPrefix: suppressing vanilla drop on G press, frame: {Time.frameCount}");
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
        else if (ShipInventoryCompat.IsLoaded &&
                 ShipInventoryCompat.IsChuteTriggerInstance(__instance))
        {
            __instance.cooldownTime = StartOfRound.Instance.shipHasLanded
                ? ShipInventoryConfiguration.StoreDelayLanded
                : ShipInventoryConfiguration.StoreDelayOrbit;
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