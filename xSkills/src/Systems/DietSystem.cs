using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using HMLLibrary;
using UnityEngine;

namespace xSkills.Systems
{
    // Диета привязана к миру и локальному игроку
    internal static class DietSystem
    {
        private const float MaxDiet = 100f;
        private const float DefaultMaximumBonusHP = 40f;
        private const float DefaultDailyDietLossPercent = 5f;
        private const float DefaultDeathDietLossPercent = 50f;
        private const string HarmonyId = "xskills.diet.v77";
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        private static readonly Dictionary<string, float[]> Food = new Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "Banana", new float[] { 0f, 0f, 1f, 0f } },
            { "Berries_Red", new float[] { 0f, 0f, 1f, 0f } },
            { "Bucket_Milk", new float[] { 0f, 1f, 0f, 0f } },
            { "CaveMushroom", new float[] { 0f, 0f, 0f, 1f } },
            { "Claybowl_CoconutChicken", new float[] { 0.5f, 0f, 0.25f, 0.25f } },
            { "Claybowl_HeadBroth", new float[] { 0.5f, 0.25f, 0f, 0.25f } },
            { "Claybowl_HeartyStew", new float[] { 0.25f, 0f, 0f, 0.75f } },
            { "Claybowl_RootVegetableSoup", new float[] { 0f, 0f, 0f, 1f } },
            { "Claybowl_SimpleFishStew", new float[] { 0.5f, 0f, 0f, 0.5f } },
            { "ClayPlate_BBQ", new float[] { 0.75f, 0f, 0f, 0.25f } },
            { "ClayPlate_CatfishDeluxe", new float[] { 0.25f, 0f, 0f, 0.75f } },
            { "ClayPlate_MushroomOmelette", new float[] { 0.5f, 0f, 0f, 0.5f } },
            { "ClayPlate_SalmonSalad", new float[] { 0.25f, 0f, 0.25f, 0.5f } },
            { "ClayPlate_SharkDinner", new float[] { 0.5f, 0f, 0f, 0.5f } },
            { "ClayPlate_SteakWithJam", new float[] { 0.25f, 0f, 0.5f, 0.25f } },
            { "ClayPlate_Sushi", new float[] { 0.75f, 0f, 0f, 0.25f } },
            { "Coconut", new float[] { 0f, 0f, 1f, 0f } },
            { "Cooked_Beet", new float[] { 0f, 0f, 0f, 1f } },
            { "Cooked_Catfish", new float[] { 1f, 0f, 0f, 0f } },
            { "Cooked_Drumstick", new float[] { 1f, 0f, 0f, 0f } },
            { "Cooked_GenericMeat", new float[] { 1f, 0f, 0f, 0f } },
            { "Cooked_Herring", new float[] { 1f, 0f, 0f, 0f } },
            { "Cooked_Mackerel", new float[] { 1f, 0f, 0f, 0f } },
            { "Cooked_Pomfret", new float[] { 1f, 0f, 0f, 0f } },
            { "Cooked_Potato", new float[] { 0f, 0f, 0f, 1f } },
            { "Cooked_Salmon", new float[] { 1f, 0f, 0f, 0f } },
            { "Cooked_Shark", new float[] { 1f, 0f, 0f, 0f } },
            { "Cooked_Tilapia", new float[] { 1f, 0f, 0f, 0f } },
            { "DrinkingGlass_CoconutBeat", new float[] { 0f, 0f, 0.75f, 0.25f } },
            { "DrinkingGlass_Mangonana", new float[] { 0f, 0.25f, 0.75f, 0f } },
            { "DrinkingGlass_RedbeetShot", new float[] { 0f, 0f, 0.25f, 0.75f } },
            { "DrinkingGlass_RedMelon", new float[] { 0f, 0f, 1f, 0f } },
            { "DrinkingGlass_SilverSmoothie", new float[] { 0f, 0.25f, 0.5f, 0.25f } },
            { "DrinkingGlass_SimpleSmoothie", new float[] { 0f, 0f, 1f, 0f } },
            { "DrinkingGlass_SpicyPineberry", new float[] { 0f, 0f, 0.75f, 0.25f } },
            { "DrinkingGlass_StrawberryColada", new float[] { 0f, 0f, 1f, 0f } },
            { "Egg", new float[] { 1f, 0f, 0f, 0f } },
            { "Jar_Honey", new float[] { 0f, 0f, 1f, 0f } },
            { "Mango", new float[] { 0f, 0f, 1f, 0f } },
            { "Pineapple", new float[] { 0f, 0f, 1f, 0f } },
            { "Raw_Beet", new float[] { 0f, 0f, 0f, 1f } },
            { "Raw_Catfish", new float[] { 1f, 0f, 0f, 0f } },
            { "Raw_Drumstick", new float[] { 1f, 0f, 0f, 0f } },
            { "Raw_GenericMeat", new float[] { 1f, 0f, 0f, 0f } },
            { "Raw_Herring", new float[] { 1f, 0f, 0f, 0f } },
            { "Raw_Mackerel", new float[] { 1f, 0f, 0f, 0f } },
            { "Raw_Pomfret", new float[] { 1f, 0f, 0f, 0f } },
            { "Raw_Potato", new float[] { 0f, 0f, 0f, 1f } },
            { "Raw_Salmon", new float[] { 1f, 0f, 0f, 0f } },
            { "Raw_Shark", new float[] { 1f, 0f, 0f, 0f } },
            { "Raw_Tilapia", new float[] { 1f, 0f, 0f, 0f } },
            { "SilverAlgae", new float[] { 0f, 0f, 0f, 1f } },
            { "Strawberry", new float[] { 0f, 0f, 1f, 0f } },
            { "Watermelon", new float[] { 0f, 0f, 1f, 0f } },
        };
        private const string SavePrefix = "xskills_diet_v2_";
        private const string LegacySavePrefix = "xskills_diet_v1_";
        private const string PrefMaximumBonusHP = "xskills_diet_max_bonus_hp";
        private const string PrefDailyLossPercent = "xskills_diet_daily_loss_percent";
        private const string PrefDeathLossPercent = "xskills_diet_death_loss_percent";
        private static readonly float[] levels = new float[4];
        private static Harmony harmony;
        private static Network_Player currentPlayer;
        private static Stat_Health currentHealth;
        private static string saveKey;
        private static float baseHealthMax;
        private static float baseHealthTargetMax;
        private static float appliedHealthMax;
        private static float appliedHealthTargetMax;
        private static bool deathLatched;
        private static bool dirty;
        private static bool installed;
        private static float saveAt;
        private static int lastDietDay = -1;
        private static HudRectState hudHealthRoot;
        private static HudRectState hudHealthFill;
        private static HudRectState hudHealthBackground;
        private static HudRectState hudTargetFillArea;
        private static float hudBackgroundLeftInset;
        private static float hudBackgroundRightInset;
        private static int hudScreenWidth;
        private static int hudScreenHeight;
        private static readonly Vector3[] hudCorners = new Vector3[4];
        private static readonly Vector3[] hudOtherCorners = new Vector3[4];

        private sealed class HudRectState
        {
            internal readonly RectTransform Rect;
            private readonly Vector2 originalSize;
            private readonly Vector2 originalPosition;
            internal readonly float OriginalWidth;

            internal HudRectState(RectTransform rect)
            {
                Rect = rect;
                if (rect == null) return;
                originalSize = rect.sizeDelta;
                originalPosition = rect.anchoredPosition;
                OriginalWidth = rect.rect.width;
            }

            internal void ApplyExtraWidth(float extraWidth)
            {
                if (Rect == null) return;
                float width = Mathf.Max(1f, OriginalWidth + extraWidth);
                if (Mathf.Abs(Rect.rect.width - width) > .001f)
                    Rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);

                Vector2 position = originalPosition;
                position.x += extraWidth * Rect.pivot.x;
                if ((Rect.anchoredPosition - position).sqrMagnitude > .000001f)
                    Rect.anchoredPosition = position;
            }

            internal void Restore()
            {
                if (Rect == null) return;
                Rect.sizeDelta = originalSize;
                Rect.anchoredPosition = originalPosition;
            }
        }

        public static float Level(int category)
        {
            return category >= 0 && category < levels.Length ? Mathf.Clamp01(levels[category] / MaxDiet) : 0f;
        }

        public static float CapacityHP
        {
            get { return ComputeCapacity(); }
        }

        public static float MaximumBonusHP
        {
            get { return Mathf.Clamp(PlayerPrefs.GetFloat(PrefMaximumBonusHP, DefaultMaximumBonusHP), 0f, 200f); }
        }

        public static float DailyLossPercent
        {
            get { return Mathf.Clamp(PlayerPrefs.GetFloat(PrefDailyLossPercent, DefaultDailyDietLossPercent), 0f, 100f); }
        }

        public static float DeathLossPercent
        {
            get { return Mathf.Clamp(PlayerPrefs.GetFloat(PrefDeathLossPercent, DefaultDeathDietLossPercent), 0f, 100f); }
        }

        public static void SetMaximumBonusHP(float value)
        {
            value = Mathf.Clamp(Mathf.Round(value), 0f, 200f);
            if (Mathf.Abs(MaximumBonusHP - value) < .001f) return;
            PlayerPrefs.SetFloat(PrefMaximumBonusHP, value);
            PlayerPrefs.Save();
            if (currentPlayer != null)
            {
                ApplyHealthCapacity(false, false);
                UpdateHealthHud();
            }
        }

        public static void SetDailyLossPercent(float value)
        {
            value = Mathf.Clamp(Mathf.Round(value), 0f, 100f);
            if (Mathf.Abs(DailyLossPercent - value) < .001f) return;
            PlayerPrefs.SetFloat(PrefDailyLossPercent, value);
            PlayerPrefs.Save();
        }

        public static void SetDeathLossPercent(float value)
        {
            value = Mathf.Clamp(Mathf.Round(value), 0f, 100f);
            if (Mathf.Abs(DeathLossPercent - value) < .001f) return;
            PlayerPrefs.SetFloat(PrefDeathLossPercent, value);
            PlayerPrefs.Save();
        }

        public static void Initialize()
        {
            if (installed) return;
            installed = true;
            try
            {
                Canvas.willRenderCanvases -= UpdateHealthHud;
                Canvas.willRenderCanvases += UpdateHealthHud;
                harmony = new Harmony(HarmonyId);
                harmony.CreateClassProcessor(typeof(ConsumePatch)).Patch();
                harmony.CreateClassProcessor(typeof(DeathPatch)).Patch();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[xSkills Diet] Could not initialize diet patches: " + ex.Message);
            }
        }

        public static void Tick()
        {
            if (!installed) return;
            Network_Player player = null;
            try
            {
                if (RAPI.IsCurrentSceneGame() && !SaveAndLoad.IsGameLoading)
                    player = RAPI.GetLocalPlayer();
            }
            catch (Exception) { }
            if (player == null || player.Stats == null || SaveAndLoad.WorldGuid == Guid.Empty)
            {
                if (currentPlayer != null) Detach();
                return;
            }
            EnsurePlayer(player);
            if (currentPlayer != player) return;

            bool dead = player.PlayerScript != null && player.PlayerScript.IsDead;
            if (dead && !deathLatched) ApplyDeath();
            if (!dead) deathLatched = false;

            HandleDailyDecay();
            ApplyHealthCapacity(false, false);
            UpdateHealthHud();
            if (dirty && Time.unscaledTime >= saveAt)
            {
                Save();
                saveAt = Time.unscaledTime + 2f;
            }
        }

        public static void Dispose()
        {
            Canvas.willRenderCanvases -= UpdateHealthHud;
            Detach();
            if (harmony != null)
            {
                harmony.UnpatchAll(HarmonyId);
                harmony = null;
            }
            installed = false;
        }

        private static void EnsurePlayer(Network_Player player)
        {
            string id = SaveAndLoad.WorldGuid.ToString("N") + "_" + player.steamID;
            string key = SavePrefix + id;
            if (player == currentPlayer && key == saveKey) return;
            Detach();
            currentPlayer = player;
            currentHealth = GetNormalHealth(player.Stats);
            saveKey = key;
            for (int i = 0; i < levels.Length; i++) levels[i] = 0f;
            lastDietDay = -1;
            deathLatched = player.PlayerScript != null && player.PlayerScript.IsDead;

            if (currentHealth != null)
            {
                baseHealthMax = currentHealth.Max;
                baseHealthTargetMax = currentHealth.statTarget != null
                    ? currentHealth.statTarget.Max : 0f;
                appliedHealthMax = currentHealth.Max;
                appliedHealthTargetMax = baseHealthTargetMax;
            }
            else
            {
                baseHealthMax = baseHealthTargetMax = 0f;
                appliedHealthMax = appliedHealthTargetMax = 0f;
            }

            bool migrated = false;
            try
            {
                string saved = PlayerPrefs.GetString(key, "");
                if (string.IsNullOrEmpty(saved))
                {
                    saved = PlayerPrefs.GetString(LegacySavePrefix + id, "");
                    migrated = !string.IsNullOrEmpty(saved);
                }
                string[] parts = saved.Split(';');
                if (parts.Length >= 4)
                {
                    for (int i = 0; i < levels.Length; i++)
                    {
                        float value;
                        if (float.TryParse(parts[i], NumberStyles.Float, Invariant, out value))
                            levels[i] = Mathf.Clamp(value, 0f, MaxDiet);
                    }
                    int savedDay;
                    if (!migrated && parts.Length >= 5 &&
                        int.TryParse(parts[4], NumberStyles.Integer, Invariant, out savedDay))
                        lastDietDay = savedDay;
                }

                // Старый щит удаляется только если не пересекается с бонусом коктейля
                if (migrated && parts.Length >= 5 && player.Stats.stat_BonusHealth != null)
                {
                    float shield;
                    Stat_Health bonus = player.Stats.stat_BonusHealth;
                    if (float.TryParse(parts[4], NumberStyles.Float, Invariant, out shield) &&
                        shield > .01f && Mathf.Abs(bonus.Value - shield) < .05f)
                    {
                        bonus.Value = 0f;
                        if (bonus.statTarget != null) bonus.statTarget.Value = bonus.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[xSkills Diet] Diet data could not be restored: " + ex.Message);
            }

            if (lastDietDay < 0) lastDietDay = Mathf.Max(0, WorldManager.DayCounter);
            HandleDailyDecay();
            ApplyHealthCapacity(false, true);

            if (migrated) dirty = true;
        }

        private static void HandleDailyDecay()
        {
            if (currentPlayer == null) return;
            int day = Mathf.Max(0, WorldManager.DayCounter);
            if (lastDietDay < 0)
            {
                lastDietDay = day;
                dirty = true;
                return;
            }
            if (day < lastDietDay)
            {
                lastDietDay = day;
                dirty = true;
                return;
            }
            if (day == lastDietDay) return;
            int elapsed = day - lastDietDay;
            float multiplier = Mathf.Pow(1f - DailyLossPercent / 100f, elapsed);
            for (int i = 0; i < levels.Length; i++) levels[i] *= multiplier;
            lastDietDay = day;
            ApplyHealthCapacity(false, false);

            dirty = true;
        }

        private static Stat_Health GetNormalHealth(PlayerStats stats)
        {
            return stats != null ? stats.stat_health : null;
        }

        private static void ApplyHealthCapacity(bool restoreIncrease, bool firstLoad)
        {
            if (currentPlayer == null || currentPlayer.Stats == null) return;
            Stat_Health health = GetNormalHealth(currentPlayer.Stats);
            if (health == null) return;
            if (currentHealth != health)
            {
                currentHealth = health;
                baseHealthMax = health.Max;
                baseHealthTargetMax = health.statTarget != null ? health.statTarget.Max : 0f;
                appliedHealthMax = baseHealthMax;
                appliedHealthTargetMax = baseHealthTargetMax;
                firstLoad = true;
            }
            if (baseHealthMax <= 0f) return;

            // Изменения максимума от других эффектов сохраняются
            if (!firstLoad && Mathf.Abs(health.Max - appliedHealthMax) > .05f)
                baseHealthMax = Mathf.Max(1f, health.Max);
            if (health.statTarget != null && !firstLoad &&
                Mathf.Abs(health.statTarget.Max - appliedHealthTargetMax) > .05f)
                baseHealthTargetMax = Mathf.Max(1f, health.statTarget.Max);

            float desiredMax = baseHealthMax + ComputeCapacity();
            float difference = desiredMax - appliedHealthMax;
            bool wasFull = firstLoad && health.Value >= baseHealthMax - .05f;
            bool maxChanged = Mathf.Abs(health.Max - desiredMax) > .001f;
            if (maxChanged) health.SetMaxValue(desiredMax);
            appliedHealthMax = desiredMax;

            if (health.statTarget != null)
            {
                float desiredTargetMax = Mathf.Max(1f, baseHealthTargetMax) + ComputeCapacity();
                if (Mathf.Abs(health.statTarget.Max - desiredTargetMax) > .001f)
                    health.statTarget.SetMaxValue(desiredTargetMax);
                appliedHealthTargetMax = desiredTargetMax;
            }

            bool valueChanged = false;
            if ((restoreIncrease || wasFull) && difference > .001f &&
                (currentPlayer.PlayerScript == null || !currentPlayer.PlayerScript.IsDead))
            {
                health.Value = Mathf.Min(desiredMax, health.Value + difference);
                valueChanged = true;
            }
            else if (health.Value > desiredMax)
            {
                health.Value = desiredMax;
                valueChanged = true;
            }

            if ((maxChanged || valueChanged) && health.statTarget != null)
                health.statTarget.Value = health.Value;
        }

        [HarmonyPatch(typeof(PlayerStats), "Consume", new Type[] { typeof(Item_Base) })]
        private static class ConsumePatch
        {
            private static void Postfix(PlayerStats __instance, Item_Base edibleItem)
            {
                AfterConsume(__instance, edibleItem);
            }
        }

        [HarmonyPatch(typeof(PlayerStats), "SendOnDeath")]
        private static class DeathPatch
        {
            private static void Postfix(PlayerStats __instance)
            {
                AfterDeath(__instance);
            }
        }

        private static void AfterConsume(PlayerStats __instance, Item_Base edibleItem)
        {
            if (!installed || __instance == null || edibleItem == null ||
                string.IsNullOrEmpty(edibleItem.UniqueName)) return;
            float[] nutrition;
            if (!Food.TryGetValue(edibleItem.UniqueName, out nutrition)) return;
            Network_Player player = null;
            try { player = RAPI.GetLocalPlayer(); }
            catch (Exception) { }
            if (player == null || player.Stats != __instance || SaveAndLoad.WorldGuid == Guid.Empty) return;
            EnsurePlayer(player);
            if (currentPlayer != player) return;
            ItemInstance_Consumeable item = edibleItem.settings_consumeable;
            if (item == null) return;

            float worth = Mathf.Max(0f, item.HungerYield) + .5f * Mathf.Max(0f, item.BonusHungerYield)
                + .3f * Mathf.Max(0f, item.ThirstYield + item.BonusThirstYield);
            float gain = Mathf.Clamp(worth / 2.5f, 2f, 25f);
            float total = 0f;
            for (int i = 0; i < 4; i++) total += nutrition[i];
            if (total <= 0f) return;
            for (int i = 0; i < 4; i++)
                levels[i] = Mathf.Min(MaxDiet, levels[i] + gain * nutrition[i] / total);
            ApplyHealthCapacity(true, false);

            dirty = true;
            Save();
        }

        private static void AfterDeath(PlayerStats __instance)
        {
            if (!installed || __instance == null || currentPlayer == null ||
                currentPlayer.Stats != __instance) return;
            ApplyDeath();
        }

        private static void ApplyDeath()
        {
            if (deathLatched || currentPlayer == null) return;
            deathLatched = true;
            float multiplier = 1f - DeathLossPercent / 100f;
            for (int i = 0; i < levels.Length; i++) levels[i] *= multiplier;
            ApplyHealthCapacity(false, false);

            dirty = true;
            Save();
        }

        private static float ComputeCapacity()
        {
            float sum = 0f;
            for (int i = 0; i < levels.Length; i++) sum += Mathf.Clamp01(levels[i] / MaxDiet);
            return MaximumBonusHP * sum / 4f;
        }

        private static void Detach()
        {
            RestoreHealthHud();
            if (dirty) Save();
            currentPlayer = null;
            currentHealth = null;
            saveKey = null;
            dirty = false;
            lastDietDay = -1;
            baseHealthMax = baseHealthTargetMax = 0f;
            appliedHealthMax = appliedHealthTargetMax = 0f;
            deathLatched = false;
        }

        private static void UpdateHealthHud()
        {
            if (!installed || currentPlayer == null || currentHealth == null ||
                (currentPlayer.PlayerScript != null && currentPlayer.PlayerScript.IsDead)) return;

            CanvasHelper hud = ComponentManager<CanvasHelper>.Value;
            RectTransform healthRoot = hud != null && hud.healthSlider != null
                ? hud.healthSlider.transform as RectTransform : null;
            RectTransform healthFill = hud != null && hud.healthSlider != null
                ? hud.healthSlider.sliderTransform : null;
            if (healthRoot == null || healthFill == null || baseHealthMax <= .001f) return;

            // Слои HP найдены по исходной иерархии HUD Raft
            RectTransform background = healthRoot.Find("SliderBG") as RectTransform;
            RectTransform targetFillArea = healthRoot.Find("TargetHealth/Fill Area") as RectTransform;
            if (background == null || targetFillArea == null) return;

            if (hudHealthRoot == null || hudHealthRoot.Rect != healthRoot ||
                hudHealthFill == null || hudHealthFill.Rect != healthFill ||
                hudHealthBackground == null || hudHealthBackground.Rect != background ||
                hudTargetFillArea == null || hudTargetFillArea.Rect != targetFillArea ||
                hudScreenWidth != Screen.width || hudScreenHeight != Screen.height)
            {
                RestoreHealthHud();
                hudHealthRoot = new HudRectState(healthRoot);
                hudHealthFill = new HudRectState(healthFill);
                hudHealthBackground = new HudRectState(background);
                hudTargetFillArea = new HudRectState(targetFillArea);
                hudScreenWidth = Screen.width;
                hudScreenHeight = Screen.height;

                healthFill.GetWorldCorners(hudCorners);
                background.GetWorldCorners(hudOtherCorners);
                hudBackgroundLeftInset = hudCorners[0].x - hudOtherCorners[0].x;
                hudBackgroundRightInset = hudOtherCorners[3].x - hudCorners[3].x;
            }

            if (hudHealthFill.OriginalWidth <= .001f) return;

            float extraWidth = hudHealthFill.OriginalWidth *
                Mathf.Max(0f, currentHealth.Max / baseHealthMax - 1f);

            // Отступ под значок HP сохраняется, фон удлиняется на столько же, сколько красная полоса
            hudHealthRoot.ApplyExtraWidth(extraWidth);
            hudHealthFill.ApplyExtraWidth(extraWidth);

            healthFill.GetWorldCorners(hudCorners);
            float redLeft = hudCorners[0].x;
            float redRight = hudCorners[3].x;

            SetHorizontalSpan(background,
                redLeft - hudBackgroundLeftInset,
                redRight + hudBackgroundRightInset);

            // Полоса полученного урона / тёмная заливка получает ту же ширину, что и красная
            // Дочерний Fill растягивается самостоятельно благодаря стандартным якорям Unity
            SetHorizontalSpan(targetFillArea, redLeft, redRight);
        }

        private static void SetHorizontalSpan(RectTransform rect, float left, float right)
        {
            if (rect == null) return;
            float axisScale = rect.TransformVector(Vector3.right).x;
            if (Mathf.Abs(axisScale) < .0001f || right <= left) return;

            float desiredWidth = (right - left) / axisScale;
            if (Mathf.Abs(rect.rect.width - desiredWidth) > .001f)
                rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, desiredWidth);

            rect.GetWorldCorners(hudCorners);
            float delta = left - hudCorners[0].x;
            if (Mathf.Abs(delta) > .001f)
            {
                Transform parent = rect.parent;
                Vector3 localDelta = parent != null
                    ? parent.InverseTransformVector(new Vector3(delta, 0f, 0f))
                    : new Vector3(delta, 0f, 0f);
                Vector2 position = rect.anchoredPosition;
                position.x += localDelta.x;
                position.y += localDelta.y;
                rect.anchoredPosition = position;
            }
        }

        private static void RestoreHealthHud()
        {
            if (hudTargetFillArea != null) hudTargetFillArea.Restore();
            if (hudHealthBackground != null) hudHealthBackground.Restore();
            if (hudHealthFill != null) hudHealthFill.Restore();
            if (hudHealthRoot != null) hudHealthRoot.Restore();
            hudTargetFillArea = null;
            hudHealthBackground = null;
            hudHealthFill = null;
            hudHealthRoot = null;
            hudBackgroundLeftInset = 0f;
            hudBackgroundRightInset = 0f;
            hudScreenWidth = 0;
            hudScreenHeight = 0;
        }

        private static void Save()
        {
            if (string.IsNullOrEmpty(saveKey)) return;
            try
            {
                PlayerPrefs.SetString(saveKey,
                    levels[0].ToString("R", Invariant) + ";" +
                    levels[1].ToString("R", Invariant) + ";" +
                    levels[2].ToString("R", Invariant) + ";" +
                    levels[3].ToString("R", Invariant) + ";" +
                    lastDietDay.ToString(Invariant));
                PlayerPrefs.Save();
                dirty = false;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[xSkills Diet] Failed to save diet: " + ex.Message);
            }
        }
    }
}
