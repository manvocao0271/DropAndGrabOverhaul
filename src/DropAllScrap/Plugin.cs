using BepInEx;
using BepInEx.Logging;
using BepInEx.Configuration;
using System.Collections.Generic;
using UnityEngine;
using HarmonyLib;
using DropAllScrap.Input;
using DropAllScrap.Inventory;
using DropAllScrap.Configuration;
using GameNetcodeStuff;
using UnityEngine.InputSystem;

namespace DropAllScrap;

// Here are some basic resources on code style and naming conventions to help
// you in your first CSharp plugin!
// https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions
// https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/identifier-names
// https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/names-of-namespaces

// The BepInAutoPlugin attribute comes from the Hamunii.BepInEx.AutoPlugin
// NuGet package, and it will generate the BepInPlugin attribute for you!
// For more info, see https://github.com/Hamunii/BepInEx.AutoPlugin

/// <summary>
/// The BepInEx plugin class of DropAllScrap.
/// </summary>
[BepInAutoPlugin]
public partial class Plugin : BaseUnityPlugin
{
    internal static ManualLogSource Log { get; private set; } = null!;
    private static Harmony? harmonyInstance;

    private void Awake()
    {
        // BepInEx gives us a logger which we can use to log information.
        // See https://lethal.wiki/dev/fundamentals/logging
        Log = Logger;

        // Initialize configurations
        ItemBlacklist.Initialize(Config);
        InputConfiguration.Initialize(Config);

        // Apply Harmony patch to intercept drop behavior
        harmonyInstance = new Harmony("com.github.manvocao0271.dropallscrap");
        harmonyInstance.PatchAll();

        // BepInEx also gives us a config file for easy configuration.
        // See https://lethal.wiki/dev/intermediate/custom-configs

        // We can apply our hooks here.
        // See https://lethal.wiki/dev/fundamentals/patching-code

        // Log our awake here so we can see it in LogOutput.log file
        Log.LogInfo($"Plugin {Name} is loaded!");
    }

    private void Update()
    {
        // Check for force drop (holding key) first
        bool isForceDropping = InputHandler.IsForceDropHeld();
        
        // Check if drop key was double-tapped (only if not force dropping)
        bool isDoubleTap = !isForceDropping && InputHandler.IsDoubleTapDrop();

        if (!isForceDropping && !isDoubleTap)
            return;

        Plugin.Log.LogInfo(isForceDropping ? "Force drop detected - dropping ALL items (ignoring blacklist)" : "Double-tap drop detected - dropping all items");

        PlayerControllerB? player = StartOfRound.Instance?.localPlayerController;
        if (player == null)
        {
            Plugin.Log.LogWarning("Could not access player");
            return;
        }

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

        // Save the current item slot to restore later
        int originalSlot = player.currentItemSlot;

        // Drop each item by equipping it first, then dropping it
        int droppedCount = 0;
        foreach (GrabbableObject item in itemsToDropList)
        {
            if (item != null)
            {
                string itemName = item.itemProperties.itemName;

                // Find the slot index
                int slotIndex = System.Array.IndexOf(player.ItemSlots, item);

                // During double-tap, don't drop the currently held blacklisted item
                if (!isForceDropping && slotIndex == player.currentItemSlot && ItemBlacklist.IsBlacklisted(itemName)) continue;

                // Check if item is blacklisted (unless force dropping)
                if (!isForceDropping && ItemBlacklist.IsBlacklisted(itemName))
                {
                    Plugin.Log.LogInfo($"Skipping blacklisted item: {itemName}");
                    continue;
                }

                // Switch to this slot to equip it
                player.SwitchToItemSlot(slotIndex);
                // Now drop it
                player.DiscardHeldObject();
                droppedCount++;
                Plugin.Log.LogInfo($"Dropped item: {itemName}");
            }
        }

        // Restore the original hotbar slot index, even if it's now empty
        player.SwitchToItemSlot(originalSlot);

        Plugin.Log.LogInfo($"Dropped {droppedCount} items total");
    }

    // Harmony patch to intercept the game's default drop call
    [HarmonyPatch(typeof(PlayerControllerB), "DiscardHeldObject")]
    [HarmonyPrefix]
    private static bool DiscardHeldObjectPrefix(PlayerControllerB __instance)
    {
        // Supress ALL drops triggered by the G key (single-tap, double-tap, or hold)
        // Let the Update() method handle all drop logic with proper blacklist checks
        if (Keyboard.current != null && Keyboard.current[Key.G].wasPressedThisFrame)
        {
            return false;
        }

        return true;
    }
}
