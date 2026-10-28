using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using HMLLibrary;
using UnityEngine;
using xSkills.Services;

namespace xSkills.Skills
{
    public sealed class Cooking : XSkill
    {
        private const string HarmonyId = "xskills.cooking.experience";
        private const string SettingsPrefix = "xskills_cooking_experience_v1_";
        private const float MaximumExperience = 100f;

        internal const float DefaultGrillExperience = 1f;
        internal const float DefaultPotExperience = 3f;
        internal const float DefaultWaterExperience = 0.25f;
        internal const float DefaultDiversityBonus = 1f;

        private struct CookOwner
        {
            internal ulong PlayerId;
            internal int ItemIndex;
        }

        private sealed class PotBatch
        {
            internal SO_CookingTable_Recipe Recipe;
            internal int UniqueIngredients;
            internal int FoodGroups;
            internal int IngredientCount;
            internal readonly Dictionary<ulong, int> Contributions = new Dictionary<ulong, int>();
        }

        private static readonly Dictionary<CookingSlot, CookOwner> StandOwners =
            new Dictionary<CookingSlot, CookOwner>();
        private static readonly Dictionary<CookingTable_Slot, CookOwner> PotOwners =
            new Dictionary<CookingTable_Slot, CookOwner>();
        private static readonly Dictionary<CookingTable, PotBatch> PotBatches =
            new Dictionary<CookingTable, PotBatch>();

        private static Harmony harmony;
        private static Guid trackedWorld;

        public Cooking() : base("cooking", "xskills:skill-cooking", "xskills:group-processing")
        {
            AddAbility(new Ability("chef", "xskills:ability-chef",
                "xskills:abilitydesc-chef", 1, 3, new[] { 10, 20, 30 }));
            AddAbility(new Ability("eggtimer", "xskills:ability-eggtimer",
                "xskills:abilitydesc-eggtimer", 3, 3, new[] { 10, 20, 30 }));
            AddAbility(new Ability("preserver", "xskills:ability-preserver",
                "xskills:abilitydesc-preserver", 5, 3, new[] { 10, 20, 30 }));
            AddAbility(new Ability("happymeal", "xskills:ability-happymeal",
                "xskills:abilitydesc-happymeal", 7, 3, new[] { 10, 20, 30 }));
            AddAbility(new Ability("fastfood", "xskills:ability-fastfood",
                "xskills:abilitydesc-fastfood", 9, 3, new[] { 10, 20, 30 }));
            AddAbility(new Ability("durablebowl", "xskills:ability-durablebowl",
                "xskills:abilitydesc-durablebowl", 4, 2, new[] { 2, 0 }));
            xSkills.Systems.ReusableDishesSystem.RegisterAbility(this);
        }

        // Настройки наград

        internal static float GrillExperience { get { return GetSetting("grill", DefaultGrillExperience); } }
        internal static float PotExperience { get { return GetSetting("pot", DefaultPotExperience); } }
        internal static float WaterExperience { get { return GetSetting("water", DefaultWaterExperience); } }
        internal static float DiversityBonus { get { return GetSetting("diversity", DefaultDiversityBonus); } }

        internal static void SetExperience(string type, float value)
        {
            if (!ValidType(type) || float.IsNaN(value) || float.IsInfinity(value)) return;
            PlayerPrefs.SetFloat(SettingsPrefix + type,
                Mathf.Round(Mathf.Clamp(value, 0f, MaximumExperience) * 100f) / 100f);
            PlayerPrefs.Save();
        }

        internal static void RestoreExperienceDefaults()
        {
            PlayerPrefs.DeleteKey(SettingsPrefix + "grill");
            PlayerPrefs.DeleteKey(SettingsPrefix + "pot");
            PlayerPrefs.DeleteKey(SettingsPrefix + "water");
            PlayerPrefs.DeleteKey(SettingsPrefix + "diversity");
            PlayerPrefs.Save();
        }

        private static float GetSetting(string type, float defaultValue)
        {
            float value = PlayerPrefs.GetFloat(SettingsPrefix + type, defaultValue);
            return float.IsNaN(value) || float.IsInfinity(value)
                ? defaultValue : Mathf.Clamp(value, 0f, MaximumExperience);
        }

        private static bool ValidType(string type)
        {
            return type == "grill" || type == "pot" || type == "water" || type == "diversity";
        }

        // Перехватчики игрового процесса

