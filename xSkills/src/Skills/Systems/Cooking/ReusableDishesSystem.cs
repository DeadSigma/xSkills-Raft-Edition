using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using xSkills.Skills;
using xSkills.Services;

namespace xSkills.Systems
{
    internal static class ReusableDishesSystem
    {
        private const string HarmonyId = "xskills.cooking.reusable-dishes";
        private const string PrefPrefix = "xskills_cooking_bowls_v1_";
        private const string WearMarker = "|xSkillsBowlWear=";
        private const int DefaultDurability = 5;
        private const int DefaultMultiplier = 2;
        private const int DefaultRequiredLevel = 4;

        private sealed class ItemDefaults
        {
            internal int MaxUses;
            internal int StackSize;
            internal bool Empty;
        }

        private sealed class PickupState
        {
            internal string FoodName;
            internal int Wear;
            internal bool Applied;
        }

        private struct ConsumeState
        {
            internal Item_Base EmptyBowl;
            internal int Wear;
            internal bool Active;
        }

        private static readonly Dictionary<Item_Base, ItemDefaults> ModifiedItems =
            new Dictionary<Item_Base, ItemDefaults>();
        private static readonly Dictionary<Inventory, PickupState> Pickups =
            new Dictionary<Inventory, PickupState>();
        private static readonly FieldInfo MaxUsesField = AccessTools.Field(typeof(Item_Base), "maxUses");
        private static readonly FieldInfo StackSizeField = AccessTools.Field(typeof(ItemInstance_Inventory), "stackSize");
        private static readonly FieldInfo PickupBowlField = AccessTools.Field(typeof(CookingTable), "pickupFoodItem");
        private static Harmony harmony;
        private static bool betterBowlPatched;
        private static volatile bool betterBowlRescanRequested;
        private static float nextRefresh;
        private static bool refreshInitialized;
        private static AbilityData registeredAbility;
        private static Guid refreshedWorld;
        private static Network_Player refreshedPlayer;
        private static int refreshedDurability;

        internal static int BaseDurability { get { return Mathf.Clamp(PlayerPrefs.GetInt(PrefPrefix + "durability", DefaultDurability), 1, 100); } }
        internal static int FirstTierMultiplier
        {
            get
            {
                if (registeredAbility != null && registeredAbility.Values.Length > 0)
                    return Mathf.Clamp(registeredAbility.Values[0], 1, 10);
                return Mathf.Clamp(PlayerPrefs.GetInt(PrefPrefix + "multiplier", DefaultMultiplier), 1, 10);
            }
        }
        internal static int RequiredLevel { get { return Mathf.Clamp(PlayerPrefs.GetInt(PrefPrefix + "required", DefaultRequiredLevel), 1, 25); } }

        internal static void RegisterAbility(SkillData skill)
        {
            if (skill == null) return;
            AbilityData ability = skill.FindAbility("durablebowl");
            if (ability == null) return;
            registeredAbility = ability;
            ability.RequiredLevel = Mathf.Clamp(RequiredLevel, 1, skill.MaxLevel);
            ability.Values[0] = Mathf.Clamp(PlayerPrefs.GetInt(PrefPrefix + "multiplier", DefaultMultiplier), 1, 10);
            ability.Values[1] = 0;
        }

        internal static void SetRequiredLevel(int value)
        {
            PlayerPrefs.SetInt(PrefPrefix + "required", Mathf.Clamp(value, 1, 25));
            PlayerPrefs.Save();
            SkillData skill = SkillCatalog.Find("cooking");
            RegisterAbility(skill);
            if (skill != null) skill.RevalidateAbilityTiers();
            SkillCatalog.NotifyChanged();
        }

        internal static void SetDurability(int value)
        {
            PlayerPrefs.SetInt(PrefPrefix + "durability", Mathf.Clamp(value, 1, 100));
            PlayerPrefs.Save();
            RefreshItems();
        }

