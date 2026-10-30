using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using HMLLibrary;
using UnityEngine;
using UnityEngine.InputSystem;
using xSkills.Services;
using xSkills.Skills;

namespace xSkills.Systems
{
    internal static class CanteenCookSystem
    {
        private const string HarmonyId = "xskills.cooking.canteen-cook";
        private const string SettingPrefix = "xskills_canteencook_v1_";
        private const string Marker = "|xSkillsCanteen=";
        private const int DefaultRequiredLevel = 6;
        private const int FirstTierBatchLimit = 3;
        private const int DefaultBatchLimit = 6;

        private sealed class SlotStack
        {
            internal readonly List<ulong> Contributors = new List<ulong>();
            internal ItemInstance Item;
            internal string MarkerValue;
        }

        private sealed class ActiveBatch
        {
            internal int Multiplier;
            internal uint BasePortions;
            internal int RecipeIndex;
        }

        private static readonly Dictionary<CookingTable_Slot, SlotStack> Stacks =
            new Dictionary<CookingTable_Slot, SlotStack>();
        private static readonly Dictionary<CookingTable, ActiveBatch> Batches =
            new Dictionary<CookingTable, ActiveBatch>();
        private static readonly FieldInfo PickupItemField =
            AccessTools.Field(typeof(CookingTable), "pickupFoodItem");
        private static Harmony harmony;
        private static AbilityData registeredAbility;
        private static Guid trackedWorld;

        internal static int RequiredLevel
        {
            get { return Mathf.Clamp(PlayerPrefs.GetInt(SettingPrefix + "required", DefaultRequiredLevel), 1, 25); }
        }

        internal static int BatchLimit
        {
            get
            {
                if (registeredAbility != null && registeredAbility.Values.Length > 0)
                {
                    int index = registeredAbility.MaxTier >= 2 ? 1 : 0;
                    return Mathf.Clamp(registeredAbility.Values[index], 1, 10);
                }
                return Mathf.Clamp(PlayerPrefs.GetInt(SettingPrefix + "limit_v2", DefaultBatchLimit), 4, 10);
            }
        }

        private static int TierOneBatchLimit
        {
            get
            {
                return registeredAbility != null && registeredAbility.Values.Length > 0
                    ? Mathf.Clamp(registeredAbility.Values[0], 1, 10)
                    : FirstTierBatchLimit;
            }
        }

        internal static void RegisterAbility(SkillData skill)
        {
            if (skill == null) return;
            AbilityData ability = skill.FindAbility("canteencook");
            if (ability == null) return;
            registeredAbility = ability;
            ability.RequiredLevel = Mathf.Clamp(RequiredLevel, 1, skill.MaxLevel);
            ability.Values[0] = FirstTierBatchLimit;
            ability.Values[1] = Mathf.Clamp(PlayerPrefs.GetInt(SettingPrefix + "limit_v2", DefaultBatchLimit), 4, 10);
        }

        internal static void SetRequiredLevel(int level)
        {
            PlayerPrefs.SetInt(SettingPrefix + "required", Mathf.Clamp(level, 1, 25));
            PlayerPrefs.Save();
            RefreshAbility();
        }

        internal static void SetBatchLimit(int limit)
        {
            PlayerPrefs.SetInt(SettingPrefix + "limit_v2", Mathf.Clamp(limit, 4, 10));
            PlayerPrefs.Save();
            RefreshAbility();
        }

        internal static void RestoreDefaults()
        {
            PlayerPrefs.DeleteKey(SettingPrefix + "required");
            PlayerPrefs.DeleteKey(SettingPrefix + "limit");
            PlayerPrefs.DeleteKey(SettingPrefix + "limit_v2");
            PlayerPrefs.Save();
            RefreshAbility();
        }

        private static void RefreshAbility()
        {
            SkillData skill = SkillCatalog.Find("cooking");
            RegisterAbility(skill);
            if (skill != null) skill.RevalidateAbilityTiers();
            SkillCatalog.NotifyChanged();
        }

        private static int LocalMaximum()
        {
            SkillData skill = SkillCatalog.Find("cooking");
            AbilityData perk = skill == null ? null : skill.FindAbility("canteencook");
            if (perk == null || skill.Level < perk.RequiredLevel) return 1;
            if (perk.PreviewTier >= 2) return BatchLimit;
            if (perk.PreviewTier >= 1) return TierOneBatchLimit;
            return 1;
        }

