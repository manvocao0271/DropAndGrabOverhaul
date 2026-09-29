using System;
using System.Collections;
using System.Reflection;
using BepInEx.Bootstrap;
using GameNetcodeStuff;

namespace DropAndGrabOverhaul.Compatibility;

// Optional integration with FlipMods' ReservedItemSlotCore (RIS), which appends "reserved"
// slots to player.ItemSlots after the main hotbar. Everything is looked up through reflection,
// so this mod needs RIS neither to build nor to run, and none of RIS's types are ever loaded
// when it isn't installed. RIS members used, both read from its decompiled source:
//   ReservedPlayerData.allPlayerData                 (public static Dictionary<PlayerControllerB, ReservedPlayerData>)
//   ReservedPlayerData.IsReservedItemSlot(int slot)  (public instance method)
// If either can't be found (e.g. a future RIS renames them) this logs once and reports "no
// reserved slots", which makes every slot count as main hotbar - i.e. behaviour without RIS.
//
// RefreshHudAfterReservedDrop below also calls HUDPatcher.UpdateUI() - unlike the two members
// above, HUDPatcher.cs itself was never seen, only its call sites in DropReservedItemPatcher.cs,
// so its namespace is unconfirmed. It's found by scanning the assembly for a type named
// "HUDPatcher" instead of a hardcoded namespace, to still work if that guess is wrong.
internal static class ReservedItemSlotCompat
{
    private const string PluginGuid = "FlipMods.ReservedItemSlotCore";
    private const string PlayerDataTypeName = "ReservedItemSlotCore.Data.ReservedPlayerData";
    private const string HudPatcherTypeName = "HUDPatcher";

    // Resolved once, on first use (by which point every plugin has loaded).
    private static bool resolved;
    private static bool disabled;
    private static FieldInfo? allPlayerDataField;
    private static MethodInfo? isReservedItemSlotMethod;
    private static MethodInfo? updateHudMethod;
    private static bool warnedHudUnavailable;

    private static bool? isLoaded;

    public static bool IsLoaded => isLoaded ??= Chainloader.PluginInfos.ContainsKey(PluginGuid);

    // True if slot is one of RIS's reserved item slots for this player. Uses RIS's own
    // definition (start index + number of unlocked slots) instead of assuming a 4-slot main
    // hotbar, so it stays correct when other mods change the hotbar size.
    public static bool IsReservedSlot(PlayerControllerB? player, int slot)
    {
        if (player == null || disabled || !IsLoaded)
            return false;

        EnsureResolved();
        if (allPlayerDataField == null || isReservedItemSlotMethod == null)
            return false;

        try
        {
            if (allPlayerDataField.GetValue(null) is not IDictionary allPlayerData || !allPlayerData.Contains(player))
                return false;

            object? playerData = allPlayerData[player];
            if (playerData == null)
                return false;

            return (bool)isReservedItemSlotMethod.Invoke(playerData, new object[] { slot })!;
        }
        catch (Exception e)
        {
            disabled = true;
            ModLog.Warning(
                $"ReservedItemSlotCore compat failed ({e.GetType().Name}: {e.Message}) - treating every slot as a main hotbar slot from now on.");
            return false;
        }
    }

    // Call once after dropping one or more reserved-slot items in the same sequence.
    // RIS's own HUD refresh (DropReservedItemPatcher.OnDiscardItem) only runs for the first
    // reserved item dropped in a back-to-back sequence - a private HashSet gate
    // (playersDiscardingItems) skips every call after that until its own delayed-switch
    // coroutine finishes, well after we're done. Left alone, reserved slots emptied after the
    // first one keep showing their frame (even with HideEmptyReservedItemSlots on) until
    // something else happens to trigger a refresh. Calling RIS's own refresh method ourselves
    // once, after the whole sequence, fixes that without touching its internal gating.
    public static void RefreshHudAfterReservedDrop()
    {
        if (disabled || !IsLoaded)
            return;

        EnsureResolved();
        if (updateHudMethod == null)
        {
            if (!warnedHudUnavailable)
            {
                warnedHudUnavailable = true;
                ModLog.Warning(
                    "ReservedItemSlotCore's HUD refresh method couldn't be resolved (version change?) - reserved slot frames may look stale after a force drop.");
            }

            return;
        }

        try
        {
            updateHudMethod.Invoke(null, null);
        }
        catch (Exception e)
        {
            ModLog.Warning(
                $"ReservedItemSlotCore HUD refresh failed ({e.GetType().Name}: {e.Message}) - reserved slot frames may look stale after a force drop until something else refreshes them.");
        }
    }

    // Looks up everything this class needs from RIS in one pass. The slot-query members and the
    // HUD-refresh method are independent: failing to find one doesn't disable the other.
    private static void EnsureResolved()
    {
        if (resolved)
            return;

        resolved = true;

        Assembly assembly;
        try
        {
            assembly = Chainloader.PluginInfos[PluginGuid].Instance!.GetType().Assembly;
        }
        catch (Exception e)
        {
            ModLog.Warning($"Failed to inspect ReservedItemSlotCore: {e.Message}");
            return;
        }

        try
        {
            Type? playerDataType = assembly.GetType(PlayerDataTypeName);
            allPlayerDataField = playerDataType?.GetField("allPlayerData", BindingFlags.Public | BindingFlags.Static);
            isReservedItemSlotMethod = playerDataType?.GetMethod(
                "IsReservedItemSlot", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(int) }, null);
        }
        catch (Exception e)
        {
            ModLog.Warning($"Failed to inspect ReservedItemSlotCore's slot API: {e.Message}");
        }

        if (allPlayerDataField != null && isReservedItemSlotMethod != null)
        {
            ModLog.Info("ReservedItemSlotCore detected - reserved item slots are dropped by holding the drop key longer.");
        }
        else
        {
            ModLog.Warning(
                "ReservedItemSlotCore is installed but its API couldn't be resolved (version change?) - treating every slot as a main hotbar slot.");
        }

        try
        {
            Type? hudPatcherType = Array.Find(assembly.GetTypes(), t => t.Name == HudPatcherTypeName);
            updateHudMethod = hudPatcherType?.GetMethod(
                "UpdateUI", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
        }
        catch (Exception e)
        {
            ModLog.Warning($"Failed to inspect ReservedItemSlotCore for its HUD refresh method: {e.Message}");
        }
    }
}
