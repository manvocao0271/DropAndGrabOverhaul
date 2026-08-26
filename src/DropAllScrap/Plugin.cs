using BepInEx;
using BepInEx.Logging;
using BepInEx.Configuration;
using System.Collections.Generic;
using UnityEngine;
using DropAllScrap.Input;
using DropAllScrap.Inventory;
using DropAllScrap.Configuration;
using GameNetcodeStuff;

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

    private void Awake()
    {
        // BepInEx gives us a logger which we can use to log information.
        // See https://lethal.wiki/dev/fundamentals/logging
        Log = Logger;

        // Initialize configurations
        ItemBlacklist.Initialize(Config);
        InputConfiguration.Initialize(Config);

        // BepInEx also gives us a config file for easy configuration.
        // See https://lethal.wiki/dev/intermediate/custom-configs

        // We can apply our hooks here.
        // See https://lethal.wiki/dev/fundamentals/patching-code

        // Log our awake here so we can see it in LogOutput.log file
        Log.LogInfo($"Plugin {Name} is loaded!");
    }

    private void Update()
    {
        // Check if drop key was double-tapped
        if (!InputHandler.IsDoubleTapDrop())
            return;

        Plugin.Log.LogInfo("Double-tap drop detected - dropping all items");

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

                // Check if item is blacklisted
                if (ItemBlacklist.IsBlacklisted(itemName))
                {
                    Plugin.Log.LogInfo($"Skipping blacklisted item: {itemName}");
                    continue;
                }

                // Find the slot index
                int slotIndex = System.Array.IndexOf(player.ItemSlots, item);
                if (slotIndex >= 0)
                {
                    // Switch to this slot to equip it
                    player.SwitchToItemSlot(slotIndex);
                    // Now drop it
                    player.DiscardHeldObject();
                    droppedCount++;
                    Plugin.Log.LogInfo($"Dropped item: {itemName}");
                }
            }
        }

        // Restore the original item slot (or switch to first non-empty slot if original was dropped)
        if (originalSlot >= 0 && originalSlot < player.ItemSlots.Length && player.ItemSlots[originalSlot] != null)
        {
            player.SwitchToItemSlot(originalSlot);
        }
        else if (player.ItemSlots != null)
        {
            // Find first non-empty slot
            for (int i = 0; i < player.ItemSlots.Length; i++)
            {
                if (player.ItemSlots[i] != null)
                {
                    player.SwitchToItemSlot(i);
                    break;
                }
            }
        }

        Plugin.Log.LogInfo($"Dropped {droppedCount} items total");
    }
}
