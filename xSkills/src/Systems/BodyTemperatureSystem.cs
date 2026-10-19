using System;
using System.Collections.Generic;
using System.Globalization;
using HMLLibrary;
using UltimateWater;
using UnityEngine;
using UnityEngine.UI;

using xSkills.Services;
namespace xSkills.Systems
{
    internal static class BodyTemperatureSystem
    {
        private const float NormalTemperature = 36.6f;
        private const float MinimumTemperature = 31f;
        private const float MaximumTemperature = 41f;
        private const float PartialWetness = 50f;
        private const float DryingPerSecond = 0.22f;
        private const float HeatRadius = 4.5f;
        private const float TorchRadius = 2.7f;
        private const float FireWarmingPerSecond = 0.050f;
        private const float FireExtraDryingPerSecond = 0.85f;
        private const float HeatScanInterval = 8f;
        private const float SimulationInterval = 0.2f;
        private const float PlayerPollInterval = 0.5f;
        private const float ClassificationCacheLifetime = 120f;
        private const int MaximumClassifiedBlocks = 4096;

        private sealed class HeatSource
        {
            public Block Block;
            public Fuel Fuel;
            public ParticleSystem CookingFlame;
            public bool IsTorch;
            public float Radius;
            public float Strength;
        }

        private static readonly List<HeatSource> HeatSources = new List<HeatSource>();
        private static readonly Dictionary<Block, HeatSource> CachedHeatSources =
            new Dictionary<Block, HeatSource>();
        private static readonly HashSet<Block> CachedNonHeatBlocks = new HashSet<Block>();
        private static float nextHeatScan;
        private static float nextClassificationReset;
        private static float nextPlayerPoll;
        private static float simulationTime;
        private static float nextWeatherCheck;
        private static float nextShadeCheck;
        private static float outsideTemperature = 26f;
        private static float airHumidity = 80f;
        private static float daylight;
        private static float airHeatIndex = 26f;
        private static float injuryTimer;
        private static float sunHeatIndex;
        private static bool raining;
        private static bool shaded;
        private static bool sheltered;
        private static bool fullySubmerged;
        private static bool isInWorld;
        private static bool installed;
        private static Network_Player currentPlayer;
        private static GameObject overlayRoot;
        private static Image coldOverlay;
        private static Image hotOverlay;

        public static float Temperature { get; private set; } = NormalTemperature;
        public static float Wetness { get; private set; }
        public static bool IsShaded { get { return shaded; } }
        public static bool HasPlayer { get { return isInWorld && currentPlayer != null; } }

        public static void Initialize()
        {
            if (installed) return;
            installed = true;
            PersonController.CameraSubmersionAction += OnCameraSubmersion;
            CreateOverlay();
        }

        public static void Tick(float dt, float uiDt)
        {
            if (!installed) return;

            // Игрок и состояние сцены проверяются не каждый кадр
            float now = Time.unscaledTime;
            if (now >= nextPlayerPoll)
            {
                nextPlayerPoll = now + PlayerPollInterval;
                Network_Player localPlayer = null;
                try
                {
                    if (RAPI.IsCurrentSceneGame() && Raft_Network.WorldHasBeenRecieved)
                        localPlayer = RAPI.GetLocalPlayer();
                }
                catch (Exception) { }

                if (localPlayer == null || !localPlayer.IsLocalPlayer)
                {
                    if (isInWorld) ResetWorldState();
                }
                else if (currentPlayer != localPlayer)
                {
                    ResetWorldState();
                    currentPlayer = localPlayer;
                    isInWorld = true;
                }
            }

            Network_Player player = currentPlayer;
            if (!isInWorld || player == null)
            {
                if (isInWorld) ResetWorldState();
                if (overlayRoot != null && overlayRoot.activeSelf)
                {
                    SetOverlayAlpha(coldOverlay, 0f);
                    SetOverlayAlpha(hotOverlay, 0f);
                    overlayRoot.SetActive(false);
                }
                return;
            }

            PersonController controller = player.PersonController;
            bool inWater = controller != null && controller.controllerType == ControllerType.Water;
            bool underwater = (controller != null && controller.SubmersionState == SubmersionState.Full) ||
                fullySubmerged;

            if (now >= nextWeatherCheck)
            {
                nextWeatherCheck = now + 1f;
                WorldEnvironment.ThermalSnapshot environment;
                if (WorldEnvironment.TryReadThermal(out environment))
                {
                    outsideTemperature = environment.Temperature;
                    airHumidity = environment.RelativeHumidity;
                    daylight = environment.Daylight;
                    raining = environment.IsRaining;
                    airHeatIndex = CalculateHeatIndex(outsideTemperature, airHumidity);
                }
            }

            if (now >= nextShadeCheck)
            {
                nextShadeCheck = now + 0.8f;
                sheltered = IsUnderShelter(player);
                shaded = CheckShade(player, sheltered);
            }

            // Полное погружение намокает сразу, остальные значения обновляются реже
            if (underwater) Wetness = 100f;
            else if (inWater) Wetness = Mathf.Max(Wetness, PartialWetness);

            simulationTime += Mathf.Clamp(dt, 0f, 0.25f);
            if (simulationTime >= SimulationInterval)
            {
                float step = simulationTime;
                simulationTime = 0f;
                Simulate(player, step, inWater, underwater);
            }

            float coldAmount = Mathf.Clamp01((35f - Temperature) / (35f - MinimumTemperature));
            float heatAmount = Mathf.Clamp01((Temperature - 38f) / (MaximumTemperature - 38f));
            bool hasEffect = coldAmount > 0f || heatAmount > 0f ||
                (coldOverlay != null && coldOverlay.enabled) ||
                (hotOverlay != null && hotOverlay.enabled);
            if (overlayRoot != null && overlayRoot.activeSelf != hasEffect)
                overlayRoot.SetActive(hasEffect);
            if (hasEffect)
            {
                float blend = 1f - Mathf.Exp(-3.5f * Mathf.Max(0f, uiDt));
                UpdateOverlay(coldOverlay, coldAmount, blend);
                UpdateOverlay(hotOverlay, heatAmount, blend);
            }
        }

