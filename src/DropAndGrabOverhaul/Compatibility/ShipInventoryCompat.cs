using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using BepInEx.Bootstrap;
using GameNetcodeStuff;
using UnityEngine;

namespace DropAndGrabOverhaul.Compatibility;

internal static class ShipInventoryCompat
{
    private const string PluginGuid = "org.lethalcompanymodding.shipinventoryupdated";

    public static bool IsLoaded => Chainloader.PluginInfos.ContainsKey(PluginGuid);

    public static bool IsHoveringChute(PlayerControllerB player)
    {
        return player.hoveringOverTrigger is ShipInventoryUpdated.Scripts.ChuteTrigger;
    }

    public static bool IsChuteTriggerInstance(InteractTrigger trigger)
    {
        return trigger is ShipInventoryUpdated.Scripts.ChuteTrigger;
    }

    public static object? AcquireHoveredChute(PlayerControllerB player)
    {
        return player.hoveringOverTrigger as ShipInventoryUpdated.Scripts.ChuteTrigger;
    }

    public static bool IsChuteHandleValid(object? chuteHandle)
    {
        return chuteHandle is ShipInventoryUpdated.Scripts.ChuteTrigger chuteTrigger && chuteTrigger != null;
    }

    public static bool IsChuteOnCooldown(object? chuteHandle)
    {
        return chuteHandle is ShipInventoryUpdated.Scripts.ChuteTrigger chuteTrigger
            && chuteTrigger != null
            && chuteTrigger.currentCooldownValue > 0f;
    }

    public static List<(int Slot, GrabbableObject Item)> GetStorableItems(PlayerControllerB player)
    {
        string? blacklist = GetChuteBlacklistRaw();
        var result = new List<(int, GrabbableObject)>();

        for (int i = 0; i < player.ItemSlots.Length; i++)
        {
            GrabbableObject item = player.ItemSlots[i];
            if (item == null)
                continue;

            if (IsChuteBlacklisted(item.itemProperties.itemName, blacklist))
                continue;

            result.Add((i, item));
        }

        return result;
    }

    public enum ChuteStoreAttempt
    {
        NotAllowed,

        Called,

        Threw
    }

    public static ChuteStoreAttempt AttemptStore(object chuteHandle, PlayerControllerB player, out Exception? exception)
    {
        exception = null;

        if (chuteHandle is not ShipInventoryUpdated.Scripts.ChuteTrigger chuteTrigger || chuteTrigger == null)
            return ChuteStoreAttempt.NotAllowed;

        if (!chuteTrigger.interactable)
            return ChuteStoreAttempt.NotAllowed;

        try
        {
            chuteTrigger.Interact(player.transform);
            return ChuteStoreAttempt.Called;
        }
        catch (Exception e)
        {
            exception = e;
            return ChuteStoreAttempt.Threw;
        }
    }

    private static bool IsChuteBlacklisted(string itemName, string? blacklistRaw)
    {
        if (string.IsNullOrEmpty(blacklistRaw))
            return false;

        string name = itemName.ToLowerInvariant();
        foreach (string entry in blacklistRaw.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            string pattern = "^" + entry.Trim().ToLowerInvariant() + "$";
            try
            {
                if (Regex.IsMatch(name, pattern))
                    return true;
            }
            catch (ArgumentException)
            {
            }
        }

        return false;
    }

    private static string? GetChuteBlacklistRaw()
    {
        try
        {
            Type? configType = Type.GetType("ShipInventoryUpdated.Configurations.Configuration, ShipInventoryUpdated");
            object? instance = configType?.GetField("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            object? chute = instance != null
                ? configType!.GetField("Chute", BindingFlags.Public | BindingFlags.Instance)?.GetValue(instance)
                : null;
            object? blacklistEntry = chute?.GetType().GetField("Blacklist", BindingFlags.Public | BindingFlags.Instance)?.GetValue(chute);
            return blacklistEntry?.GetType().GetProperty("Value")?.GetValue(blacklistEntry) as string;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Could not read ShipInventoryUpdated's chute blacklist config, treating it as empty: {e.Message}");
            return null;
        }
    }
}