        internal static void Initialize()
        {
            if (harmony != null) return;
            try
            {
                harmony = new Harmony(HarmonyId);
                Patch(typeof(CookingTable_Slot), "InsertItem", new[] { typeof(Network_Player), typeof(ItemInstance) },
                    "BeforeInsert", "AfterInsert");
                Patch(typeof(CookingTable_Slot), "RemoveItem", new[] { typeof(Network_Player) },
                    "BeforeRemove", "AfterRemove");
                Patch(typeof(CookingTable), "Deserialize", new[] { typeof(Message_NetworkBehaviour), typeof(Network_UserId) },
                    "BeforeDeserialize", null);
                Patch(typeof(CookingTable), "HandleStartCooking", Type.EmptyTypes,
                    "BeforeStartInteraction", null);
                Patch(typeof(CookingTable), "StartCooking", new[] { typeof(SO_CookingTable_Recipe) },
                    "BeforeStart", "AfterStart");
                Patch(typeof(CookingTable), "FinishCooking", Type.EmptyTypes, null, "AfterFinish");
                Patch(typeof(CookingTable), "RestoreCookingPot", new[] { typeof(RGD_CookingPot) },
                    null, "AfterRestore");
                Patch(typeof(CookingTable), "HandlePickupFood", Type.EmptyTypes, null, "AfterPickupPrompt");

                // В некоторых сборках обработчик наведения имеет другое имя
                MethodInfo onRay = AccessTools.Method(typeof(CookingTable_Slot), "OnIsRayed", Type.EmptyTypes);
                if (onRay != null)
                    harmony.Patch(onRay, new HarmonyMethod(AccessTools.Method(typeof(CanteenCookSystem), "BeforeSlotRay")));
                else
                    Debug.LogWarning("[xSkills] Canteen Cook: CookingTable_Slot.OnIsRayed was not found");
            }
            catch (Exception ex)
            {
                Debug.LogError("[xSkills] Canteen Cook: " + ex);
                if (harmony != null) harmony.UnpatchAll(HarmonyId);
                harmony = null;
            }
        }

        private static void Patch(Type owner, string name, Type[] args, string prefixName, string postfixName)
        {
            MethodInfo original = AccessTools.Method(owner, name, args);
            if (original == null) throw new MissingMethodException(owner.FullName, name);
            HarmonyMethod prefix = prefixName == null ? null :
                new HarmonyMethod(AccessTools.Method(typeof(CanteenCookSystem), prefixName));
            HarmonyMethod postfix = postfixName == null ? null :
                new HarmonyMethod(AccessTools.Method(typeof(CanteenCookSystem), postfixName));
            harmony.Patch(original, prefix, postfix);
        }

        internal static void Tick()
        {
            Guid world = SkillProgressStorage.IsWorldBound ? SaveAndLoad.WorldGuid : Guid.Empty;
            if (world == trackedWorld) return;
            trackedWorld = world;
            Stacks.Clear();
            Batches.Clear();
        }

        internal static void Dispose()
        {
            if (harmony != null) harmony.UnpatchAll(HarmonyId);
            harmony = null;
            Stacks.Clear();
            Batches.Clear();
            trackedWorld = Guid.Empty;
        }

        // Состояние слота сверяется с предметом и его меткой
        private static SlotStack ReadStack(CookingTable_Slot slot)
        {
            if (slot == null || !slot.HasItem || slot.CurrentItem == null) return null;
            ItemInstance item = slot.CurrentItem;
            string source = item.exclusiveString ?? string.Empty;
            int pos = source.IndexOf(Marker, StringComparison.Ordinal);
            string markerValue = string.Empty;
            if (pos >= 0)
            {
                int from = pos + Marker.Length;
                int to = source.IndexOf(';', from);
                if (to < 0) to = source.Length;
                markerValue = source.Substring(from, to - from);
            }

            SlotStack stack;
            if (Stacks.TryGetValue(slot, out stack) &&
                object.ReferenceEquals(stack.Item, item) &&
                string.Equals(stack.MarkerValue, markerValue, StringComparison.Ordinal))
                return stack;

            stack = new SlotStack { Item = item, MarkerValue = markerValue };
            if (markerValue.Length != 0)
            {
                foreach (string value in markerValue.Split(','))
                {
                    ulong owner;
                    if (ulong.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out owner))
                        stack.Contributors.Add(owner);
                    if (stack.Contributors.Count >= 10) break;
                }
            }
            if (stack.Contributors.Count == 0) stack.Contributors.Add(0UL);
            Stacks[slot] = stack;
            return stack;
        }

