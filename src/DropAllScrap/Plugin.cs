using BepInEx;
using BepInEx.Logging;
using BepInEx.Configuration;
using UnityEngine;
using DropAllScrap.Input;
using DropAllScrap.Inventory;
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
    public static ConfigEntry<KeyCode> ActivationKey { get; private set; } = null!;

    private void Awake()
    {
        // BepInEx gives us a logger which we can use to log information.
        // See https://lethal.wiki/dev/fundamentals/logging
        Log = Logger;
        ActivationKey = Config.Bind("General", "ActivationKey", KeyCode.Z, "Key to press to drop all items");

        // BepInEx also gives us a config file for easy configuration.
        // See https://lethal.wiki/dev/intermediate/custom-configs

        // We can apply our hooks here.
        // See https://lethal.wiki/dev/fundamentals/patching-code

        // Log our awake here so we can see it in LogOutput.log file
        Log.LogInfo($"Plugin {Name} is loaded!");
    }

    private void Update()
    {
        // Check if activation key was pressed
        if (!InputHandler.IsActivationKeyPressed()) return;

        Plugin.Log.LogInfo("Drop all activation triggered");

        // Get all droppable items
        var itemsToDropList = InventoryAccessor.GetDroppableItems();

        if (itemsToDropList.Count == 0) return;

        // Drop each item
        int droppedCount = 0;
        PlayerControllerB? player = StartOfRound.Instance?.localPlayerController;

        if (player != null)
        {
            foreach (GrabbableObject item in itemsToDropList)
            {
                if (item != null && item.playerHeldBy == player)
                {
                    player.DiscardHeldObject();
                    droppedCount++;
                }
            }
        }

        Plugin.Log.LogInfo($"Dropped {droppedCount} items total");
    }
}
