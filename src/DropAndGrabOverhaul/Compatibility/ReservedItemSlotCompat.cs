using System;
using System.Collections;
using System.Reflection;
using BepInEx.Bootstrap;
using GameNetcodeStuff;

namespace DropAndGrabOverhaul.Compatibility;

// Optional integration with ReservedItemSlotCore (RIS), which appends "reserved" slots to
// player.ItemSlots after the main hotbar. Reflection-only, so the mod needs RIS neither to build
// nor to run. If a member can't be resolved this logs once and treats every slot as a main
// hotbar slot (behaviour without RIS). See CLAUDE.md gotcha #10.
//
// Members used: ReservedPlayerData.allPlayerData (static Dictionary<PlayerControllerB, ReservedPlayerData>),
// ReservedPlayerData.IsReservedItemSlot(int), and HUDPatcher.UpdateUI() - the HUDPatcher type was
// never seen, so it is found by name rather than by namespace.
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

    // Uses RIS's own definition of a reserved slot rather than assuming a 4-slot main hotbar.
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

    // Call once after dropping reserved-slot items. RIS's own HUD refresh only runs for the first
    // reserved item of a back-to-back sequence, so later emptied slots would keep a stale frame.
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

    // The slot-query members and the HUD-refresh method are independent: failing to find one
    // doesn't disable the other.
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

        if (allPlayerDataField == null || isReservedItemSlotMethod == null)
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