        private static void SaveStack(CookingTable_Slot slot, SlotStack stack)
        {
            if (slot == null || !slot.HasItem || slot.CurrentItem == null || stack == null) return;
            string raw = slot.CurrentItem.exclusiveString ?? string.Empty;
            int start = raw.IndexOf(Marker, StringComparison.Ordinal);
            if (start >= 0)
            {
                int end = raw.IndexOf(';', start + Marker.Length);
                raw = raw.Remove(start, end < 0 ? raw.Length - start : end + 1 - start);
            }
            string[] values = new string[stack.Contributors.Count];
            for (int i = 0; i < values.Length; i++)
                values[i] = stack.Contributors[i].ToString(CultureInfo.InvariantCulture);
            string markerValue = string.Join(",", values);
            slot.CurrentItem.exclusiveString = raw + Marker + markerValue + ";";
            stack.Item = slot.CurrentItem;
            stack.MarkerValue = markerValue;
        }

        private static void StripMarker(CookingTable_Slot slot)
        {
            if (slot == null || !slot.HasItem || slot.CurrentItem == null) return;
            string raw = slot.CurrentItem.exclusiveString ?? string.Empty;
            int start = raw.IndexOf(Marker, StringComparison.Ordinal);
            if (start < 0) return;
            int end = raw.IndexOf(';', start + Marker.Length);
            slot.CurrentItem.exclusiveString = raw.Remove(start,
                end < 0 ? raw.Length - start : end + 1 - start);
        }

        internal static int GetCount(CookingTable_Slot slot)
        {
            SlotStack stack = ReadStack(slot);
            return stack == null ? 0 : stack.Contributors.Count;
        }

        internal static ulong FirstContributor(CookingTable_Slot slot)
        {
            SlotStack stack = ReadStack(slot);
            return stack == null || stack.Contributors.Count == 0 ? 0UL : stack.Contributors[0];
        }

        internal static void AddAdditionalContributions(CookingTable_Slot slot,
            Dictionary<ulong, int> counts, ref int total)
        {
            SlotStack stack = ReadStack(slot);
            if (stack == null) return;
            for (int i = 1; i < stack.Contributors.Count; i++)
            {
                total++;
                ulong owner = stack.Contributors[i];
                if (owner == 0UL) continue;
                int current;
                counts.TryGetValue(owner, out current);
                counts[owner] = current + 1;
            }
        }

        private static bool IsCookingPot(CookingTable table)
        {
            return table is CookingTable_Pot;
        }

        private static bool BeforeSlotRay(CookingTable_Slot __instance)
        {
            if (__instance == null || !__instance.HasItem || LocalMaximum() <= 1) return true;
            CookingTable table = __instance.GetComponentInParent<CookingTable>();
            if (!IsCookingPot(table) || table.CurrentRecipe != null) return true;
            Network_Player player = ComponentManager<Network_Player>.Value;
            if (player == null || !player.IsLocalPlayer || player.Inventory == null) return true;
            ItemInstance held = player.Inventory.GetSelectedHotbarItem();
            if (held == null || !held.Valid || held.Amount <= 0 || held.baseItem == null ||
                __instance.CurrentItem == null ||
                __instance.CurrentItem.baseItem == null ||
                held.baseItem.UniqueIndex != __instance.CurrentItem.baseItem.UniqueIndex) return true;
            int count = GetCount(__instance);
            if (CanvasHelper.ActiveMenu != MenuType.None || PlayerItemManager.IsBusy ||
                !Helper.LocalPlayerIsWithinDistance(__instance.transform.position, Player.UseDistance)) return true;

            DisplayTextManager texts = ComponentManager<DisplayTextManager>.Value;
            if (texts != null)
                texts.ShowText(count >= LocalMaximum() ?
                    Localizer.F("canteencook-slot-full", count, LocalMaximum()) :
                    Localizer.F("canteencook-add-ingredient", count, LocalMaximum()), 0, true, 0);
            if (count >= LocalMaximum()) return false;
            PlayerInput input = PlayerInput.GetPlayerByIndex(0);
            InputAction action = input == null ? null : input.actions["Interact"];
            if (action != null && SimpleMonoBehaviourSingleton<CustomInputConfig>.Instance.WasPressedThisFrame(action, "Interact"))
            {
                Action<Network_Player, CookingTable_Slot, ItemInstance> handler = __instance.OnInsertItem;
                if (handler != null) handler(player, __instance, held);
            }
            return false;
        }