        internal static void SetMultiplier(int value)
        {
            PlayerPrefs.SetInt(PrefPrefix + "multiplier", Mathf.Clamp(value, 1, 10));
            PlayerPrefs.Save();
            RegisterAbility(SkillCatalog.Find("cooking"));
            SkillCatalog.NotifyChanged();
            RefreshItems();
        }

        internal static void RestoreDefaults()
        {
            PlayerPrefs.DeleteKey(PrefPrefix + "required");
            PlayerPrefs.DeleteKey(PrefPrefix + "durability");
            PlayerPrefs.DeleteKey(PrefPrefix + "multiplier");
            PlayerPrefs.Save();
            RegisterAbility(SkillCatalog.Find("cooking"));
            SkillData skill = SkillCatalog.Find("cooking");
            if (skill != null) skill.RevalidateAbilityTiers();
            SkillCatalog.NotifyChanged();
            RefreshItems();
        }

        internal static void Initialize()
        {
            if (harmony != null) return;
            if (MaxUsesField == null || StackSizeField == null || PickupBowlField == null)
            {
                Debug.LogError("[xSkills] Reusable dishes: required Raft fields not found");
                return;
            }
            try
            {
                harmony = new Harmony(HarmonyId);
                Patch(typeof(Slot), "IncrementUses", new[] { typeof(int), typeof(bool) }, "BeforeIncrementUses", "AfterIncrementUses");
                Patch(typeof(Slot), "SetItem", new[] { typeof(ItemInstance) }, null, "AfterSetItem");
                Patch(typeof(Slot), "SetItem", new[] { typeof(Item_Base), typeof(int) }, null, "AfterSetItem");
                Patch(typeof(CookingTable), "PickupFood", new[] { typeof(Network_Player) }, "BeforePickupFood", "AfterPickupFood");
                Patch(typeof(Inventory), "AddItem", new[] { typeof(string), typeof(int) }, "BeforeAddFood", "AfterAddFood");
                AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
                betterBowlRescanRequested = true;
                refreshInitialized = false;
                nextRefresh = 0f;
            }
            catch (Exception ex)
            {
                Debug.LogError("[xSkills] Reusable dishes patches: " + ex);
                if (harmony != null) harmony.UnpatchAll(HarmonyId);
                harmony = null;
            }
        }

        private static void Patch(Type type, string methodName, Type[] signature, string prefixName, string postfixName)
        {
            MethodInfo target = AccessTools.Method(type, methodName, signature);
            if (target == null) throw new MissingMethodException(type.FullName, methodName);
            harmony.Patch(target,
                prefixName == null ? null : new HarmonyMethod(AccessTools.Method(typeof(ReusableDishesSystem), prefixName)),
                postfixName == null ? null : new HarmonyMethod(AccessTools.Method(typeof(ReusableDishesSystem), postfixName)));
        }

        internal static void Tick()
        {
            if (betterBowlRescanRequested)
            {
                betterBowlRescanRequested = false;
                TryPatchBetterBowl();
            }

            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 1f;

            Network_Player player = ComponentManager<Network_Player>.Value;
            Guid world = SkillProgressStorage.IsWorldBound ? SaveAndLoad.WorldGuid : Guid.Empty;
            int durability = MaximumDurability();
            if (refreshInitialized && world == refreshedWorld && player == refreshedPlayer &&
                durability == refreshedDurability) return;

            if (player == null || !player.IsLocalPlayer || player.Inventory == null ||
                player.Inventory.allSlots == null || player.Inventory.allSlots.Count == 0) return;

            RefreshItems();
            refreshedWorld = world;
            refreshedPlayer = player;
            refreshedDurability = durability;
            refreshInitialized = true;
        }

        private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
        {
            if (!betterBowlPatched) betterBowlRescanRequested = true;
        }

        internal static void OnPerkBalanceChanged()
        {
            refreshInitialized = false;
            nextRefresh = 0f;
        }