        private static void Simulate(Network_Player player, float step, bool inWater, bool underwater)
        {
            float fireInfluence = !underwater && !inWater ? GetFireInfluence(player) : 0f;
            if (!underwater && !inWater)
            {
                if (raining && !sheltered)
                    Wetness = Mathf.Max(Wetness, Mathf.Min(85f, Wetness + 0.18f * step));
                else
                {
                    float airDrying = airHumidity >= 90f ? 0.55f : (daylight > 0.25f && !shaded ? 1.45f : 1f);
                    Wetness = Mathf.Max(0f, Wetness -
                        (DryingPerSecond * airDrying + FireExtraDryingPerSecond * fireInfluence) * step);
                }
            }

            if (player.PlayerScript != null && player.PlayerScript.IsDead) return;

            float regulation = (NormalTemperature - Temperature) *
                (Temperature >= NormalTemperature ? 0.013f : 0.008f);
            regulation = Mathf.Clamp(regulation, -0.075f, 0.045f);

            float sunHeat = shaded || underwater || inWater ? 0f : 5.5f * daylight;
            float apparentHeat = airHeatIndex + sunHeat;
            sunHeatIndex = apparentHeat;

            float heatStrain = underwater || inWater ? 0f :
                Mathf.Clamp((apparentHeat - 41f) * 0.0026f, 0f, 0.065f);
            float cooling = 0f;
            if (underwater)
            {
                float waterTemperature = Mathf.Clamp(outsideTemperature - 1.5f, 23f, 30f);
                cooling = 0.008f + Mathf.Max(0f, 30f - waterTemperature) * 0.00065f;
            }
            else if (inWater)
                cooling = 0.0055f;
            else if (Wetness > 0f)
            {
                float evaporation = Mathf.Lerp(1f, 0.28f, Mathf.Clamp01((airHumidity - 55f) / 45f));
                cooling = Wetness / 100f * 0.009f * evaporation;
                if (raining) cooling += 0.002f;
            }

            float fireWarming = fireInfluence * FireWarmingPerSecond *
                (Temperature < NormalTemperature ? 1f : 0.25f);
            Temperature = Mathf.Clamp(Temperature +
                (regulation + heatStrain + fireWarming - cooling) * step,
                MinimumTemperature, MaximumTemperature);

            ApplyHealthConsequences(player, step);
        }

        private static float CalculateHeatIndex(float celsius, float humidity)
        {
            // Формула NOAA применяется только в области, где индекс жары имеет смысл
            if (celsius < 26.7f || humidity < 40f) return celsius;
            float f = celsius * 1.8f + 32f;
            float rh = humidity;
            float index = -42.379f + 2.04901523f * f + 10.14333127f * rh
                - 0.22475541f * f * rh - 0.00683783f * f * f
                - 0.05481717f * rh * rh + 0.00122874f * f * f * rh
                + 0.00085282f * f * rh * rh - 0.00000199f * f * f * rh * rh;
            return Mathf.Clamp((index - 32f) / 1.8f, celsius, 67f);
        }

