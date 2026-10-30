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
    internal static class DilutionSystem
    {
        private const string HarmonyId = "xskills.cooking.dilution";
        private const string Prefix = "xskills_dilution_v1_";
        private const string FoodMarker = "|xSkillsDilution=";
        private const int Scale = 100;

        private sealed class PotState
        {
            internal int Recipe;
            internal long Units;
            internal uint Shells;
        }

        private sealed class PickupState
        {
            internal CookingTable Table;
            internal PlayerInventory Inventory;
            internal string FoodName;
            internal int Fraction;
            internal long Units;
        }

        private struct ConsumeState
        {
            internal float Portion;
            internal float Oxygen;
            internal PlayerStats PreviousStats;
            internal float PreviousPortion;
            internal bool Active;
        }

        private static readonly Dictionary<CookingTable, PotState> Ready =
            new Dictionary<CookingTable, PotState>();
        private static readonly Dictionary<CookingTable, int> Active =
            new Dictionary<CookingTable, int>();
        private static readonly Dictionary<Inventory, PickupState> PendingFood =
            new Dictionary<Inventory, PickupState>();
        private static readonly FieldInfo PickupItemField =
            AccessTools.Field(typeof(CookingTable), "pickupFoodItem");
        private static Harmony harmony;
        private static Guid trackedWorld;
        private static Item_Base lastConsumedFood;
        private static int lastConsumedFraction = Scale;
        private static int lastConsumeFrame = -1;
        [ThreadStatic]
        private static PlayerStats activeConsumeStats;
        [ThreadStatic]
        private static float activeConsumePortion;
        [ThreadStatic]
        private static int sortingDepth;

        internal static int CurrentBonusPercent
        {
            get
            {
                SkillData skill = SkillCatalog.Find("cooking");
                AbilityData perk = skill == null ? null : skill.FindAbility("dilution");
                if (perk == null || perk.PreviewTier <= 0 || skill.Level < perk.RequiredLevel ||
                    perk.ValuesPerTier < 3) return 0;
                int index = (Math.Min(perk.PreviewTier, perk.MaxTier) - 1) * perk.ValuesPerTier;
                if (index + 2 >= perk.Values.Length) return 0;
                int start = Math.Max(0, perk.Values[index]);
                int perLevel = Math.Max(0, perk.Values[index + 1]);
                int cap = Math.Max(0, perk.Values[index + 2]);
                return (int)Math.Min((long)cap, (long)start + (long)perLevel * Math.Max(0, skill.Level - 1));
            }
        }

        private static string WorldKey(CookingTable table, string prefix)
        {
            if (table == null || !SkillProgressStorage.IsWorldBound) return null;
            return Prefix + prefix + SaveAndLoad.WorldGuid.ToString("N") + "_" + table.ObjectIndex;
        }

        private static void SaveActive(CookingTable table, int bonus)
        {
            string key = WorldKey(table, "active_");
            if (key == null || table.CurrentRecipe == null) return;
            if (bonus <= 0) PlayerPrefs.DeleteKey(key);
            else PlayerPrefs.SetString(key, table.CurrentRecipe.RecipeIndex.ToString(CultureInfo.InvariantCulture) +
                "," + bonus.ToString(CultureInfo.InvariantCulture));
            PlayerPrefs.Save();
        }

        private static int ActiveBonus(CookingTable table)
        {
            int bonus;
            if (Active.TryGetValue(table, out bonus)) return bonus;
            string key = WorldKey(table, "active_");
            if (key == null || !PlayerPrefs.HasKey(key) || table.CurrentRecipe == null || table.Portions > 0U) return 0;
            string[] parts = PlayerPrefs.GetString(key, "").Split(',');
            int recipe;
            if (parts.Length != 2 || !int.TryParse(parts[0], out recipe) ||
                !int.TryParse(parts[1], out bonus) || bonus <= 0 || bonus > 10000 ||
                recipe != (int)table.CurrentRecipe.RecipeIndex) return 0;
            Active[table] = bonus;
            return bonus;
        }

        private static void ClearActive(CookingTable table)
        {
            Active.Remove(table);
            string key = WorldKey(table, "active_");
            if (key != null) { PlayerPrefs.DeleteKey(key); PlayerPrefs.Save(); }
        }

        private static uint CeilUnits(long value)
        {
            return (uint)Math.Min(uint.MaxValue, (value + Scale - 1L) / Scale);
        }

        private static void SaveReady(CookingTable table, PotState state)
        {
            string key = WorldKey(table, "ready_");
            if (key == null) return;
            if (state == null || state.Units <= 0)
                PlayerPrefs.DeleteKey(key);
            else
                PlayerPrefs.SetString(key, state.Recipe.ToString(CultureInfo.InvariantCulture) + "," +
                    state.Units.ToString(CultureInfo.InvariantCulture) + "," +
                    state.Shells.ToString(CultureInfo.InvariantCulture));
            PlayerPrefs.Save();
        }

        private static PotState GetReady(CookingTable table)
        {
            if (!(table is CookingTable_Pot) || table.CurrentRecipe == null || table.Portions == 0U) return null;
            PotState state;
            if (Ready.TryGetValue(table, out state))
            {
                if (state.Recipe == (int)table.CurrentRecipe.RecipeIndex && state.Shells == table.Portions)
                    return state;
                Ready.Remove(table);
            }
            string key = WorldKey(table, "ready_");
            if (key == null || !PlayerPrefs.HasKey(key)) return null;
            string[] parts = PlayerPrefs.GetString(key, "").Split(',');
            int recipe;
            long units;
            uint shells;
            if (parts.Length != 3 || !int.TryParse(parts[0], out recipe) ||
                !long.TryParse(parts[1], out units) || !uint.TryParse(parts[2], out shells) ||
                units <= 0 || units > (long)uint.MaxValue * Scale ||
                shells != table.Portions || CeilUnits(units) != shells ||
                recipe != (int)table.CurrentRecipe.RecipeIndex) return null;
            state = new PotState { Recipe = recipe, Units = units, Shells = shells };
            Ready[table] = state;
            return state;
        }

        internal static string PortionText(CookingTable table, uint fallback)
        {
            PotState ready = GetReady(table);
            return FormatUnits(ready == null ? (long)fallback * Scale : ready.Units);
        }

        internal static string FormatUnits(long units)
        {
            return (units / Scale).ToString(CultureInfo.InvariantCulture) +
                (units % Scale == 0 ? "" : "." + (units % Scale).ToString("D2", CultureInfo.InvariantCulture).TrimEnd('0'));
        }

        internal static string ExpectedText(CookingTable table, uint basePortions)
        {
            int bonus = ActiveBonus(table);
            return FormatUnits((long)basePortions * (Scale + bonus));
        }

        internal static void Initialize()
        {
            if (harmony != null) return;
            try
            {
                harmony = new Harmony(HarmonyId);
                Patch(typeof(CookingTable), "StartCooking", new[] { typeof(SO_CookingTable_Recipe) },
                    "BeforeStart", "AfterStart");
                Patch(typeof(CookingTable), "FinishCooking", Type.EmptyTypes, null, "AfterFinish");
                Patch(typeof(CookingTable), "PickupFood", new[] { typeof(Network_Player) },
                    "BeforePickup", "AfterPickup");
                Patch(typeof(CookingTable), "RestoreCookingPot", new[] { typeof(RGD_CookingPot) },
                    null, "AfterRestore");
                Patch(typeof(CookingTable), "OnDestroy", Type.EmptyTypes, null, "AfterDestroy");
                Patch(typeof(CookingTable), "HandlePickupFood", Type.EmptyTypes, null, "AfterPickupPrompt");
                Patch(typeof(Inventory), "AddItem", new[] { typeof(string), typeof(int) },
                    "BeforeAddItem", null);
                PatchSort(typeof(Inventory), "Sort");
                PatchSort(typeof(Inventory), "SortPlayerInventoryIntoStorage");
                Patch(typeof(ItemInstance), "get_HasMaxUses", Type.EmptyTypes,
                    null, "AfterHasMaxUses");
                PatchConsume();
                Patch(typeof(Stat_Bonus_Consumable), "Consume",
                    new[] { typeof(float), typeof(float), typeof(bool) },
                    "BeforeStatConsume", null);
                Patch(typeof(Slot), "IncrementUses", new[] { typeof(int), typeof(bool) },
                    "BeforeIncrementUses", null);
                Patch(typeof(PlayerInventory), "SetItemDescription", new[] { typeof(Item_Base) },
                    null, "AfterItemDescription");
            }
            catch (Exception ex)
            {
                Debug.LogError("[xSkills] Dilution: " + ex);
                if (harmony != null) harmony.UnpatchAll(HarmonyId);
                harmony = null;
            }
        }

        private static void Patch(Type type, string name, Type[] arguments, string prefix, string postfix)
        {
            MethodInfo original = AccessTools.Method(type, name, arguments);
            if (original == null) throw new MissingMethodException(type.FullName, name);
            HarmonyMethod before = prefix == null ? null : new HarmonyMethod(
                AccessTools.Method(typeof(DilutionSystem), prefix));
            HarmonyMethod after = postfix == null ? null : new HarmonyMethod(
                AccessTools.Method(typeof(DilutionSystem), postfix));
            if (after != null) after.priority = Priority.Last;
            harmony.Patch(original, before, after);
        }

        private static void PatchConsume()
        {
            MethodInfo original = AccessTools.Method(typeof(PlayerStats), "Consume", new[] { typeof(Item_Base) });
            if (original == null) throw new MissingMethodException(typeof(PlayerStats).FullName, "Consume");
            HarmonyMethod postfix = new HarmonyMethod(AccessTools.Method(typeof(DilutionSystem), "AfterConsume"));
            postfix.priority = Priority.Last;
            harmony.Patch(original,
                prefix: new HarmonyMethod(AccessTools.Method(typeof(DilutionSystem), "BeforeConsume")),
                postfix: postfix,
                finalizer: new HarmonyMethod(AccessTools.Method(typeof(DilutionSystem), "FinishConsume")));
        }

        private static void PatchSort(Type type, string methodName)
        {
            MethodInfo original = AccessTools.Method(type, methodName, Type.EmptyTypes);
            if (original == null) throw new MissingMethodException(type.FullName, methodName);
            harmony.Patch(original,
                prefix: new HarmonyMethod(AccessTools.Method(typeof(DilutionSystem), "BeforeSort")),
                finalizer: new HarmonyMethod(AccessTools.Method(typeof(DilutionSystem), "AfterSort")));
        }

        private static void BeforeSort()
        {
            sortingDepth++;
        }

        private static Exception AfterSort(Exception __exception)
        {
            if (sortingDepth > 0) sortingDepth--;
            return __exception;
        }

        private static void AfterHasMaxUses(ItemInstance __instance, ref bool __result)
        {
            if (!__result || sortingDepth == 0 || __instance == null ||
                !IsFilledDish(__instance.baseItem)) return;
            if (ReadFraction(__instance) < Scale) __result = false;
        }

        private static void BeforeStart(CookingTable __instance, ref int __state)
        {
            __state = __instance is CookingTable_Pot && Raft_Network.IsHost ? CurrentBonusPercent : 0;
        }

        private static void AfterStart(CookingTable __instance, bool __result, int __state)
        {
            if (!(__instance is CookingTable_Pot) || !Raft_Network.IsHost || !__result) return;
            Ready.Remove(__instance);
            SaveReady(__instance, null);
            Active.Remove(__instance);
            if (__state > 0)
            {
                Active[__instance] = __state;
                SaveActive(__instance, __state);
            }
            else ClearActive(__instance);
        }

        private static void AfterFinish(CookingTable __instance)
        {
            if (!(__instance is CookingTable_Pot) || !Raft_Network.IsHost || __instance.CurrentRecipe == null ||
                __instance.Portions == 0U) return;
            int bonus = ActiveBonus(__instance);
            if (bonus > 0)
            {
                ReusableDishesSystem.EnsureFoodIsSeparate(__instance.CurrentRecipe.Result);
                long units = (long)__instance.Portions * (Scale + bonus);
                PotState state = new PotState
                {
                    Recipe = (int)__instance.CurrentRecipe.RecipeIndex,
                    Units = units,
                    Shells = CeilUnits(units)
                };
                __instance.Portions = state.Shells;
                Ready[__instance] = state;
                SaveReady(__instance, state);
            }
            ClearActive(__instance);
        }

        private static void BeforePickup(CookingTable __instance, Network_Player player,
            ref PickupState __state)
        {
            __state = null;
            PotState state = GetReady(__instance);
            if (state == null || player == null || __instance.CurrentRecipe == null) return;
            __state = new PickupState
            {
                Table = __instance,
                Inventory = player.IsLocalPlayer ? player.Inventory : null,
                FoodName = __instance.CurrentRecipe.Result.UniqueName,
                Units = state.Units,
                Fraction = (int)Math.Min(Scale, state.Units)
            };
            if (__state.Inventory != null && __state.Fraction < Scale)
                PendingFood[__state.Inventory] = __state;
        }

        private static void AfterPickup(CookingTable __instance, Network_Player player,
            bool __result, PickupState __state)
        {
            if (__state == null) return;
            if (__state.Inventory != null)
                PendingFood.Remove(__state.Inventory);
            if (!__result) return;
            long remaining = Math.Max(0L, __state.Units - __state.Fraction);
            if (remaining == 0)
            {
                Ready.Remove(__instance);
                SaveReady(__instance, null);
            }
            else
            {
                PotState state = Ready.ContainsKey(__instance) ? Ready[__instance] : null;
                if (state == null) return;
                state.Units = remaining;
                state.Shells = CeilUnits(remaining);
                Ready[__instance] = state;
                SaveReady(__instance, state);
            }
        }

        private static bool BeforeAddItem(Inventory __instance, string uniqueItemName,
            int amount, ref int __result)
        {
            PickupState pending;
            if (__instance == null || amount != 1 ||
                !PendingFood.TryGetValue(__instance, out pending) ||
                pending.FoodName != uniqueItemName || pending.Fraction >= Scale)
                return true;
            Item_Base food = ItemManager.GetItemByName(uniqueItemName);
            if (food == null) return true;

            ReusableDishesSystem.EnsureFoodIsSeparate(food);
            ItemInstance portion = new ItemInstance(food, 1, food.MaxUses, "");
            WriteFraction(portion, pending.Fraction);
            ReusableDishesSystem.ApplyPendingPickupWear(__instance, portion);
            __instance.AddItem(portion, true);
            __result = 0;
            return false;
        }

        private static int ReadFraction(ItemInstance item)
        {
            if (item == null || string.IsNullOrEmpty(item.exclusiveString)) return Scale;
            string raw = item.exclusiveString;
            int begin = raw.IndexOf(FoodMarker, StringComparison.Ordinal);
            if (begin < 0) return Scale;
            begin += FoodMarker.Length;
            int end = raw.IndexOf(';', begin);
            if (end < 0) end = raw.Length;
            int value;
            return int.TryParse(raw.Substring(begin, end - begin), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out value) && value > 0 && value <= Scale ? value : Scale;
        }

        private static void WriteFraction(ItemInstance item, int fraction)
        {
            if (item == null) return;
            string raw = item.exclusiveString ?? string.Empty;
            int start = raw.IndexOf(FoodMarker, StringComparison.Ordinal);
            if (start >= 0)
            {
                int end = raw.IndexOf(';', start + FoodMarker.Length);
                raw = raw.Remove(start, end < 0 ? raw.Length - start : end + 1 - start);
            }
            item.exclusiveString = raw + FoodMarker + fraction.ToString(CultureInfo.InvariantCulture) + ";";
        }

        private static void BeforeIncrementUses(Slot __instance, int amountOfUsesToAdd)
        {
            if (amountOfUsesToAdd >= 0 || __instance == null || __instance.IsEmpty ||
                __instance.itemInstance == null || __instance.itemInstance.baseItem == null) return;
            int fraction = ReadFraction(__instance.itemInstance);
            if (fraction >= Scale) return;
            lastConsumedFood = __instance.itemInstance.baseItem;
            lastConsumedFraction = fraction;
            lastConsumeFrame = Time.frameCount;
        }

        private static void BeforeConsume(PlayerStats __instance, Item_Base edibleItem, ref ConsumeState __state)
        {
            __state = default(ConsumeState);
            Network_Player player = ComponentManager<Network_Player>.Value;
            if (player == null || !player.IsLocalPlayer || player.Stats != __instance ||
                player.Inventory == null || edibleItem == null) return;
            ItemInstance held = player.Inventory.GetSelectedHotbarItem();
            int fraction = held == null || held.baseItem == null ||
                held.baseItem.UniqueIndex != edibleItem.UniqueIndex ? Scale : ReadFraction(held);
            if (fraction >= Scale && lastConsumedFood == edibleItem && lastConsumeFrame == Time.frameCount)
                fraction = lastConsumedFraction;
            if (fraction >= Scale || __instance.stat_hunger == null || __instance.stat_thirst == null) return;
            __state.Active = true;
            __state.Portion = fraction / (float)Scale;
            __state.Oxygen = __instance.stat_oxygen == null ? 0f : __instance.stat_oxygen.Value;
            __state.PreviousStats = activeConsumeStats;
            __state.PreviousPortion = activeConsumePortion;
            activeConsumeStats = __instance;
            activeConsumePortion = __state.Portion;
            lastConsumedFood = edibleItem;
            lastConsumedFraction = fraction;
            lastConsumeFrame = Time.frameCount;
        }

        private static void BeforeStatConsume(object __instance, ref float __0, ref float __1)
        {
            PlayerStats stats = activeConsumeStats;
            if (stats == null || activeConsumePortion <= 0f || activeConsumePortion >= 1f ||
                (!ReferenceEquals(__instance, stats.stat_hunger) &&
                 !ReferenceEquals(__instance, stats.stat_thirst))) return;
            __0 *= activeConsumePortion;
            __1 *= activeConsumePortion;
        }

        private static void AfterConsume(PlayerStats __instance, ConsumeState __state)
        {
            if (!__state.Active) return;
            if (__instance.stat_oxygen != null && __instance.stat_oxygen.Value > __state.Oxygen)
                __instance.stat_oxygen.Value = Mathf.Min(__instance.stat_oxygen.Max,
                    __state.Oxygen + (__instance.stat_oxygen.Value - __state.Oxygen) * __state.Portion);
        }

        private static Exception FinishConsume(Exception __exception, ConsumeState __state)
        {
            if (__state.Active)
            {
                activeConsumeStats = __state.PreviousStats;
                activeConsumePortion = __state.PreviousPortion;
            }
            return __exception;
        }

        internal static float NutritionFraction(Item_Base food)
        {
            return food != null && lastConsumedFood == food && lastConsumeFrame == Time.frameCount
                ? lastConsumedFraction / (float)Scale : 1f;
        }

        private static void AfterRestore(CookingTable __instance)
        {
            if (!(__instance is CookingTable_Pot)) return;
            Ready.Remove(__instance);
            Active.Remove(__instance);
            if (__instance.CurrentRecipe != null && __instance.Portions > 0U) GetReady(__instance);
            else if (__instance.CurrentRecipe != null) ActiveBonus(__instance);
        }

        private static void AfterDestroy(CookingTable __instance)
        {
            Ready.Remove(__instance);
            Active.Remove(__instance);
        }

        private static bool IsFilledDish(Item_Base item)
        {
            if (item == null || item.settings_consumeable == null) return false;
            string name = item.UniqueName ?? string.Empty;
            if (name.IndexOf("Empty", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            Cost after = item.settings_consumeable.ItemAfterUse;
            if ((object)after != null && after.item != null)
            {
                string afterName = after.item.UniqueName ?? string.Empty;
                if (afterName.IndexOf("Empty", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    (afterName.IndexOf("Claybowl", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     afterName.IndexOf("ClayPlate", StringComparison.OrdinalIgnoreCase) >= 0))
                    return true;
            }
            return name.IndexOf("Claybowl", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("ClayPlate", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void AfterItemDescription(PlayerInventory __instance, Item_Base item)
        {
            if (__instance == null || __instance.itemDescriptionText == null ||
                !IsFilledDish(item)) return;
            Slot hovered = Inventory.hoverSlot;
            if (hovered == null || hovered.IsEmpty || hovered.itemInstance == null ||
                hovered.itemInstance.baseItem == null ||
                hovered.itemInstance.UniqueIndex != item.UniqueIndex) return;
            int fraction = ReadFraction(hovered.itemInstance);
            string label = Localizer.Code == "ru" ? "Порций: " : "Portions: ";
            string description = __instance.itemDescriptionText.text;
            __instance.itemDescriptionText.text = (string.IsNullOrEmpty(description) ? "" : description + "\n") +
                label + FormatUnits(fraction);
        }

        private static void AfterPickupPrompt(CookingTable __instance)
        {
            if (!(__instance is CookingTable_Pot) || __instance.CurrentRecipe == null ||
                __instance.displayText == null) return;
            PotState ready = GetReady(__instance);
            if (ready == null && (__instance.Portions > 0U || ActiveBonus(__instance) == 0)) return;
            string amount;
            if (ready != null) amount = FormatUnits(ready.Units);
            else
            {
                uint basePortions = CanteenCookSystem.PortionsBeingCookedForDilution(__instance);
                amount = ExpectedText(__instance, basePortions);
            }
            string label = (Localizer.Code == "ru" ? "Порций: " : "Portions: ") + amount;
            if (ready == null)
            {
                float duration = CanteenCookSystem.CookTimeForDilution(__instance);
                int percent = duration > 0f ? Mathf.Clamp(Mathf.FloorToInt(
                    100f * __instance.CookTimer / duration), 0, 99) : 0;
                __instance.displayText.ShowText((Localizer.Code == "ru" ? "Готовится: " : "Cooking: ") +
                    percent + "% | " + label, 0, true, 0);
                return;
            }
            Item_Base pickup = PickupItemField == null ? null : PickupItemField.GetValue(__instance) as Item_Base;
            if (pickup != null && __instance.localPlayer != null && __instance.localPlayer.Inventory != null)
            {
                ItemInstance held = __instance.localPlayer.Inventory.GetSelectedHotbarItem();
                if (held != null && held.Valid && held.UniqueIndex == pickup.UniqueIndex)
                {
                    PlayerInput input = PlayerInput.GetPlayerByIndex(0);
                    InputAction action = input == null ? null : input.actions["Interact"];
                    if (action != null)
                    {
                        __instance.displayText.ShowText("Pickup " +
                            __instance.CurrentRecipe.Result.settings_Inventory.DisplayName + " (" + label + ")",
                            action.GetBindingDisplayString((InputBinding.DisplayStringOptions)0, null),
                            MyInput.Keybinds["Interact"].MainKey, 0, 0, true);
                        return;
                    }
                }
            }
            __instance.displayText.ShowText(label, 0, true, 0);
        }

        internal static void Tick()
        {
            Guid world = SkillProgressStorage.IsWorldBound ? SaveAndLoad.WorldGuid : Guid.Empty;
            if (world == trackedWorld) return;
            trackedWorld = world;
            Active.Clear();
            Ready.Clear();
            PendingFood.Clear();
            lastConsumedFood = null;
            lastConsumeFrame = -1;
            activeConsumeStats = null;
            activeConsumePortion = 0f;
            sortingDepth = 0;
        }

        internal static void Dispose()
        {
            if (harmony != null) harmony.UnpatchAll(HarmonyId);
            harmony = null;
            Ready.Clear();
            Active.Clear();
            PendingFood.Clear();
            trackedWorld = Guid.Empty;
            lastConsumedFood = null;
            lastConsumeFrame = -1;
            activeConsumeStats = null;
            activeConsumePortion = 0f;
            sortingDepth = 0;
        }
    }
}