        private static void RefreshItems()
        {
            Item_Base bowl = ItemManager.GetItemByName("Claybowl_Empty");
            if (bowl != null) ConfigureItem(bowl, true);
            Item_Base plate = ItemManager.GetItemByName("ClayPlate_Empty");
            if (plate != null) ConfigureItem(plate, true);

            Network_Player player = ComponentManager<Network_Player>.Value;
            if (player == null || !player.IsLocalPlayer || player.Inventory == null || player.Inventory.allSlots == null) return;
            foreach (Slot slot in player.Inventory.allSlots)
            {
                if (slot == null || slot.IsEmpty || slot.itemInstance == null) continue;
                ItemInstance item = slot.itemInstance;
                if (IsEmptyBowl(item.baseItem))
                {
                    ConfigureItem(item.baseItem, true);
                    NormalizeEmptyBowl(slot);
                }
                else if (GetReturnedBowl(item.baseItem) != null)
                    ConfigureItem(item.baseItem, false);
            }
        }

        private static int Tier()
        {
            SkillData skill = SkillCatalog.Find("cooking");
            AbilityData perk = skill != null ? skill.FindAbility("durablebowl") : null;
            if (perk == null || skill.Level < perk.RequiredLevel) return 0;
            return Mathf.Clamp(perk.PreviewTier, 0, 2);
        }

        private static int MaximumDurability()
        {
            return Mathf.Clamp(BaseDurability * (Tier() >= 1 ? FirstTierMultiplier : 1), 1, 1000);
        }

