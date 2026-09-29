using System;
using System.Collections;
using System.Reflection;
using BepInEx.Bootstrap;
using DropAndGrabOverhaul;
using GameNetcodeStuff;

namespace DropAndGrabOverhaul.Compatibility
{
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

        private static bool resolveAttempted = false;
        private static bool disabled = false;
        private static FieldInfo? allPlayerDataField;
        private static MethodInfo? isReservedItemSlotMethod;

        private static bool hudResolveAttempted = false;
        private static MethodInfo? updateHudMethod;

        public static bool IsLoaded => Chainloader.PluginInfos.ContainsKey(PluginGuid);

        // True if slot is one of RIS's reserved item slots for this player. Uses RIS's own
        // definition (start index + number of unlocked slots) instead of assuming a 4-slot main
        // hotbar, so it stays correct when other mods change the hotbar size.
        public static bool IsReservedSlot(PlayerControllerB? player, int slot)
        {
            if (player == null || disabled || !IsLoaded || !TryResolve())
                return false;

            try
            {
                if (allPlayerDataField!.GetValue(null) is not IDictionary allPlayerData || !allPlayerData.Contains(player))
                    return false;

                object? playerData = allPlayerData[player];
                if (playerData == null)
                    return false;

                return (bool)isReservedItemSlotMethod!.Invoke(playerData, new object[] { slot })!;
            }
            catch (Exception e)
            {
                disabled = true;
                Logging.Warning(
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
            if (disabled || !IsLoaded || !TryResolveHud())
                return;

            try
            {
                updateHudMethod!.Invoke(null, null);
            }
            catch (Exception e)
            {
                Logging.Warning(
                    $"ReservedItemSlotCore HUD refresh failed ({e.GetType().Name}: {e.Message}) - reserved slot frames may look stale after a force drop until something else refreshes them.");
            }
        }

        private static bool TryResolveHud()
        {
            if (hudResolveAttempted)
                return updateHudMethod != null;

            hudResolveAttempted = true;

            try
            {
                Assembly assembly = Chainloader.PluginInfos[PluginGuid].Instance!.GetType().Assembly;
                Type? hudPatcherType = Array.Find(assembly.GetTypes(), t => t.Name == HudPatcherTypeName);
                updateHudMethod = hudPatcherType?.GetMethod(
                    "UpdateUI", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            }
            catch (Exception e)
            {
                Logging.Warning($"Failed to inspect ReservedItemSlotCore for its HUD refresh method: {e.Message}");
            }

            if (updateHudMethod == null)
            {
                Logging.Warning(
                    "ReservedItemSlotCore's HUD refresh method couldn't be resolved (version change?) - reserved slot frames may look stale after a force drop.");
            }

            return updateHudMethod != null;
        }

        private static bool TryResolve()
        {
            if (resolveAttempted)
                return allPlayerDataField != null && isReservedItemSlotMethod != null;

            resolveAttempted = true;

            try
            {
                Type? playerDataType = Chainloader.PluginInfos[PluginGuid].Instance?.GetType().Assembly.GetType(PlayerDataTypeName);
                allPlayerDataField = playerDataType?.GetField("allPlayerData", BindingFlags.Public | BindingFlags.Static);
                isReservedItemSlotMethod = playerDataType?.GetMethod(
                    "IsReservedItemSlot", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(int) }, null);
            }
            catch (Exception e)
            {
                Logging.Warning($"Failed to inspect ReservedItemSlotCore: {e.Message}");
            }

            bool resolved = allPlayerDataField != null && isReservedItemSlotMethod != null;
            if (resolved)
            {
                Logging.Info("ReservedItemSlotCore detected - reserved item slots are dropped by holding the drop key longer.");
            }
            else
            {
                Logging.Warning(
                    "ReservedItemSlotCore is installed but its API couldn't be resolved (version change?) - treating every slot as a main hotbar slot.");
            }

            return resolved;
        }
    }
}