        internal static void Initialize()
        {
            if (harmony != null) return;
            try
            {
                harmony = new Harmony(HarmonyId);

                Patch(typeof(Block_CookingStand), "InsertItem",
                    new[] { typeof(Item_Base), typeof(CookingSlot[]), typeof(Network_Player) },
                    null, "AfterStandInsert");
                Patch(typeof(CookingSlot), "OnComplete", Type.EmptyTypes,
                    "BeforeSlotComplete", "AfterSlotComplete");
                Patch(typeof(CookingSlot), "ResetSlot", Type.EmptyTypes,
                    null, "AfterSlotReset");

                Patch(typeof(CookingTable_Slot), "InsertItem",
                    new[] { typeof(Network_Player), typeof(ItemInstance) },
                    null, "AfterPotIngredientInsert");
                Patch(typeof(CookingTable_Slot), "RemoveItem",
                    new[] { typeof(Network_Player) }, null, "AfterPotIngredientRemove");
                Patch(typeof(CookingTable), "StartCooking",
                    new[] { typeof(SO_CookingTable_Recipe) },
                    "BeforePotStart", "AfterPotStart");
                Patch(typeof(CookingTable), "FinishCooking", Type.EmptyTypes,
                    "BeforePotFinish", "AfterPotFinish");
            }
            catch (Exception ex)
            {
                Debug.LogError("[xSkills] Cooking experience patches: " + ex);
                if (harmony != null) harmony.UnpatchAll(HarmonyId);
                harmony = null;
            }
        }

        private static void Patch(Type owner, string name, Type[] arguments,
            string prefixName, string postfixName)
        {
            MethodInfo method = AccessTools.Method(owner, name, arguments);
            if (method == null) throw new MissingMethodException(owner.FullName, name);
            MethodInfo prefix = prefixName == null ? null : AccessTools.Method(typeof(Cooking), prefixName);
            MethodInfo postfix = postfixName == null ? null : AccessTools.Method(typeof(Cooking), postfixName);
            if ((prefixName != null && prefix == null) || (postfixName != null && postfix == null))
                throw new MissingMethodException(typeof(Cooking).FullName, prefixName + "/" + postfixName);
            harmony.Patch(method, prefix == null ? null : new HarmonyMethod(prefix),
                postfix == null ? null : new HarmonyMethod(postfix));
        }

        internal static void Tick()
        {
            Guid world = SkillProgressStorage.IsWorldBound ? SaveAndLoad.WorldGuid : Guid.Empty;
            if (world == trackedWorld) return;
            trackedWorld = world;
            ClearTrackedCooking();
        }

        internal static void Dispose()
        {
            if (harmony != null) harmony.UnpatchAll(HarmonyId);
            harmony = null;
            ClearTrackedCooking();
            trackedWorld = Guid.Empty;
        }

        private static void ClearTrackedCooking()
        {
            StandOwners.Clear();
            PotOwners.Clear();
            PotBatches.Clear();
        }

        // Владелец фиксируется в момент закладки продукта

        private static void AfterStandInsert(Item_Base itemToInsert, CookingSlot[] emptySlots,
            Network_Player player, bool __result)
        {
            if (!__result || player == null || player.steamID == 0UL ||
                itemToInsert == null || emptySlots == null || emptySlots.Length == 0 ||
                emptySlots[0] == null || !WorldActive()) return;

            StandOwners[emptySlots[0]] = new CookOwner
            {
                PlayerId = player.steamID,
                ItemIndex = itemToInsert.UniqueIndex
            };
        }

        private static void BeforeSlotComplete(CookingSlot __instance, ref bool __state)
        {
            __state = __instance != null && !__instance.IsComplete;
        }

        private static void AfterSlotComplete(CookingSlot __instance, bool __state)
        {
            if (!__state || __instance == null || !__instance.IsComplete) return;
            CookOwner owner;
            if (!StandOwners.TryGetValue(__instance, out owner)) return;

            // Учитывается только основной слот, а не его связанные копии
            StandOwners.Remove(__instance);
            if (!WorldActive() || __instance.CurrentItem == null ||
                __instance.CurrentItem.UniqueIndex != owner.ItemIndex) return;

            Block_CookingStand stand = __instance.GetComponentInParent<Block_CookingStand>();
            if (stand == null || stand is Block_CookingStand_Smelter) return;
            if (stand is Block_CookingStand_Purifier)
                AwardForOwner(owner.PlayerId, WaterExperience);
            else
                AwardForOwner(owner.PlayerId, GrillExperience);
        }

        private static void AfterSlotReset(CookingSlot __instance)
        {
            if (__instance != null) StandOwners.Remove(__instance);
        }

        // Владельцы ингредиентов котла

        private static void AfterPotIngredientInsert(CookingTable_Slot __instance,
            Network_Player player, ItemInstance itemInstance)
        {
            if (__instance == null || player == null || player.steamID == 0UL ||
                itemInstance == null || itemInstance.baseItem == null ||
                !__instance.HasItem || !WorldActive()) return;

            PotOwners[__instance] = new CookOwner
            {
                PlayerId = player.steamID,
                ItemIndex = itemInstance.baseItem.UniqueIndex
            };
        }

        private static void AfterPotIngredientRemove(CookingTable_Slot __instance)
        {
            if (__instance != null && !__instance.HasItem) PotOwners.Remove(__instance);
        }

