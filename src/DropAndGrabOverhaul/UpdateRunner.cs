using System;
using System.Diagnostics.CodeAnalysis;
using DropAndGrabOverhaul.Configuration;
using DropAndGrabOverhaul.Features;
using DropAndGrabOverhaul.Inputs;
using DropAndGrabOverhaul.Inventory;
using GameNetcodeStuff;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DropAndGrabOverhaul;

// The per-frame loop: turns drop-key input into single drops, drop-all sequences and desk
// auto-sell. Created lazily by StartOfRoundPatch (CLAUDE.md gotcha #2).
internal sealed class UpdateRunner : MonoBehaviour
{
    private static UpdateRunner? instance;

    // DiscardPerformedPatch hands the drop key back to vanilla while this is false, including
    // after repeated failures in Update (see below), so a bug here can't leave the key dead.
    internal static bool IsActive => instance != null && !instance.faulted;

    private const int MaxConsecutiveFailures = 3;
    private int consecutiveFailures;
    private bool faulted;

    // Instance fields, so the gates die with the runner.
    private readonly CoroutineGate dropAllGate = new();
    private readonly CoroutineGate autoSellGate = new();

    // Searched for at most once per scene change; the scene events below clear the cache.
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

    // A new scene gets a fresh start: retry the mod after a fault, and re-find the desk.
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        consecutiveFailures = 0;
        faulted = false;
        ForgetDesk();
    }

    private void OnSceneUnloaded(Scene scene) => ForgetDesk();

    private void ForgetDesk()
    {
        cachedDesk = null;
        deskSearched = false;
    }

    private void Update()
    {
        if (faulted)
            return;

        try
        {
            Tick();
            consecutiveFailures = 0;
        }
        catch (Exception e)
        {
            consecutiveFailures++;
            ModLog.Error($"UpdateRunner failed ({consecutiveFailures}/{MaxConsecutiveFailures}): {e}");

            if (consecutiveFailures >= MaxConsecutiveFailures)
            {
                faulted = true;
                ModLog.Error("Too many consecutive failures - vanilla's drop handler takes over until the next scene.");
            }
        }
    }

    private void Tick()
    {
        PlayerControllerB? player = StartOfRound.Instance?.localPlayerController;
        if (player == null)
            return;

        // Reset rather than just return, so a key held through a blocked state can't later
        // count as a gesture.
        if (!DropGuard.CanAcceptDropInput(player))
        {
            InputHandler.ResetDropKeyTracking();
            return;
        }

        // Vanilla's handler did this on a press that got past its grab-animation and
        // slot-switch-cooldown checks.
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
                if (DropGuard.CanDropHeldItemNow(player))
                    DropHeldItem(player);
                break;
            case DropGesture.DoubleTap:
                StartDropAll(player, includeReservedSlots: false, ignoreBlacklist: false);
                break;
            case DropGesture.ForceDrop:
                StartDropAll(player, includeReservedSlots: false, ignoreBlacklist: true);
                break;
            case DropGesture.ForceDropReserved:
                // Only differs from ForceDrop with ReservedItemSlotCore installed.
                StartDropAll(player, includeReservedSlots: true, ignoreBlacklist: true);
                break;
        }
    }

    private void DropHeldItem(PlayerControllerB player)
    {
        GrabbableObject? held = player.currentlyHeldObjectServer;
        if (held == null)
            return;

        // Vanilla placed an item already over the counter on it instead of dropping it.
        DepositItemsDesk? desk = GetDesk();
        if (desk != null && desk.triggerCollider != null && desk.triggerCollider.bounds.Contains(held.transform.position))
        {
            desk.PlaceItemOnCounter(player);
            return;
        }

        player.DiscardHeldObject();
    }

    private void StartDropAll(PlayerControllerB player, bool includeReservedSlots, bool ignoreBlacklist)
    {
        // A held force drop reports its gesture every frame; stay quiet while one is running.
        if (dropAllGate.IsRunning)
            return;

        var items = InventoryAccessor.GetItemSlots(player, includeReservedSlots);
        if (items.Count > 0)
            dropAllGate.Start(this, DropAllRoutine.Run(player, items, ignoreBlacklist));
    }

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

    private bool IsHoveringDesk(PlayerControllerB player, [NotNullWhen(true)] out DepositItemsDesk? desk)
    {
        desk = null;

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