        private static bool IsEmptyBowl(Item_Base item)
        {
            if (item == null) return false;
            string name = item.UniqueName ?? string.Empty;
            return (name.IndexOf("Claybowl", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("ClayPlate", StringComparison.OrdinalIgnoreCase) >= 0) &&
                   name.IndexOf("Empty", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static Item_Base GetReturnedBowl(Item_Base item)
        {
            if (item == null || item.settings_consumeable == null || IsEmptyBowl(item)) return null;
            Cost after = item.settings_consumeable.ItemAfterUse;
            if ((object)after != null && after.item != null && IsEmptyBowl(after.item)) return after.item;
            string name = item.UniqueName ?? string.Empty;
            if (name.IndexOf("Claybowl", StringComparison.OrdinalIgnoreCase) >= 0)
                return ItemManager.GetItemByName("Claybowl_Empty");
            if (name.IndexOf("ClayPlate", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Item_Base plate = ItemManager.GetItemByName("ClayPlate_Empty");
                return plate != null ? plate : ItemManager.GetItemByName("Claybowl_Empty");
            }
            return null;
        }

        internal static void EnsureFoodIsSeparate(Item_Base food)
        {
            if (food != null && GetReturnedBowl(food) != null)
                ConfigureItem(food, false);
        }

        private static void ConfigureItem(Item_Base item, bool isEmpty)
        {
            if (item == null || item.settings_Inventory == null) return;
            ItemDefaults defaults;
            if (!ModifiedItems.TryGetValue(item, out defaults))
            {
                defaults = new ItemDefaults
                {
                    MaxUses = item.MaxUses,
                    StackSize = item.settings_Inventory.StackSize,
                    Empty = isEmpty
                };
                ModifiedItems[item] = defaults;
            }
            if (isEmpty && item.MaxUses != MaximumDurability())
                MaxUsesField.SetValue(item, MaximumDurability());
            if (item.settings_Inventory.StackSize != 1)
                StackSizeField.SetValue(item.settings_Inventory, 1);
        }

        private static int ReadWear(ItemInstance item)
        {
            if (item == null || string.IsNullOrEmpty(item.exclusiveString)) return 0;
            string source = item.exclusiveString;
            int index = source.IndexOf(WearMarker, StringComparison.Ordinal);
            if (index < 0) return 0;
            int start = index + WearMarker.Length;
            int end = source.IndexOf('|', start);
            if (end < 0) end = source.Length;
            int wear;
            return int.TryParse(source.Substring(start, end - start), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out wear) ? Mathf.Clamp(wear, 0, 100000) : 0;
        }

        private static void WriteWear(ItemInstance item, int wear)
        {
            if (item == null) return;
            string source = item.exclusiveString ?? string.Empty;
            int index = source.IndexOf(WearMarker, StringComparison.Ordinal);
            if (index >= 0)
            {
                int end = source.IndexOf('|', index + WearMarker.Length);
                if (end < 0) end = source.Length;
                source = source.Remove(index, end - index);
            }
            item.exclusiveString = source + WearMarker + Mathf.Max(0, wear).ToString(CultureInfo.InvariantCulture);
        }

        private static void NormalizeEmptyBowl(Slot slot)
        {
            if (slot == null || slot.IsEmpty || !IsEmptyBowl(slot.itemInstance.baseItem)) return;
            ItemInstance item = slot.itemInstance;
            int wear = ReadWear(item);
            int allowed = Mathf.Max(1, MaximumDurability() - wear);
            if (item.Uses != allowed)
            {
                item.Uses = allowed;
                slot.RefreshComponents();
            }
            if (item.exclusiveString == null || item.exclusiveString.IndexOf(WearMarker, StringComparison.Ordinal) < 0)
                WriteWear(item, 0);
        }

        private static void AfterSetItem(Slot __instance)
        {
            if (__instance == null || __instance.IsEmpty || __instance.itemInstance == null) return;
            Item_Base item = __instance.itemInstance.baseItem;
            if (IsEmptyBowl(item))
            {
                ConfigureItem(item, true);
                NormalizeEmptyBowl(__instance);
            }
            else if (GetReturnedBowl(item) != null)
                ConfigureItem(item, false);
        }

        private static void BeforeIncrementUses(Slot __instance, int amountOfUsesToAdd, ref ConsumeState __state)
        {
            __state = default(ConsumeState);
            if (__instance == null || __instance.IsEmpty || amountOfUsesToAdd >= 0) return;
            ItemInstance item = __instance.itemInstance;
            Item_Base bowl = GetReturnedBowl(item.baseItem);
            if (bowl == null || item.Uses + amountOfUsesToAdd > 0) return;
            ConfigureItem(bowl, true);
            __state = new ConsumeState
            {
                Active = true,
                EmptyBowl = bowl,
                Wear = ReadWear(item)
            };
        }

        private static void AfterIncrementUses(Slot __instance, bool addItemAfterUseToInventory, ConsumeState __state)
        {
            if (!__state.Active || !addItemAfterUseToInventory ||
                __instance == null || __state.EmptyBowl == null) return;
            int wear = __state.Wear + (Tier() >= 2 ? 0 : 1);
            if (wear >= MaximumDurability() && Tier() < 2)
            {
                if (!__instance.IsEmpty && IsEmptyBowl(__instance.itemInstance.baseItem))
                    __instance.SetItem((ItemInstance)null);
                return;
            }

            if (__instance.IsEmpty)
            {
                __instance.SetItem(__state.EmptyBowl, 1);
            }
            else if (!IsEmptyBowl(__instance.itemInstance.baseItem))
            {
                // При наличии ещё одной порции результат сохраняется отдельно
                Network_Player player = ComponentManager<Network_Player>.Value;
                if (player != null && player.IsLocalPlayer && player.Inventory != null)
                {
                    ItemInstance spare = new ItemInstance(__state.EmptyBowl, 1, MaximumDurability(), "");
                    WriteWear(spare, wear);
                    spare.Uses = Mathf.Max(1, MaximumDurability() - wear);
                    player.Inventory.AddItem(spare, true);
                }
                return;
            }
            if (!__instance.IsEmpty && IsEmptyBowl(__instance.itemInstance.baseItem))
            {
                WriteWear(__instance.itemInstance, wear);
                NormalizeEmptyBowl(__instance);
            }
        }

        private static void BeforePickupFood(CookingTable __instance, Network_Player player, ref PickupState __state)
        {
            __state = null;
            if (__instance == null || player == null || !player.IsLocalPlayer || player.Inventory == null ||
                __instance.CurrentRecipe == null || __instance.Portions == 0U) return;
            Item_Base food = __instance.CurrentRecipe.Result;
            Item_Base bowl = PickupBowlField.GetValue(__instance) as Item_Base;
            if (food == null || !IsEmptyBowl(bowl)) return;
            ConfigureItem(bowl, true);
            ConfigureItem(food, false);
            foreach (Slot slot in player.Inventory.allSlots)
            {
                if (slot == null || slot.IsEmpty || slot.itemInstance.UniqueIndex != bowl.UniqueIndex) continue;
                __state = new PickupState { FoodName = food.UniqueName, Wear = ReadWear(slot.itemInstance) };
                Pickups[player.Inventory] = __state;
                break;
            }
        }

        private static void BeforeAddFood(Inventory __instance, string uniqueItemName, ref Slot __state)
        {
            __state = null;
            PickupState pending;
            if (__instance == null || !Pickups.TryGetValue(__instance, out pending) ||
                pending.FoodName != uniqueItemName) return;
            Item_Base food = ItemManager.GetItemByName(uniqueItemName);
            if (food != null) __state = __instance.FindSuitableSlot(food);
        }

        internal static void ApplyPendingPickupWear(Inventory inventory, ItemInstance food)
        {
            PickupState pending;
            if (inventory == null || food == null ||
                !Pickups.TryGetValue(inventory, out pending) || pending.Applied ||
                pending.FoodName != food.UniqueName) return;
            WriteWear(food, pending.Wear);
            pending.Applied = true;
        }

        private static void AfterAddFood(Inventory __instance, string uniqueItemName, Slot __state)
        {
            PickupState pending;
            if (__instance == null || __state == null || __state.IsEmpty ||
                !Pickups.TryGetValue(__instance, out pending) || pending.Applied ||
                pending.FoodName != uniqueItemName || __state.itemInstance.UniqueName != uniqueItemName) return;
            WriteWear(__state.itemInstance, pending.Wear);
            pending.Applied = true;
        }

        private static void AfterPickupFood(Network_Player player, PickupState __state, bool __result)
        {
            if (player != null && player.Inventory != null && __state != null)
                Pickups.Remove(player.Inventory);
        }

        private static bool SkipBetterBowl(Item_Base item)
        {
            return GetReturnedBowl(item) == null;
        }

        private static void TryPatchBetterBowl()
        {
            if (betterBowlPatched || harmony == null) return;
            Type type = null;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { type = assembly.GetType("BetterBowl.BetterBowlMod", false); }
                catch { continue; }
                if (type != null) break;
            }
            MethodInfo method = type == null ? null : AccessTools.Method(type, "OnConsumeItem", new[] { typeof(Item_Base) });
            if (method == null) return;
            try
            {
                harmony.Patch(method, prefix: new HarmonyMethod(AccessTools.Method(typeof(ReusableDishesSystem), "SkipBetterBowl")));
                betterBowlPatched = true;
            }
            catch (Exception ex) { Debug.LogWarning("[xSkills] BetterBowl compatibility: " + ex.Message); }
        }

        internal static void Dispose()
        {
            AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
            if (harmony != null) harmony.UnpatchAll(HarmonyId);
            harmony = null;
            betterBowlPatched = false;
            betterBowlRescanRequested = false;
            refreshInitialized = false;
            refreshedWorld = Guid.Empty;
            refreshedPlayer = null;
            refreshedDurability = 0;
            nextRefresh = 0f;
            Pickups.Clear();
            foreach (KeyValuePair<Item_Base, ItemDefaults> entry in ModifiedItems)
            {
                if (entry.Key == null || entry.Key.settings_Inventory == null) continue;
                if (entry.Value.Empty) MaxUsesField.SetValue(entry.Key, entry.Value.MaxUses);
                StackSizeField.SetValue(entry.Key.settings_Inventory, entry.Value.StackSize);
            }
            ModifiedItems.Clear();
        }
    }
}