        private static bool BeforeInsert(CookingTable_Slot __instance, Network_Player player,
            ItemInstance itemInstance)
        {
            if (__instance == null || !IsCookingPot(__instance.GetComponentInParent<CookingTable>()) ||
                !__instance.HasItem || itemInstance == null ||
                itemInstance.baseItem == null || __instance.CurrentItem == null ||
                __instance.CurrentItem.baseItem == null ||
                __instance.CurrentItem.baseItem.UniqueIndex != itemInstance.baseItem.UniqueIndex)
                return true;

            SlotStack stack = ReadStack(__instance);
            int limit = player != null && player.IsLocalPlayer ? LocalMaximum() : BatchLimit;
            if (stack == null || stack.Contributors.Count >= limit) return false;

            if (player != null && player.IsLocalPlayer && player.Inventory != null)
            {
                ItemInstance held = player.Inventory.GetSelectedHotbarItem();
                if (held == null || !held.Valid || held.Amount <= 0 || held.baseItem == null ||
                    held.baseItem.UniqueIndex != itemInstance.baseItem.UniqueIndex) return false;
                player.Inventory.RemoveSelectedHotSlotItem(1);
            }
            if (player == null)
                stack.Contributors.Add(0UL);
            else
                stack.Contributors.Add(player.steamID);
            SaveStack(__instance, stack);
            return false;
        }

        private static void AfterInsert(CookingTable_Slot __instance, Network_Player player)
        {
            if (__instance == null || !IsCookingPot(__instance.GetComponentInParent<CookingTable>()) ||
                !__instance.HasItem) return;
            SlotStack stack = ReadStack(__instance);
            if (stack == null || stack.Contributors.Count != 1 || stack.Contributors[0] != 0UL) return;
            if (player == null)
                stack.Contributors[0] = 0UL;
            else
                stack.Contributors[0] = player.steamID;
            SaveStack(__instance, stack);
        }

        private static bool BeforeRemove(CookingTable_Slot __instance, Network_Player player)
        {
            if (__instance == null || !IsCookingPot(__instance.GetComponentInParent<CookingTable>()))
                return true;
            CookingTable table = __instance.GetComponentInParent<CookingTable>();
            if (table != null && table.CurrentRecipe != null)
            {
                StripMarker(__instance);
                Stacks.Remove(__instance);
                return true;
            }
            SlotStack stack = ReadStack(__instance);
            if (stack == null) return true;
            if (stack.Contributors.Count <= 1)
            {
                StripMarker(__instance);
                return true;
            }
            if (player != null && player.IsLocalPlayer && player.Inventory != null &&
                __instance.CurrentItem != null && __instance.CurrentItem.baseItem != null)
            {
                ItemInstance ingredient = __instance.CurrentItem;
                ItemInstance returned = ingredient.Clone();
                returned.Amount = 1;
                string raw = returned.exclusiveString ?? string.Empty;
                int at = raw.IndexOf(Marker, StringComparison.Ordinal);
                if (at >= 0)
                {
                    int end = raw.IndexOf(';', at + Marker.Length);
                    returned.exclusiveString = raw.Remove(at,
                        end < 0 ? raw.Length - at : end + 1 - at);
                }
                player.Inventory.AddItem(returned, true);
            }
            stack.Contributors.RemoveAt(stack.Contributors.Count - 1);
            SaveStack(__instance, stack);
            return false;
        }