        private static void ApplyHealthConsequences(Network_Player player, float dt)
        {
            if (GameModeValueManager.IsPlayerInvurnerable || player.Stats == null) return;
            PlayerStats stats = player.Stats;
            if (Temperature >= 38f && stats.stat_thirst != null && stats.stat_thirst.Normal != null)
            {
                float dehydration = (Temperature - 38f) * 0.012f * dt;
                stats.stat_thirst.RemoveValue(dehydration);
            }

            if (Temperature > 33f && Temperature < 40f)
            {
                injuryTimer = 0f;
                return;
            }
            injuryTimer += dt;
            if (injuryTimer < 8f) return;
            injuryTimer = 0f;
            float severity = Temperature <= 33f ? 33f - Temperature : Temperature - 40f;
            float damage = 0.35f + Mathf.Max(0f, severity) * 0.55f;
            stats.Damage(damage, player.transform.position, Vector3.up, EntityType.Environment, false, null);
        }

        public static string Command(string[] args)
        {
            if (!installed || !HasPlayer) return "Enter a world first";
            if (args == null || args.Length == 0) return Status();
            string input = args[0];
            if (string.Equals(input, "bodytemp", StringComparison.OrdinalIgnoreCase))
            {
                if (args.Length < 2) return Status();
                input = args[1];
            }
            if (string.Equals(input, "get", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(input, "status", StringComparison.OrdinalIgnoreCase)) return Status();
            if (string.Equals(input, "sources", StringComparison.OrdinalIgnoreCase)) return ListHeatSources();
            if (string.Equals(input, "inspect", StringComparison.OrdinalIgnoreCase)) return InspectNearbyBlocks();
            if (string.Equals(input, "reset", StringComparison.OrdinalIgnoreCase))
            {
                Temperature = NormalTemperature;
                injuryTimer = 0f;
                return Status();
            }
            if (string.Equals(input, "resume", StringComparison.OrdinalIgnoreCase))
                return Status() + " (simulation is always running)";

            float value;
            if (!float.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
                !float.TryParse(input, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
                return "Usage: bodytemp <31..41|get|reset|sources|inspect>";
            if (float.IsNaN(value) || float.IsInfinity(value) ||
                value < MinimumTemperature || value > MaximumTemperature)
                return "Body temperature must be between 31 and 41 C";

            Temperature = value;
            injuryTimer = 0f;
            return Status() + " (natural temperature changes are active)";
        }

        private static string Status()
        {
            return "Body: " + Temperature.ToString("0.0", CultureInfo.InvariantCulture) +
                " C, wet: " + Mathf.RoundToInt(Wetness) + "%, air: " +
                outsideTemperature.ToString("0.0", CultureInfo.InvariantCulture) +
                " C, humidity: " + Mathf.RoundToInt(airHumidity) +
                "%, feels: " + sunHeatIndex.ToString("0.0", CultureInfo.InvariantCulture) + " C";
        }

        private static void OnCameraSubmersion(SubmersionState state)
        {
            if (isInWorld) fullySubmerged = state == SubmersionState.Full;
        }

        private static float GetFireInfluence(Network_Player player)
        {
            if (Time.unscaledTime >= nextHeatScan)
            {
                nextHeatScan = Time.unscaledTime + HeatScanInterval;
                RefreshHeatSources(player);
            }
            Vector3 position = player.transform.position;
            float maxInfluence = 0f;
            foreach (HeatSource source in HeatSources)
            {
                if (source.Block == null) continue;
                float sqrDistance = (source.Block.transform.position - position).sqrMagnitude;
                if (sqrDistance >= source.Radius * source.Radius || !IsHeatActive(source)) continue;
                float proximity = 1f - Mathf.Sqrt(sqrDistance) / source.Radius;
                maxInfluence = Mathf.Max(maxInfluence, proximity * source.Strength);
            }
            return maxInfluence;
        }

        private static bool IsHeatActive(HeatSource source)
        {
            if (source.Block == null || !source.Block.gameObject.activeInHierarchy) return false;
            if (source.Fuel != null) return source.Fuel.IsBurning();
            if (source.IsTorch) return true;
            return source.CookingFlame != null && source.CookingFlame.gameObject.activeInHierarchy &&
                source.CookingFlame.isPlaying;
        }

        private static void RefreshHeatSources(Network_Player player)
        {
            HeatSources.Clear();
            try
            {
                float now = Time.unscaledTime;
                if (now >= nextClassificationReset ||
                    CachedHeatSources.Count + CachedNonHeatBlocks.Count >= MaximumClassifiedBlocks)
                {
                    CachedHeatSources.Clear();
                    CachedNonHeatBlocks.Clear();
                    nextClassificationReset = now + ClassificationCacheLifetime;
                }

                List<Block> blocks = BlockCreator.GetPlacedBlocks();
                if (blocks == null) return;
                Vector3 position = player.transform.position;
                foreach (Block block in blocks)
                {
                    if (block == null || (block.transform.position - position).sqrMagnitude > 100f)
                        continue;
                    HeatSource source;
                    if (CachedHeatSources.TryGetValue(block, out source))
                    {
                        HeatSources.Add(source);
                        continue;
                    }
                    if (CachedNonHeatBlocks.Contains(block)) continue;

                    source = ClassifyHeatSource(block);
                    if (source == null)
                        CachedNonHeatBlocks.Add(block);
                    else
                    {
                        CachedHeatSources.Add(block, source);
                        HeatSources.Add(source);
                    }
                }
            }
            catch (Exception)
            {
                HeatSources.Clear();
                CachedHeatSources.Clear();
                CachedNonHeatBlocks.Clear();
            }
        }

        private static HeatSource ClassifyHeatSource(Block block)
        {
            if (block is Block_CookingStand_Electric || block is Block_CookingStand_Smelter)
                return null;

            string name = block.gameObject.name.ToLowerInvariant();
            bool torch = name.Contains("torch") || name.Contains("brazier");
            bool cooking = block is Block_CookingStand || name.Contains("cooking") ||
                name.Contains("cookpot") || name.Contains("grill") ||
                name.Contains("campfire") || name.Contains("fireplace") ||
                name.Contains("bonfire") || name.Contains("stove");
            if (!torch && !cooking)
                cooking = block.GetComponentInChildren<CookingSlot>(true) != null;
            if (!torch && !cooking) return null;

            Fuel fuel = null;
            Block_CookingStand stand = block as Block_CookingStand;
            if (stand != null) fuel = stand.fuel;
            if (fuel == null) fuel = block.GetComponentInChildren<Fuel>(true);
            ParticleSystem flame = !torch && fuel == null ?
                block.GetComponentInChildren<ParticleSystem>(true) : null;
            if (fuel == null && !torch && flame == null) return null;
            return new HeatSource
            {
                Block = block,
                Fuel = fuel,
                CookingFlame = flame,
                IsTorch = torch,
                Radius = torch ? TorchRadius : HeatRadius,
                Strength = torch ? 0.26f : 1f
            };
        }

        private static string ListHeatSources()
        {
            if (currentPlayer == null) return "No local player";
            RefreshHeatSources(currentPlayer);
            nextHeatScan = Time.unscaledTime + HeatScanInterval;
            if (HeatSources.Count == 0)
                return "No recognized campfire, cooking pot, grill or torch within 10 m";
            var result = new System.Text.StringBuilder("Nearby heat sources: ");
            int count = 0;
            foreach (HeatSource source in HeatSources)
            {
                if (count++ >= 7) break;
                if (count > 1) result.Append("; ");
                result.Append(source.Block.gameObject.name);
                result.Append(IsHeatActive(source) ? " [ON]" : " [OFF]");
                result.Append(" ");
                result.Append(Vector3.Distance(source.Block.transform.position,
                    currentPlayer.transform.position).ToString("0.0", CultureInfo.InvariantCulture));
                result.Append("m");
            }
            return result.ToString();
        }

        private static string InspectNearbyBlocks()
        {
            if (currentPlayer == null) return "No local player";
            List<Block> blocks = BlockCreator.GetPlacedBlocks();
            if (blocks == null) return "No placed blocks";
            var nearest = new List<Block>();
            Vector3 position = currentPlayer.transform.position;
            foreach (Block block in blocks)
            {
                if (block == null || (block.transform.position - position).sqrMagnitude > 36f) continue;
                string name = block.gameObject.name.ToLowerInvariant();
                if (name.Contains("foundation") || name.Contains("floor") ||
                    name.Contains("triangle") || name.Contains("pillar") ||
                    name.Contains("roof") || name.Contains("ceiling")) continue;
                nearest.Add(block);
            }
            nearest.Sort((a, b) => (a.transform.position - position).sqrMagnitude.CompareTo(
                (b.transform.position - position).sqrMagnitude));
            if (nearest.Count == 0) return "No placed objects within 6m";
            var result = new System.Text.StringBuilder("Objects near player: ");
            for (int i = 0; i < nearest.Count && i < 8; i++)
            {
                Block block = nearest[i];
                if (i > 0) result.Append("; ");
                result.Append(block.gameObject.name);
                Fuel fuel = block.GetComponentInChildren<Fuel>(true);
                if (fuel != null) result.Append(fuel.IsBurning() ? " [FUEL ON]" : " [FUEL OFF]");
                if (block.GetComponentInChildren<CookingSlot>(true) != null) result.Append(" [COOK]");
                result.Append(" ");
                result.Append(Vector3.Distance(block.transform.position, position)
                    .ToString("0.0", CultureInfo.InvariantCulture));
                result.Append("m");
            }
            return result.ToString();
        }

        private static bool IsUnderShelter(Network_Player player)
        {
            Transform eye = player.CameraTransform;
            Vector3 start = eye != null ? eye.position : player.transform.position + Vector3.up;
            return Physics.Raycast(start + Vector3.up * 0.15f, Vector3.up, 30f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }

        private static bool CheckShade(Network_Player player, bool underShelter)
        {
            if (daylight < 0.02f) return true;
            Light sun = RenderSettings.sun;
            if (sun == null || !sun.isActiveAndEnabled || sun.intensity <= 0f)
                return underShelter;
            Vector3 toSun = -sun.transform.forward;
            if (Vector3.Dot(toSun, Vector3.up) < 0.1f) return true;
            Transform eye = player.CameraTransform;
            Vector3 start = eye != null ? eye.position : player.transform.position + Vector3.up;
            RaycastHit hit;
            return Physics.Raycast(start + toSun * 0.3f, toSun, out hit, 60f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }

        private static void CreateOverlay()
        {
            overlayRoot = new GameObject("xSkills temperature effects", typeof(RectTransform), typeof(Canvas));
            Canvas canvas = overlayRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30000;
            UnityEngine.Object.DontDestroyOnLoad(overlayRoot);
            coldOverlay = CreateImage(overlayRoot.transform, "Cold overlay", "cold");
            hotOverlay = CreateImage(overlayRoot.transform, "Heat overlay", "hot");
            overlayRoot.SetActive(false);
        }

        private static Image CreateImage(Transform parent, string name, string spriteName)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(Image));
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            Image image = obj.GetComponent<Image>();
            image.raycastTarget = false;
            image.sprite = ResourceLoader.Sprite("effects", spriteName);
            image.type = Image.Type.Simple;
            image.preserveAspect = false;
            image.color = image.sprite != null ? new Color(1f, 1f, 1f, 0f) :
                spriteName == "cold" ? new Color(0.23f, 0.55f, 1f, 0f) :
                new Color(1f, 0.28f, 0.08f, 0f);
            image.enabled = false;
            return image;
        }

        private static void UpdateOverlay(Image image, float intensity, float blend)
        {
            if (image == null || (!image.enabled && intensity <= 0f)) return;
            float maxAlpha = image.sprite != null ? 0.88f : 0.16f;
            Color color = image.color;
            float alpha = Mathf.Lerp(color.a, intensity * maxAlpha, blend);
            if (intensity <= 0f && alpha < 0.002f)
            {
                if (color.a != 0f)
                {
                    color.a = 0f;
                    image.color = color;
                }
                image.enabled = false;
                return;
            }
            if (Mathf.Abs(alpha - color.a) < 0.001f) return;
            if (!image.enabled) image.enabled = true;
            color.a = alpha;
            image.color = color;
        }

        private static void SetOverlayAlpha(Image image, float alpha)
        {
            if (image == null) return;
            Color color = image.color;
            if (color.a != alpha)
            {
                color.a = alpha;
                image.color = color;
            }
            if (alpha <= 0f && image.enabled) image.enabled = false;
        }

        private static void ResetWorldState()
        {
            currentPlayer = null;
            isInWorld = false;
            fullySubmerged = false;
            shaded = false;
            sheltered = false;
            raining = false;
            Wetness = 0f;
            Temperature = NormalTemperature;
            outsideTemperature = 26f;
            airHumidity = 80f;
            daylight = 0f;
            injuryTimer = 0f;
            sunHeatIndex = 26f;
            HeatSources.Clear();
            CachedHeatSources.Clear();
            CachedNonHeatBlocks.Clear();
            nextClassificationReset = 0f;
            simulationTime = 0f;
            airHeatIndex = 26f;
            nextHeatScan = 0f;
            nextShadeCheck = 0f;
            nextWeatherCheck = 0f;
        }

        public static void Dispose()
        {
            if (!installed) return;
            PersonController.CameraSubmersionAction -= OnCameraSubmersion;
            installed = false;
            ResetWorldState();
            nextPlayerPoll = 0f;
            if (overlayRoot != null) UnityEngine.Object.Destroy(overlayRoot);
            overlayRoot = null;
            coldOverlay = null;
            hotOverlay = null;
        }
    }
}