        private static void BeforePotStart(CookingTable __instance, ref PotBatch __state)
        {
            __state = null;
            if (!(__instance is CookingTable_Pot) || !WorldActive()) return;

            PotBatch batch = new PotBatch();
            batch.UniqueIngredients = CountUniqueIngredients(__instance, out batch.FoodGroups);
            if (__instance.Slots == null) return;

            foreach (CookingTable_Slot slot in __instance.Slots)
            {
                if (slot == null || !slot.HasItem || slot.CurrentItem == null ||
                    slot.CurrentItem.baseItem == null) continue;

                batch.IngredientCount++;
                CookOwner owner;
                if (!PotOwners.TryGetValue(slot, out owner) || owner.PlayerId == 0UL ||
                    slot.CurrentItem.baseItem.UniqueIndex != owner.ItemIndex) continue;

                int count;
                batch.Contributions.TryGetValue(owner.PlayerId, out count);
                batch.Contributions[owner.PlayerId] = count + 1;
            }

            if (batch.IngredientCount > 0) __state = batch;
        }

        private static void AfterPotStart(CookingTable __instance, SO_CookingTable_Recipe recipe,
            bool __result, PotBatch __state)
        {
            if (!(__instance is CookingTable_Pot) || !__result || __state == null) return;
            __state.Recipe = recipe;
            PotBatches[__instance] = __state;

            // После запуска готовки записи отдельных ингредиентов больше не используются
            if (__instance.Slots == null) return;
            foreach (CookingTable_Slot slot in __instance.Slots)
                if (slot != null) PotOwners.Remove(slot);
        }

        // Награда выдаётся один раз при завершении всего котла

        private static void BeforePotFinish(CookingTable __instance, ref bool __state)
        {
            __state = __instance is CookingTable_Pot && WorldActive() &&
                __instance.CurrentRecipe != null && __instance.Portions == 0U;
        }

        private static void AfterPotFinish(CookingTable __instance, bool __state)
        {
            if (!__state || __instance == null || __instance.Portions == 0U) return;
            PotBatch batch;
            if (!PotBatches.TryGetValue(__instance, out batch)) return;
            PotBatches.Remove(__instance);

            if (batch.Recipe != __instance.CurrentRecipe || batch.IngredientCount <= 0) return;
            SO_CookingTable_Recipe recipe = __instance.CurrentRecipe;
            bool validRecipe = recipe.Result != null && recipe.Result.UniqueIndex != 203;
            int extraIngredients = validRecipe ? Mathf.Max(0, batch.UniqueIngredients - 1) : 0;
            int extraGroups = validRecipe ? Mathf.Max(0, batch.FoodGroups - 1) : 0;
            float xpPerPortion = PotExperience +
                DiversityBonus * (extraIngredients + 0.5f * extraGroups);
            float totalXp = xpPerPortion * __instance.Portions;

            // Опыт делится по количеству добавленных ингредиентов
            foreach (KeyValuePair<ulong, int> contribution in batch.Contributions)
                AwardForOwner(contribution.Key,
                    totalXp * contribution.Value / batch.IngredientCount);
        }

        // Разнообразие учитывает уникальные продукты и пищевые группы

        private static int CountUniqueIngredients(CookingTable table, out int foodGroups)
        {
            foodGroups = 0;
            if (table == null || table.Slots == null) return 0;
            HashSet<int> items = new HashSet<int>();
            HashSet<int> groups = new HashSet<int>();
            foreach (CookingTable_Slot slot in table.Slots)
            {
                if (slot == null || !slot.HasItem || slot.CurrentItem == null ||
                    slot.CurrentItem.baseItem == null) continue;
                Item_Base item = slot.CurrentItem.baseItem;
                if (!items.Add(item.UniqueIndex)) continue;
                int category = IngredientGroup(item.UniqueName);
                if (category != 0) groups.Add(category);
            }
            foodGroups = groups.Count;
            return items.Count;
        }

        private static int IngredientGroup(string name)
        {
            if (string.IsNullOrEmpty(name)) return 0;
            if (name == "Raw_Beet" || name == "Raw_Potato" ||
                name == "CaveMushroom" || name == "SilverAlgae" || name == "Seaweed") return 4;
            if (name == "Egg" || name.StartsWith("Raw_", StringComparison.Ordinal)) return 1;
            if (name == "Bucket_Milk") return 2;
            switch (name)
            {
                case "Banana":
                case "Berries_Red":
                case "Coconut":
                case "Jar_Honey":
                case "Mango":
                case "Pineapple":
                case "Strawberry":
                case "Watermelon":
                    return 3;
                default:
                    return 0;
            }
        }

        // Начисление выполняется только на клиенте владельца

        private static bool WorldActive()
        {
            if (!SkillProgressStorage.IsWorldBound || SaveAndLoad.IsGameLoading) return false;
            try { return RAPI.IsCurrentSceneGame(); }
            catch { return false; }
        }

        private static void AwardForOwner(ulong owner, float amount)
        {
            if (owner == 0UL || amount <= 0f || float.IsNaN(amount) ||
                float.IsInfinity(amount) || !WorldActive()) return;
            Network_Player local;
            try { local = RAPI.GetLocalPlayer(); }
            catch { return; }
            if (local == null || !local.IsLocalPlayer || local.steamID != owner) return;

            SkillData skill = SkillCatalog.Find("cooking");
            if (skill != null && skill.Level < skill.MaxLevel) skill.AddExperience(amount);
        }
    }
}
