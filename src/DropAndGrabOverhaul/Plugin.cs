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

// Here are some basic resources on code style and naming conventions to help
// you in your first CSharp plugin!
// https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions
// https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/identifier-names
// https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/names-of-namespaces

// The BepInAutoPlugin attribute comes from the Hamunii.BepInEx.AutoPlugin
// NuGet package, and it will generate the BepInPlugin attribute for you!
// For more info, see https://github.com/Hamunii/BepInEx.AutoPlugin

/// <summary>
/// The BepInEx plugin class of DropAndGrabOverhaul.
/// </summary>
[BepInAutoPlugin]
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

    // Floor for how long a chute-stored item stays visible/audible before despawning, regardless
    // of how short the configured pacing delay is, so its drop SFX never gets cut off.
    private const float MinSettleTime = 1f;

    private void Awake()
    {
        // BepInEx gives us a logger which we can use to log information.
        // See https://lethal.wiki/dev/fundamentals/logging
        Log = Logger;

        // Initialize configurations
        ItemBlacklist.Initialize(Config);
        InputConfiguration.Initialize(Config);
        GrabConfiguration.Initialize(Config);
        SellConfiguration.Initialize(Config);
        ShipInventoryConfiguration.Initialize(Config);

        // Apply Harmony patch to intercept drop behavior
        // Our patches are annotated directly on their methods with no wrapping class carrying its
        // own [HarmonyPatch], so the parameterless PatchAll() silently skips them - it only scans
        // types that already have a class-level [HarmonyPatch] attribute. Passing typeof(Plugin)
        // uses PatchAll(Type) instead, which allows unannotated container types.
        harmonyInstance = new Harmony("com.github.manvocao0271.dropandgraboverhaul");
        harmonyInstance.PatchAll(typeof(Plugin));

        // Anything we create this early gets destroyed by a scene transition that happens
        // shortly after chainloader startup, so the runner is created lazily once StartOfRound
        // exists instead (see StartOfRoundAwakePostfix below).

        // BepInEx also gives us a config file for easy configuration.
        // See https://lethal.wiki/dev/intermediate/custom-configs

        // We can apply our hooks here.
        // See https://lethal.wiki/dev/fundamentals/patching-code

        // Log our awake here so we can see it in LogOutput.log file
        Log.LogInfo($"Plugin {Name} is loaded!");
    }

    // Dedicated MonoBehaviour on its own GameObject - anything created in Plugin.Awake() gets
    // destroyed by a scene transition shortly after chainloader startup, taking Update() with it.
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

        // ShipInventoryUpdated compatibility: pressing the drop key once while hovering its chute
        // stores the whole inventory (including anything grabbed afterwards) until it's empty, if
        // that mod happens to be installed
        if (ShipInventoryCompat.IsLoaded && ShipInventoryCompat.IsHoveringChute(player) && InputHandler.IsDropKeyPressed())
        {
            // Only start the coroutine if one isn't already running, otherwise it never
            // gets past the first item's delay before being killed and restarted
            if (storeInChuteCoroutine == null)
                storeInChuteCoroutine = runner!.StartCoroutine(StoreAllInChuteCoroutine(player));
            return;
        }

        // Check if player is looking at the company desk's interact trigger (same condition
        // the vanilla game uses to show the "Sell item : [E]" hover tip)
        DepositItemsDesk? desk = UnityEngine.Object.FindObjectOfType<DepositItemsDesk>();
        bool atDesk = desk != null && desk.triggerScript != null && player.hoveringOverTrigger == desk.triggerScript;

        // Auto-sell inventory if enabled and drop key is pressed near the counter
        if (SellConfiguration.AutoSellInventory && atDesk && InputHandler.IsDropKeyPressed() && desk != null)
        {
            // Only start the coroutine if one isn't already running, otherwise it never
            // gets past the first item's delay before being killed and restarted
            if (autoSellCoroutine == null)
                autoSellCoroutine = runner!.StartCoroutine(AutoSellInventoryCoroutine(player, desk));
            return;
        }

        // Don't allow drop-all actions if player is at desk and auto-sell is enabled
        if (atDesk && SellConfiguration.AutoSellInventory)
            return;

        // Check for force drop (holding key) first
        bool isForceDropping = InputHandler.IsForceDropHeld();
        
        // Check if drop key was double-tapped (only if not force dropping)
        bool isDoubleTap = !isForceDropping && InputHandler.IsDoubleTapDrop();

        if (!isForceDropping && !isDoubleTap)
        {
            // DiscardHeldObjectPrefix suppresses every vanilla drop triggered by G, including a
            // plain single tap, so a normal single-item drop has to be replicated here. It fires
            // immediately rather than waiting out the double-tap window, so a following second
            // tap only drops whatever remains - the held item is already gone by then.
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

        // Collect all items to drop (excluding null slots)
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

        // NOTE: we intentionally don't use the vanilla PlayerControllerB.DropAllHeldItemsAndSync
        // here (the method KillPlayer uses to drop everything on death). It also drops
        // ItemOnlySlot - the utility "tab" slot item (walkie-talkie, compass, etc.) - with no
        // way to exclude it, so using it would toss that item too. Looping player.ItemSlots
        // ourselves naturally leaves ItemOnlySlot untouched.
        // Only start the coroutine if one isn't already running, otherwise it never
        // gets past the first item's delay before being killed and restarted
        if (dropAllCoroutine == null)
            dropAllCoroutine = runner!.StartCoroutine(DropAllItemsCoroutine(player, itemsToDropList, isForceDropping));
    }

    private static System.Collections.IEnumerator DropAllItemsCoroutine(PlayerControllerB player, List<GrabbableObject> itemsToDrop, bool isForceDropping)
    {
        // Save the current item slot to restore later
        int originalSlot = player.currentItemSlot;

        // Drop each item by equipping it first, then dropping it
        int droppedCount = 0;
        foreach (GrabbableObject item in itemsToDrop)
        {
            if (item != null)
            {
                string itemName = item.itemProperties.itemName;

                // Find the slot index
                int slotIndex = System.Array.IndexOf(player.ItemSlots, item);

                // Blacklist only applies to the double-tap drop; force drop ignores it
                if (!isForceDropping && ItemBlacklist.IsBlacklisted(itemName))
                {
                    Plugin.Log.LogInfo($"Skipping blacklisted item: {itemName}");
                    continue;
                }

                // Switch to this slot to equip it
                player.SwitchToItemSlot(slotIndex);
                // Now drop it, suppressing the drop-key patch in case this still lands on a G press frame
                isDroppingAll = true;
                player.DiscardHeldObject();
                isDroppingAll = false;
                droppedCount++;
                Plugin.Log.LogInfo($"Dropped item: {itemName}");

                // currentlyHeldObjectServer is only cleared once the ThrowObjectClientRpc echo
                // arrives back on this client. Switching slots before that lands makes
                // SwitchToItemSlot overwrite the reference while the throw is still in flight -
                // the delayed echo then finds a mismatched currentlyHeldObjectServer and the
                // vanilla ThrowObjectClientRpc safety check logs "not the same as
                // currentlyHeldObjectServer" and skips clearing it, leaving the item stuck
                // invisible/undropped for everyone. Wait for the reference to actually clear,
                // with a timeout so a dropped RPC can't hang us - but if it does time out (e.g.
                // a laggy host), stop here instead of switching slots anyway, so the still-
                // in-flight echo can still land on a matching reference later.
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

        // Restore the original hotbar slot index, even if it's now empty - but only if we
        // actually switched away from it and it's currently safe to do so. SwitchToItemSlot
        // unconditionally overwrites currentlyHeldObjectServer, so calling it unconditionally
        // here reproduces the exact same desync race the per-item wait above guards against:
        // it would clobber the reference for a throw that's still in flight, whether that's
        // our own timed-out item above, or a wholly unrelated drop (e.g. the immediate
        // single-tap drop) that happened to be in progress concurrently on another item.
        if (player.currentItemSlot != originalSlot && player.currentlyHeldObjectServer == null)
        {
            player.SwitchToItemSlot(originalSlot);
        }

        Plugin.Log.LogInfo($"Dropped {droppedCount} items total");
        dropAllCoroutine = null;
    }

    private static System.Collections.IEnumerator AutoSellInventoryCoroutine(PlayerControllerB player, DepositItemsDesk desk)
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
                // Suppress the drop-key patch from swallowing this call if it lands on a G press frame
                isPlacingOnCounter = true;
                desk.PlaceItemOnCounter(player);
                isPlacingOnCounter = false;
                soldCount++;
                Plugin.Log.LogInfo($"Sold item: {itemName}");
                // Wait for the grab animation to complete before selling the next item
                yield return new WaitForSeconds(0.2f);
            }
        }
        Plugin.Log.LogInfo($"Sold {soldCount} items total");
        autoSellCoroutine = null;
    }

    private static System.Collections.IEnumerator StoreAllInChuteCoroutine(PlayerControllerB player)
    {
        Vector3 chutePosition = player.hoveringOverTrigger.transform.position;

        int storedCount = 0;
        // Re-check the player's slots every iteration (instead of snapshotting once up front) so
        // items grabbed after this coroutine already started still get caught and stored too,
        // rather than only ever processing whatever was held at the very start. Items are tracked
        // by reference (not slot index) once claimed since their slot doesn't actually go null
        // until FinalizeStoredItemAfterDelay runs later - without this, the same still-pending
        // item would keep getting reselected every iteration until then.
        var processedItems = new HashSet<GrabbableObject>();
        while (true)
        {
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

            processedItems.Add(item);

            string itemName = item.itemProperties.itemName;
            player.SwitchToItemSlot(slot);

            // Detach the item from the player's hand and hand it off to GrabbableObject's own
            // fall-curve logic (the same thing that runs when any item is dropped/placed in the
            // world) so it visibly drops onto the chute and plays its drop sound on landing,
            // instead of just vanishing straight out of the player's hand.
            item.isHeld = false;
            item.isPocketed = false;
            item.parentObject = null;
            item.transform.SetParent(null, true);
            item.EnablePhysics(true);
            item.EnableItemMeshes(true);
            item.startFallingPosition = item.transform.position;
            item.targetFloorPosition = chutePosition;
            item.fallTime = 0f;

            // Mirrors what DiscardHeldObject/PlaceGrabbableObject do when un-equipping an item -
            // done manually here (instead of relying on DestroyItemInSlot's own equivalent logic
            // below) since that only ever runs for whichever slot happens to be currentItemSlot.
            HUDManager.Instance.itemSlotIcons[slot].enabled = false;
            player.carryWeight = Mathf.Clamp(player.carryWeight - (item.itemProperties.weight - 1f), 1f, 10f);
            player.isHoldingObject = false;
            if (player.currentlyHeldObjectServer == item)
                player.currentlyHeldObjectServer = null;

            // Pacing before the next item starts falling can be configured very short, but the
            // item itself still needs enough time on screen for its fall animation and drop SFX to
            // finish before it's destroyed - so that part always waits at least MinSettleTime.
            float pacingDelay = StartOfRound.Instance.shipHasLanded ? ShipInventoryConfiguration.StoreDelayLanded : ShipInventoryConfiguration.StoreDelayOrbit;
            float settleDelay = Mathf.Max(pacingDelay, MinSettleTime);
            runner!.StartCoroutine(FinalizeStoredItemAfterDelay(player, item, slot, itemName, settleDelay));
            storedCount++;

            yield return new WaitForSeconds(pacingDelay);
        }

        if (storedCount > 0)
            StartOfRound.Instance.SendChangedWeightEvent();

        storeInChuteCoroutine = null;
    }

    private static System.Collections.IEnumerator FinalizeStoredItemAfterDelay(PlayerControllerB player, GrabbableObject item, int slot, string itemName, float delay)
    {
        yield return new WaitForSeconds(delay);

        ShipInventoryCompat.FinalizeStoredItem(player, item, slot);
        Plugin.Log.LogInfo($"Stored item into ship inventory chute: {itemName}");
    }

    // Anything created in Plugin.Awake() gets destroyed by an early scene transition, so the
    // runner is created lazily here instead, once StartOfRound exists and that transition is over
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

    // Harmony patch to intercept the game's default drop call
    [HarmonyPatch(typeof(PlayerControllerB), "DiscardHeldObject")]
    [HarmonyPrefix]
    private static bool DiscardHeldObjectPrefix(PlayerControllerB __instance)
    {
        // Never suppress our own sell-placement or drop-all calls
        if (isPlacingOnCounter || isDroppingAll)
            return true;

        // Supress ALL drops triggered by the G key (single-tap, double-tap, or hold)
        // Let the Update() method handle all drop logic with proper blacklist checks
        if (Keyboard.current != null && Keyboard.current[Key.G].wasPressedThisFrame)
        {
            Plugin.Log.LogInfo($"DiscardHeldObjectPrefix: suppressing vanilla drop on G press, frame: {Time.frameCount}");
            return false;
        }

        return true;
    }

    // Harmony patch to shorten the vanilla 0.2s grab cooldown to the configured delay.
    // InteractTrigger.cooldownTime is what Interact() copies into currentCooldownValue on every
    // successful grab, so overriding it here is enough - no need to fight the cooldown elsewhere.
    [HarmonyPatch(typeof(InteractTrigger), "Interact")]
    [HarmonyPrefix]
    private static void InteractTriggerInteractPrefix(InteractTrigger __instance)
    {
        if (__instance.GetComponentInParent<GrabbableObject>() != null)
        {
            __instance.cooldownTime = GrabConfiguration.GrabDelay;
        }
    }

    // Harmony transpiler to shrink the two fixed waits inside PlayerControllerB's GrabObject()
    // coroutine - WaitForSeconds(0.1f) and WaitForSeconds(grabObjectAnimationTime - 0.2f) - which
    // is what actually keeps isGrabbingObjectAnimation true and blocks further grabs/interacts.
    // Patching BeginGrabObject or clearing the flag directly doesn't work: a second press calls
    // StopCoroutine() on the still-running first grab before it finishes, corrupting it. Rewriting
    // the coroutine's own IL constants (same approach as the NoGrabDelay mod) shortens the wait
    // without touching the flag or the server-sync loop in between.
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

            // WaitForSeconds(0.1f) -> WaitForSeconds(0.1f - decrease / 2f)
            if (instruction.opcode == OpCodes.Ldc_R4 && (float)instruction.operand == 0.1f
                && i + 1 < instr.Count && instr[i + 1].opcode == OpCodes.Newobj)
            {
                yield return new CodeInstruction(OpCodes.Call, getDecrease);
                yield return new CodeInstruction(OpCodes.Ldc_R4, 2f);
                yield return new CodeInstruction(OpCodes.Div);
                yield return new CodeInstruction(OpCodes.Sub);
                patchedCount++;
            }

            // (grabObjectAnimationTime - 0.2f) -> (grabObjectAnimationTime - 0.2f) - decrease
            if (instruction.opcode == OpCodes.Ldc_R4 && (float)instruction.operand == 0.2f
                && i + 1 < instr.Count && instr[i + 1].opcode == OpCodes.Sub)
            {
                yield return new CodeInstruction(OpCodes.Sub);
                yield return new CodeInstruction(OpCodes.Call, getDecrease);
                patchedCount++;
            }
        }

        // Should log 2 - if this logs 0, GrabObject()'s IL no longer matches these patterns
        // (e.g. game update) and the transpiler silently did nothing.
        Plugin.Log.LogInfo($"GrabObjectTranspiler patched {patchedCount} delay checkpoint(s)");
    }
}
