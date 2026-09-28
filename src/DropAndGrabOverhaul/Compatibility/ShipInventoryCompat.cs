// using System;
// using System.Collections.Generic;
// using System.Reflection;
// using System.Text.RegularExpressions;
// using BepInEx.Bootstrap;
// using DropAndGrabOverhaul.Inventory;
// using GameNetcodeStuff;

// namespace DropAndGrabOverhaul.Compatibility;

// internal static class ShipInventoryCompat
// {
//     private const string PluginGuid = "org.lethalcompanymodding.shipinventoryupdated";

//     public static bool IsLoaded => Chainloader.PluginInfos.ContainsKey(PluginGuid);

//     public static bool IsHoveringChute(PlayerControllerB player)
//     {
//         return player.hoveringOverTrigger is ShipInventoryUpdated.Scripts.ChuteTrigger;
//     }

//     public static bool IsChuteTriggerInstance(InteractTrigger trigger)
//     {
//         return trigger is ShipInventoryUpdated.Scripts.ChuteTrigger;
//     }

//     public static object? AcquireHoveredChute(PlayerControllerB player)
//     {
//         return player.hoveringOverTrigger as ShipInventoryUpdated.Scripts.ChuteTrigger;
//     }

//     public static bool IsChuteHandleValid(object? chuteHandle)
//     {
//         return chuteHandle is ShipInventoryUpdated.Scripts.ChuteTrigger chuteTrigger && chuteTrigger != null;
//     }

//     public static bool IsChuteOnCooldown(object? chuteHandle)
//     {
//         // InteractTrigger.Interact() itself only proceeds once currentCooldownValue is
//         // strictly negative (its own gate is `if (currentCooldownValue >= 0f) return;`),
//         // so mirror that exact boundary here instead of treating exactly 0 as "ready" -
//         // otherwise there's a razor-thin window where this reports "not on cooldown"
//         // one frame before Interact() would still silently decline.
//         return chuteHandle is ShipInventoryUpdated.Scripts.ChuteTrigger chuteTrigger
//             && chuteTrigger != null
//             && chuteTrigger.currentCooldownValue >= 0f;
//     }

//     public static List<(int Slot, GrabbableObject Item)> GetStorableItems(PlayerControllerB player)
//     {
//         string? blacklist = GetChuteBlacklistRaw();
//         var result = new List<(int, GrabbableObject)>();

//         foreach ((int slot, GrabbableObject item) in InventoryAccessor.GetItemSlots(player))
//         {
//             if (IsChuteBlacklisted(item.itemProperties.itemName, blacklist))
//                 continue;

//             result.Add((slot, item));
//         }

//         return result;
//     }

//     public enum ChuteStoreAttempt
//     {
//         NotAllowed,

//         Called,

//         Threw
//     }

//     public static ChuteStoreAttempt AttemptStore(
//         object chuteHandle,
//         PlayerControllerB player,
//         GrabbableObject expectedItem,
//         out Exception? exception)
//     {
//         exception = null;

//         if (expectedItem == null)
//             return ChuteStoreAttempt.NotAllowed;

//         if (chuteHandle is not ShipInventoryUpdated.Scripts.ChuteTrigger chuteTrigger
//             || chuteTrigger == null)
//             return ChuteStoreAttempt.NotAllowed;

//         if (!chuteTrigger.interactable)
//             return ChuteStoreAttempt.NotAllowed;

//         if (player.currentlyHeldObjectServer != expectedItem)
//             return ChuteStoreAttempt.NotAllowed;

//         try
//         {
//             chuteTrigger.Interact(player.transform);
//             return ChuteStoreAttempt.Called;
//         }
//         catch (Exception e)
//         {
//             exception = e;
//             return ChuteStoreAttempt.Threw;
//         }
//     }

//     private static bool IsChuteBlacklisted(string itemName, string? blacklistRaw)
//     {
//         if (string.IsNullOrEmpty(blacklistRaw))
//             return false;

//         string name = itemName.ToLowerInvariant();
//         foreach (string entry in blacklistRaw.Split(',', StringSplitOptions.RemoveEmptyEntries))
//         {
//             string pattern = "^" + entry.Trim().ToLowerInvariant() + "$";
//             try
//             {
//                 if (Regex.IsMatch(name, pattern))
//                     return true;
//             }
//             catch (ArgumentException)
//             {
//             }
//         }

//         return false;
//     }

//     // Resolved lazily and cached below - GetStorableItems calls into this once per
//     // storable-item scan (i.e. up to a few times a second while auto-storing), and
//     // the reflected members themselves never change shape at runtime, only their
//     // values do.
//     private static FieldInfo? cachedInstanceField;
//     private static FieldInfo? cachedChuteField;
//     private static FieldInfo? cachedBlacklistField;
//     private static PropertyInfo? cachedValueProperty;
//     private static bool triedToResolveInstanceField;

//     private static string? GetChuteBlacklistRaw()
//     {
//         try
//         {
//             if (!triedToResolveInstanceField)
//             {
//                 triedToResolveInstanceField = true;
//                 Type? configType = Type.GetType("ShipInventoryUpdated.Configurations.Configuration, ShipInventoryUpdated");
//                 cachedInstanceField = configType?.GetField("Instance", BindingFlags.Public | BindingFlags.Static);
//             }

//             object? instance = cachedInstanceField?.GetValue(null);
//             if (instance == null)
//                 return null;

//             cachedChuteField ??= instance.GetType().GetField("Chute", BindingFlags.Public | BindingFlags.Instance);
//             object? chute = cachedChuteField?.GetValue(instance);
//             if (chute == null)
//                 return null;

//             cachedBlacklistField ??= chute.GetType().GetField("Blacklist", BindingFlags.Public | BindingFlags.Instance);
//             object? blacklistEntry = cachedBlacklistField?.GetValue(chute);
//             if (blacklistEntry == null)
//                 return null;

//             cachedValueProperty ??= blacklistEntry.GetType().GetProperty("Value");
//             return cachedValueProperty?.GetValue(blacklistEntry) as string;
//         }
//         catch (Exception e)
//         {
//             Plugin.Log.LogWarning($"Could not read ShipInventoryUpdated's chute blacklist config, treating it as empty: {e.Message}");
//             return null;
//         }
//     }
// }