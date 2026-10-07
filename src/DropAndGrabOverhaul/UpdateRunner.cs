using System.Diagnostics.CodeAnalysis;
using DropAndGrabOverhaul.Configuration;
using DropAndGrabOverhaul.Features;
using DropAndGrabOverhaul.Inputs;
using DropAndGrabOverhaul.Inventory;
using GameNetcodeStuff;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DropAndGrabOverhaul;

// The mod's per-frame loop: turns drop-key input into single drops, drop-all sequences and desk
// auto-sell. Created lazily by Patches/StartOfRoundPatch (see CLAUDE.md gotcha #2).
internal sealed class UpdateRunner : MonoBehaviour
{
    private static UpdateRunner? instance;

    // True while the runner exists; DiscardPerformedPatch hands the drop key back to vanilla when
    // it doesn't (CLAUDE.md gotcha #2 - the runner can be lost to an early scene transition).
    internal static bool IsActive => instance != null;

    // One gate per routine, owned by this instance so they can never outlive it.
    private readonly CoroutineGate dropAllGate = new();
    private readonly CoroutineGate autoSellGate = new();

    // The company desk only exists on the company moon. It is searched for at most once per scene
    // change (the flag is cleared by the scene events below), so every other moon costs one
    // failed FindObjectOfType per scene instead of one per second.
    private DepositItemsDesk? cachedDesk;
    private bool deskSearched;

    internal static void EnsureCreated()
    {
        if (instance != null)
            return;

        GameObject runnerObject = new GameObject("DropAndGrabOverhaulRunner");
        DontDestroyOnLoad(runnerObject);
        instance = runnerObject.AddComponent<UpdateRunner>();

        ModLog.Info("UpdateRunner created via StartOfRound.Awake postfix");
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => ForgetDesk();

    private void OnSceneUnloaded(Scene scene) => ForgetDesk();

    private void ForgetDesk()
    {
        cachedDesk = null;
        deskSearched = false;
    }

    private void Update()
    {
        PlayerControllerB? player = StartOfRound.Instance?.localPlayerController;
        if (player == null)
            return;

        // Mirror vanilla's own guards (typing in chat, menus, animations, ...). Reset rather than
        // just return so a key held through one of those states can't later count as a gesture.
        if (!DropGuard.CanAcceptDropInput(player))
        {
            InputHandler.ResetDropKeyTracking();
            return;
        }

        // Vanilla's handler cancelled ship build mode on a press that got past its grab-animation
        // and slot-switch-cooldown checks; it no longer runs, so repeat that here.
        if (InputHandler.WasDropKeyPressedThisFrame() && ShipBuildModeManager.Instance != null
            && DropGuard.CanCancelBuildMode(player))
            ShipBuildModeManager.Instance.CancelBuildMode();

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
                // Vanilla ignores a drop press mid-grab, mid-throw or right after a slot switch.
                if (DropGuard.CanDropHeldItemNow(player))
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

    private void DropHeldItem(PlayerControllerB player)
    {
        GrabbableObject? held = player.currentlyHeldObjectServer;
        if (held == null)
            return;

        // Vanilla put an item that is already over the counter on the counter instead of dropping
        // it. That part of its handler no longer runs, so do it here.
        DepositItemsDesk? desk = GetDesk();
        if (desk != null && desk.triggerCollider != null && desk.triggerCollider.bounds.Contains(held.transform.position))
        {
            ModLog.Info("Single tap detected over the company counter - placing held item on it");
            desk.PlaceItemOnCounter(player);
            return;
        }

        ModLog.Info("Single tap detected - dropping held item immediately");
        player.DiscardHeldObject();
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

    // The company desk, or null when this scene has none. Searched for once per scene change.
    private DepositItemsDesk? GetDesk()
    {
        if (!deskSearched)
        {
            deskSearched = true;
            cachedDesk = FindObjectOfType<DepositItemsDesk>();
            ModLog.Info(cachedDesk != null ? "Company desk found" : "No company desk in this scene");
        }

        return cachedDesk;
    }

    // True while the player is looking at the company desk's counter trigger.
    private bool IsHoveringDesk(PlayerControllerB player, [NotNullWhen(true)] out DepositItemsDesk? desk)
    {
        desk = null;

        // Cheap early-out: the desk can only be the hovered trigger if something is hovered.
        if (player.hoveringOverTrigger == null)
            return false;

        DepositItemsDesk? found = GetDesk();
        if (found == null || found.triggerScript == null)
            return false;

        if (player.hoveringOverTrigger != found.triggerScript)
            return false;

        desk = found;
        return true;
    }
}
