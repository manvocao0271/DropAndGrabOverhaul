using System.Diagnostics.CodeAnalysis;
using DropAndGrabOverhaul.Configuration;
using DropAndGrabOverhaul.Features;
using DropAndGrabOverhaul.Inputs;
using DropAndGrabOverhaul.Inventory;
using DropAndGrabOverhaul.Patches;
using GameNetcodeStuff;
using UnityEngine;

namespace DropAndGrabOverhaul;

// The mod's per-frame loop: turns drop-key input into single drops, drop-all sequences and desk
// auto-sell. Created lazily by Patches/StartOfRoundPatch (see CLAUDE.md gotcha #2).
internal sealed class UpdateRunner : MonoBehaviour
{
    // How often to re-search for the company desk while it isn't found (it only exists on the
    // company moon, so on every other moon a search would always come up empty).
    private const float DeskSearchIntervalSeconds = 1f;

    private static UpdateRunner? instance;

    // One gate per routine, owned by this instance so they can never outlive it.
    private readonly CoroutineGate dropAllGate = new();
    private readonly CoroutineGate autoSellGate = new();

    private DepositItemsDesk? cachedDesk;
    private float nextDeskSearchTime;

    internal static void EnsureCreated()
    {
        if (instance != null)
            return;

        GameObject runnerObject = new GameObject("DropAndGrabOverhaulRunner");
        DontDestroyOnLoad(runnerObject);
        instance = runnerObject.AddComponent<UpdateRunner>();

        ModLog.Info("UpdateRunner created via StartOfRound.Awake postfix");
    }

    private void Update()
    {
        PlayerControllerB? player = StartOfRound.Instance?.localPlayerController;
        if (player == null)
            return;

        // Looking at the company counter: the drop key sells instead of dropping.
        if (SellConfiguration.AutoSellInventory && IsHoveringDesk(player, out DepositItemsDesk? desk))
        {
            if (!autoSellGate.IsRunning && InputHandler.IsDropKeyPressed())
                autoSellGate.Start(this, AutoSellRoutine.Run(player, desk));

            InputHandler.ResetDropKeyTracking();
            return;
        }

        switch (InputHandler.Poll())
        {
            case DropGesture.Tap:
                DropHeldItem(player);
                break;
            case DropGesture.DoubleTap:
                StartDropAll(player, "Double-tap drop detected - dropping all items",
                    includeReservedSlots: false, ignoreBlacklist: false, logIfNothingToDrop: true);
                break;
            case DropGesture.ForceDrop:
                StartDropAll(player, "Force drop detected - dropping hotbar items (ignoring blacklist)",
                    includeReservedSlots: false, ignoreBlacklist: true, logIfNothingToDrop: false);
                break;
            case DropGesture.ForceDropReserved:
                // Only differs from ForceDrop with ReservedItemSlotCore installed.
                StartDropAll(player, "Force drop held longer - dropping ALL items including reserved slots (ignoring blacklist)",
                    includeReservedSlots: true, ignoreBlacklist: true, logIfNothingToDrop: false);
                break;
        }
    }

    private static void DropHeldItem(PlayerControllerB player)
    {
        if (player.currentlyHeldObjectServer == null)
            return;

        ModLog.Info("Single tap detected - dropping held item immediately");
        using (VanillaDiscard.Allow())
        {
            player.DiscardHeldObject();
        }
    }

    private void StartDropAll(PlayerControllerB player, string logMessage, bool includeReservedSlots, bool ignoreBlacklist, bool logIfNothingToDrop)
    {
        // A held force drop reports its gesture every frame, so bail out quietly while a
        // drop-all is still running; the next frame re-checks once it has finished.
        if (dropAllGate.IsRunning)
            return;

        // The first hold stage and double-tap only touch the main hotbar; reserved slots are
        // left alone until the key has been held longer (includeReservedSlots).
        var items = InventoryAccessor.GetItemSlots(player, includeReservedSlots);
        if (items.Count == 0)
        {
            if (logIfNothingToDrop)
                ModLog.Info("No items to drop");
            return;
        }

        ModLog.Info(logMessage);
        dropAllGate.Start(this, DropAllRoutine.Run(player, items, ignoreBlacklist));
    }

    // True while the player is looking at the company desk's counter trigger.
    private bool IsHoveringDesk(PlayerControllerB player, [NotNullWhen(true)] out DepositItemsDesk? desk)
    {
        desk = null;

        // Cheap early-out: the desk can only be the hovered trigger if something is hovered.
        if (player.hoveringOverTrigger == null)
            return false;

        if (cachedDesk == null && Time.time >= nextDeskSearchTime)
        {
            cachedDesk = FindObjectOfType<DepositItemsDesk>();
            nextDeskSearchTime = Time.time + DeskSearchIntervalSeconds;
        }

        if (cachedDesk == null || cachedDesk.triggerScript == null)
            return false;

        if (player.hoveringOverTrigger != cachedDesk.triggerScript)
            return false;

        desk = cachedDesk;
        return true;
    }
}
