using System;
using System.Collections;
using System.Reflection;
using BepInEx.Bootstrap;
using GameNetcodeStuff;

namespace DropAndGrabOverhaul.Compatibility
{
    // Optional integration with FlipMods' ReservedItemSlotCore (RIS), which appends "reserved"
    // slots to player.ItemSlots after the main hotbar. Everything is looked up through reflection,
    // so this mod needs RIS neither to build nor to run, and none of RIS's types are ever loaded
    // when it isn't installed. Only two RIS members are used:
    //   ReservedPlayerData.allPlayerData                 (public static Dictionary<PlayerControllerB, ReservedPlayerData>)
    //   ReservedPlayerData.IsReservedItemSlot(int slot)  (public instance method)
    // If either can't be found (e.g. a future RIS renames them) this logs once and reports "no
    // reserved slots", which makes every slot count as main hotbar - i.e. behaviour without RIS.
    internal static class ReservedItemSlotCompat
    {
        private const string PluginGuid = "FlipMods.ReservedItemSlotCore";
        private const string PlayerDataTypeName = "ReservedItemSlotCore.Data.ReservedPlayerData";

        private static bool resolveAttempted = false;
        private static bool disabled = false;
        private static FieldInfo? allPlayerDataField;
        private static MethodInfo? isReservedItemSlotMethod;

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
                Plugin.Log.LogWarning(
                    $"ReservedItemSlotCore compat failed ({e.GetType().Name}: {e.Message}) - treating every slot as a main hotbar slot from now on.");
                return false;
            }
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
                Plugin.Log.LogWarning($"Failed to inspect ReservedItemSlotCore: {e.Message}");
            }

            bool resolved = allPlayerDataField != null && isReservedItemSlotMethod != null;
            if (resolved)
            {
                Plugin.Log.LogInfo("ReservedItemSlotCore detected - reserved item slots are dropped by holding the drop key longer.");
            }
            else
            {
                Plugin.Log.LogWarning(
                    "ReservedItemSlotCore is installed but its API couldn't be resolved (version change?) - treating every slot as a main hotbar slot.");
            }

            return resolved;
        }
    }
}
