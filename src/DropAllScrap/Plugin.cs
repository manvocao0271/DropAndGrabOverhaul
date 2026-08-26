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
        GrabConfiguration.Initialize(Config);

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

    // Harmony patch to remove grab cooldown (item use cooldown)
    [HarmonyPatch(typeof(GrabbableObject), "RequireCooldown")]
    [HarmonyPrefix]
    private static bool RequireCooldownPrefix(GrabbableObject __instance, ref bool __result)
    {
        // If grab cooldown removal is enabled, skip the cooldown check
        if (GrabConfiguration.RemoveGrabCooldown)
        {
            __result = false; // Return false to allow grab without cooldown
            return false; // Skip the original method
        }

        return true; // Allow original method to run
    }

    // Harmony patch to remove interact trigger cooldown (hover cooldown for grabbables)
    [HarmonyPatch(typeof(InteractTrigger), "Interact")]
    [HarmonyPrefix]
    private static void InteractTriggerInteractPrefix(InteractTrigger __instance)
    {
        // If grab cooldown removal is enabled and this trigger is for a grabbable object,
        // set the cooldown to expired BEFORE the method runs so the cooldown check doesn't block it
        if (GrabConfiguration.RemoveGrabCooldown)
        {
            // Check if this InteractTrigger is part of a GrabbableObject
            GrabbableObject? grabbable = __instance.GetComponentInParent<GrabbableObject>();
            if (grabbable != null)
            {
                // Set cooldown to expired (negative value means it won't block next grab)
                // This runs BEFORE the method, so the early return check will fail and let the prompt show
                __instance.currentCooldownValue = -1f;
            }
        }
    }

    // Harmony postfix to keep the hint showing (reset cooldown immediately after it's set)
    [HarmonyPatch(typeof(InteractTrigger), "Interact")]
    [HarmonyPostfix]
    private static void InteractTriggerInteractPostfix(InteractTrigger __instance)
    {
        // If grab cooldown removal is enabled and this trigger is for a grabbable object,
        // reset the cooldown that was just set so the hint stays visible
        if (GrabConfiguration.RemoveGrabCooldown)
        {
            // Check if this InteractTrigger is part of a GrabbableObject
            GrabbableObject? grabbable = __instance.GetComponentInParent<GrabbableObject>();
            if (grabbable != null)
            {
                // Keep cooldown expired so hint stays visible and player can grab continuously
                __instance.currentCooldownValue = -1f;
            }
        }
    }

    // Harmony patch to remove cooldown set by StopSpecialAnimation (for grabbables with animations)
    [HarmonyPatch(typeof(InteractTrigger), "StopSpecialAnimation")]
    [HarmonyPostfix]
    private static void StopSpecialAnimationPostfix(InteractTrigger __instance)
    {
        // If grab cooldown removal is enabled and this trigger is for a grabbable object,
        // override the cooldown that was just set by StopSpecialAnimation
        if (GrabConfiguration.RemoveGrabCooldown)
        {
            // Check if this InteractTrigger is part of a GrabbableObject
            GrabbableObject? grabbable = __instance.GetComponentInParent<GrabbableObject>();
            if (grabbable != null)
            {
                // Set cooldown to expired (negative value means it won't block next grab)
                __instance.currentCooldownValue = -1f;
            }
        }
    }

    // Harmony patch to remove cooldown set by OnEnable (when new item enters hover range)
    [HarmonyPatch(typeof(InteractTrigger), "OnEnable")]
    [HarmonyPostfix]
    private static void OnEnablePostfix(InteractTrigger __instance)
    {
        // If grab cooldown removal is enabled and this trigger is for a grabbable object,
        // override the cooldown that was just set by OnEnable
        if (GrabConfiguration.RemoveGrabCooldown)
        {
            // Check if this InteractTrigger is part of a GrabbableObject
            GrabbableObject? grabbable = __instance.GetComponentInParent<GrabbableObject>();
            if (grabbable != null)
            {
                // Set cooldown to expired (negative value means it won't block next grab)
                __instance.currentCooldownValue = -1f;
            }
        }
    }

    // Harmony patch to clear the grab animation lock that blocks the hover tip and next grab
    [HarmonyPatch(typeof(PlayerControllerB), "BeginGrabObject")]
    [HarmonyPostfix]
    private static void BeginGrabObjectPostfix(PlayerControllerB __instance)
    {
        if (GrabConfiguration.RemoveGrabCooldown)
        {
            // isGrabbingObjectAnimation gates SetHoverTipAndCurrentInteractTrigger(); clearing it
            // immediately lets the hover tip and next grab become available right away
            __instance.isGrabbingObjectAnimation = false;
        }
    }

    // Harmony patch to re-assert the grab hint if the game blanked it (animation lock/linecast obstruction)
    [HarmonyPatch(typeof(PlayerControllerB), "SetHoverTipAndCurrentInteractTrigger")]
    [HarmonyPostfix]
    private static void SetHoverTipAndCurrentInteractTriggerPostfix(PlayerControllerB __instance)
    {
        if (!GrabConfiguration.RemoveGrabCooldown || !string.IsNullOrEmpty(__instance.cursorTip.text))
            return;

        Ray ray = new Ray(__instance.gameplayCamera.transform.position, __instance.gameplayCamera.transform.forward);
        if (Physics.Raycast(ray, out RaycastHit rayHit, __instance.grabDistance, __instance.interactableObjectsMask)
            && rayHit.collider.gameObject.layer != 8 && rayHit.collider.gameObject.layer != 30
            && rayHit.collider.CompareTag("PhysicsProp"))
        {
            GrabbableObject? grabbable = rayHit.collider.gameObject.GetComponent<GrabbableObject>();
            if (grabbable != null && !grabbable.isHeld && !grabbable.isPocketed)
            {
                __instance.cursorTip.text = "Grab : [E]";
                __instance.cursorIcon.enabled = true;
                __instance.cursorIcon.sprite = __instance.grabItemIcon;
            }
        }
    }
}