        private static void AfterRemove(CookingTable_Slot __instance)
        {
            if (__instance != null && !__instance.HasItem) Stacks.Remove(__instance);
        }

        // Применяется и к сетевому пакету уже занятого слота
        private static bool BeforeDeserialize(CookingTable __instance, Message_NetworkBehaviour msg,
            ref bool __result)
        {
            if (!IsCookingPot(__instance) || msg == null) return true;
            if (msg.Type == Messages.CookingPot_Insert)
            {
                Message_CookingPot_Slot message = msg as Message_CookingPot_Slot;
                if (message == null || message.rgdItemInstance == null) return true;
                CookingTable_Slot slot = FindSlot(__instance, message.slotIndex);
                if (slot == null || !slot.HasItem || slot.CurrentItem == null) return true;
                ItemInstance item = message.rgdItemInstance.GetRealInstance();
                if (item == null || item.baseItem == null || slot.CurrentItem.baseItem == null ||
                    item.baseItem.UniqueIndex != slot.CurrentItem.baseItem.UniqueIndex) return true;
                Raft_Network network = ComponentManager<Raft_Network>.Value;
                Network_Player player = network == null ? null : network.GetPlayerFromID(message.SteamID);
                if (GetCount(slot) >= BatchLimit)
                {
                    __result = false;
                    return false;
                }
                BeforeInsert(slot, player, item);
                __result = Raft_Network.IsHost;
                return false;
            }
            if (msg.Type == Messages.CookingPot_Start)
            {
                int batches;
                if (!ValidCounts(__instance, out batches))
                {
                    __result = false;
                    return false;
                }
            }
            return true;
        }

        private static CookingTable_Slot FindSlot(CookingTable table, int index)
        {
            if (table == null || table.Slots == null) return null;
            foreach (CookingTable_Slot slot in table.Slots)
                if (slot != null && slot.Index == index) return slot;
            return null;
        }

        private static bool ValidCounts(CookingTable table, out int batchCount)
        {
            batchCount = 1;
            if (table == null || table.Slots == null) return true;
            int expected = 0;
            int occupied = 0;
            foreach (CookingTable_Slot slot in table.Slots)
            {
                if (slot == null) continue;
                int count = GetCount(slot);
                if (count == 0) continue;
                occupied++;
                if (expected == 0) expected = count;
                else if (expected != count) return false;
            }
            if (expected <= 1) return true;
            if (occupied != table.Slots.Length || expected > BatchLimit) return false;
            batchCount = expected;
            return true;
        }

        private static bool BeforeStartInteraction(CookingTable __instance)
        {
            if (!IsCookingPot(__instance)) return true;
            int batches;
            if (ValidCounts(__instance, out batches)) return true;
            DisplayTextManager manager = ComponentManager<DisplayTextManager>.Value;
            if (manager != null)
                manager.ShowText(Localizer.T("canteencook-invalid-ratio"), 0, true, 0);
            return false;
        }

        private static void BeforeStart(CookingTable __instance, ref bool __state)
        {
            int batches;
            __state = ValidCounts(__instance, out batches);
            if (!__state) return;
            if (__instance == null || !(__instance is CookingTable_Pot)) return;
            ActiveBatch batch = new ActiveBatch { Multiplier = batches };
            Batches[__instance] = batch;
        }

        private static void AfterStart(CookingTable __instance, SO_CookingTable_Recipe recipe,
            bool __result, bool __state)
        {
            if (__instance == null) return;
            if (!__result || !__state || recipe == null)
            {
                Batches.Remove(__instance);
                return;
            }
            ActiveBatch batch;
            if (!Batches.TryGetValue(__instance, out batch)) return;
            batch.RecipeIndex = (int)recipe.RecipeIndex;
            batch.BasePortions = recipe.Portions;
            SaveActive(__instance, batch);
        }

        private static void AfterFinish(CookingTable __instance)
        {
            if (!IsCookingPot(__instance) || __instance.Portions == 0U) return;
            ActiveBatch batch = ActiveFor(__instance);
            if (batch != null && batch.Multiplier > 1 && __instance.CurrentRecipe != null &&
                batch.RecipeIndex == (int)__instance.CurrentRecipe.RecipeIndex)
            {
                uint additional = batch.BasePortions * (uint)(batch.Multiplier - 1);
                __instance.Portions = (uint)Math.Min((long)uint.MaxValue, (long)__instance.Portions + additional);
            }
            Batches.Remove(__instance);
            ClearActive(__instance);
            if (__instance.Slots != null)
                foreach (CookingTable_Slot slot in __instance.Slots)
                    if (slot != null) Stacks.Remove(slot);
        }

        // Сохранение активного множителя отдельно от количества уже готовых порций
        private static string ActiveKey(CookingTable table)
        {
            if (table == null || !SkillProgressStorage.IsWorldBound) return null;
            return SettingPrefix + "active_" + SaveAndLoad.WorldGuid.ToString("N") + "_" + table.ObjectIndex;
        }

        private static void SaveActive(CookingTable table, ActiveBatch batch)
        {
            string key = ActiveKey(table);
            if (key == null || batch == null || batch.Multiplier <= 1) return;
            PlayerPrefs.SetString(key, batch.RecipeIndex.ToString(CultureInfo.InvariantCulture) + "," +
                batch.Multiplier.ToString(CultureInfo.InvariantCulture) + "," +
                batch.BasePortions.ToString(CultureInfo.InvariantCulture));
            PlayerPrefs.Save();
        }

        private static ActiveBatch ActiveFor(CookingTable table)
        {
            ActiveBatch batch;
            if (Batches.TryGetValue(table, out batch)) return batch;
            if (table == null || table.CurrentRecipe == null || table.Portions != 0U) return null;
            string key = ActiveKey(table);
            if (key == null || !PlayerPrefs.HasKey(key)) return null;
            string[] parts = PlayerPrefs.GetString(key, "").Split(',');
            int recipe, multiplier;
            uint basePortions;
            if (parts.Length != 3 || !int.TryParse(parts[0], out recipe) ||
                !int.TryParse(parts[1], out multiplier) || !uint.TryParse(parts[2], out basePortions) ||
                recipe != (int)table.CurrentRecipe.RecipeIndex || multiplier <= 1 || multiplier > 10) return null;
            batch = new ActiveBatch { RecipeIndex = recipe, Multiplier = multiplier, BasePortions = basePortions };
            Batches[table] = batch;
            return batch;
        }

        private static void ClearActive(CookingTable table)
        {
            string key = ActiveKey(table);
            if (key == null) return;
            PlayerPrefs.DeleteKey(key);
            PlayerPrefs.Save();
        }

        private static void AfterRestore(CookingTable __instance)
        {
            if (!IsCookingPot(__instance)) return;
            if (__instance.Slots != null)
                foreach (CookingTable_Slot slot in __instance.Slots)
                    if (slot != null) { Stacks.Remove(slot); ReadStack(slot); }
            if (__instance.CurrentRecipe != null && __instance.Portions == 0U)
                ActiveFor(__instance);
            else
                Batches.Remove(__instance);
        }

        internal static uint ExpectedPortions(CookingTable table)
        {
            if (table == null) return 0U;
            ActiveBatch batch = ActiveFor(table);
            if (batch == null || table.CurrentRecipe == null || batch.Multiplier <= 1 ||
                batch.RecipeIndex != (int)table.CurrentRecipe.RecipeIndex) return table.Portions;
            return (uint)Math.Min((long)uint.MaxValue,
                (long)table.Portions + (long)batch.BasePortions * (batch.Multiplier - 1));
        }

        private static void AfterPickupPrompt(CookingTable __instance)
        {
            if (!IsCookingPot(__instance) || __instance.CurrentRecipe == null || __instance.Portions == 0U ||
                __instance.displayText == null || __instance.localPlayer == null ||
                __instance.localPlayer.Inventory == null || PickupItemField == null) return;
            Item_Base pickup = PickupItemField.GetValue(__instance) as Item_Base;
            ItemInstance held = __instance.localPlayer.Inventory.GetSelectedHotbarItem();
            if (pickup == null || held == null || !held.Valid || held.UniqueIndex != pickup.UniqueIndex) return;
            __instance.displayText.ShowText(Localizer.F("canteencook-ready-portions", __instance.Portions),
                0, true, 0);
        }
    }
}
