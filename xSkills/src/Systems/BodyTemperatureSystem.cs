using System;
using System.Collections.Generic;
using System.Globalization;
using HMLLibrary;
using HarmonyLib;
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
        private const float TorchRadius = 3.3f;
        private const float BrazierRadius = 4.5f;
        private const float FireWarmingPerSecond = 0.050f;
        private const float FireExtraDryingPerSecond = 0.85f;
        private const float HeatScanInterval = 8f;
        private const float SimulationInterval = 0.2f;
        private const float PassiveTemperatureRate = 1f / 3f;
        private const string MovementHarmonyId = "xskills.bodytemperature.movement";
        private const float PlayerPollInterval = 0.5f;
        private const float ClassificationCacheLifetime = 120f;
        private const int MaximumClassifiedBlocks = 4096;

        private sealed class HeatSource
        {
            public Block Block;
            public Fuel Fuel;
            public ParticleSystem CookingFlame;
            public LightFlickering FlameLight;
            public Transform HeatPoint;
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
        private static readonly RaycastHit[] ShadeRayHits = new RaycastHit[32];
        private static readonly Collider[] ShadeNearbyColliders = new Collider[256];
        private static readonly List<Renderer> ShadeRenderers = new List<Renderer>();
        private static readonly HashSet<Renderer> ShadeRendererSet = new HashSet<Renderer>();
        private const float ShadeRayLength = 100f;
        private const float ShadeRenderRadius = 55f;
        private const int ShadeSampleCount = 9;
        private const int ShadeRequiredSamples = 7;
        private const int ShadeMaxMeshTriangles = 20000;
        private const int ShadeTriangleBudgetPerRay = 65000;
        private static readonly Vector2[] ShadeSampleOffsets =
        {
            new Vector2(0f, 0f),
            new Vector2(0.40f, 0f), new Vector2(-0.40f, 0f),
            new Vector2(0f, 0.40f), new Vector2(0f, -0.40f),
            new Vector2(0.30f, 0.30f), new Vector2(-0.30f, 0.30f),
            new Vector2(0.30f, -0.30f), new Vector2(-0.30f, -0.30f)
        };
        private sealed class ShadeMeshData
        {
            public Vector3[] Vertices;
            public int[] Triangles;
        }
        private static readonly Dictionary<Mesh, ShadeMeshData> ShadeMeshCache =
            new Dictionary<Mesh, ShadeMeshData>();
        private static float nextShadeRendererRefresh;
        private static Vector3 lastShadeRendererPosition;
        private static float outsideTemperature = 26f;
        private static float airHumidity = 80f;
        private static float daylight;
        private static float airHeatIndex = 26f;
        private static float injuryTimer;
        private static float waterExposureSeconds;
        private static float waterHeatRelief;
        private static float sunHeatIndex;
        private static bool raining;
        private static bool shaded;
        private static bool fireIsWarming;
        private static bool sheltered;
        private static bool fullySubmerged;
        private static bool isInWorld;
        private static bool deathLatched;
        private static bool installed;
        private static Network_Player currentPlayer;
        private static GameObject overlayRoot;
        private static Image coldOverlay;
        private static Image hotOverlay;
        private static Image nauseaOverlay;
        private const int DistortionBands = 18;
        private static RawImage[] distortionImages;
        private static TemperatureNauseaCapture nauseaCapture;
        private static float nextNauseaCameraCheck;
        private static bool distortionVisible;
        private static Harmony movementHarmony;

        public static float Temperature { get; private set; } = NormalTemperature;
        public static float Wetness { get; private set; }
        public static bool IsShaded { get { return shaded; } }
        internal static bool IsWarmingByFire { get { return HasPlayer && fireIsWarming; } }
        public static bool HasPlayer { get { return isInWorld && currentPlayer != null; } }
        internal static float OutsideTemperature { get { return outsideTemperature; } }
        internal static float AirHumidity { get { return airHumidity; } }
        internal static float Daylight { get { return daylight; } }
        internal static bool IsRaining { get { return raining; } }
        internal static PersonController LocalController
        {
            get { return HasPlayer && currentPlayer.PlayerScript != null && !currentPlayer.PlayerScript.IsDead
                ? currentPlayer.PersonController : null; }
        }
        internal static float MovementMultiplier
        {
            get
            {
                if (LocalController == null) return 1f;
                BodyTemperatureSettings settings = BodyTemperatureSettings.Current;
                float factor = 1f;
                if (settings.ColdEnabled)
                {
                    float cold = Mathf.Clamp01((settings.ColdSlowStart - Temperature) /
                        Mathf.Max(.1f, settings.ColdSlowStart - MinimumTemperature));
                    factor = Mathf.Min(factor, Mathf.Lerp(1f, settings.ColdMinimumSpeed, cold));
                }
                if (settings.HeatEnabled)
                {
                    float hot = Mathf.Clamp01((Temperature - settings.HeatSlowStart) /
                        Mathf.Max(.1f, MaximumTemperature - settings.HeatSlowStart));
                    factor = Mathf.Min(factor, Mathf.Lerp(1f, settings.HeatMinimumSpeed, hot));
                }
                return factor;
            }
        }
        private static float NauseaIntensity
        {
            get
            {
                BodyTemperatureSettings s = BodyTemperatureSettings.Current;
                return !s.HeatEnabled || !s.NauseaEnabled ? 0f :
                    Mathf.Clamp01((Temperature - s.NauseaStart) /
                        Mathf.Max(.1f, MaximumTemperature - s.NauseaStart)) *
                    (1f - waterHeatRelief * 0.9f);
            }
        }
        internal static bool NeedsNauseaCapture
        {
            get
            {
                BodyTemperatureSettings s = BodyTemperatureSettings.Current;
                return HasPlayer && s.NauseaDistortionEnabled && s.NauseaStrength > 0f &&
                    NauseaIntensity > 0.015f;
            }
        }

        public static void Initialize()
        {
            if (installed) return;
            installed = true;
            PersonController.CameraSubmersionAction += OnCameraSubmersion;
            try
            {
                movementHarmony = new Harmony(MovementHarmonyId);
                movementHarmony.CreateClassProcessor(typeof(TemperatureGroundMovementPatch)).Patch();
                movementHarmony.CreateClassProcessor(typeof(TemperatureWaterMovementPatch)).Patch();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[xSkills Temperature] Movement patch: " + ex.Message);
                if (movementHarmony != null) movementHarmony.UnpatchAll(MovementHarmonyId);
                movementHarmony = null;
            }
            CreateOverlay();
        }

        public static void Tick(float dt, float uiDt)
        {
            if (!installed) return;
            BodyTemperatureSettings.Tick();

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
                fireIsWarming = false;
                if (isInWorld) ResetWorldState();
                if (overlayRoot != null && overlayRoot.activeSelf)
                {
                    SetOverlayAlpha(coldOverlay, 0f);
                    SetOverlayAlpha(hotOverlay, 0f);
                    SetOverlayAlpha(nauseaOverlay, 0f);
                    StopNauseaCapture();
                    overlayRoot.SetActive(false);
                }
                return;
            }

            if (player.PlayerScript != null && player.PlayerScript.IsDead)
            {
                if (!deathLatched)
                {
                    deathLatched = true;
                    Temperature = NormalTemperature;
                    Wetness = 0f;
                    injuryTimer = 0f;
                    waterExposureSeconds = 0f;
                    waterHeatRelief = 0f;
                }
            }
            else deathLatched = false;

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

            BodyTemperatureSettings settings = BodyTemperatureSettings.Current;
            float coldAmount = settings.ColdEnabled ? Mathf.Clamp01((settings.ColdSlowStart - Temperature) /
                Mathf.Max(.1f, settings.ColdSlowStart - MinimumTemperature)) : 0f;
            float heatAmount = settings.HeatEnabled ? Mathf.Clamp01((Temperature - settings.HeatSlowStart) /
                Mathf.Max(.1f, MaximumTemperature - settings.HeatSlowStart)) *
                (1f - waterHeatRelief * 0.9f) : 0f;
            float nauseaAmount = NauseaIntensity * settings.NauseaStrength;
            bool hasEffect = coldAmount > 0f || heatAmount > 0f || nauseaAmount > 0f ||
                HasVisibleOverlay(coldOverlay) || HasVisibleOverlay(hotOverlay) ||
                HasVisibleOverlay(nauseaOverlay);
            if (!hasEffect)
            {
                SetOverlayAlpha(coldOverlay, 0f);
                SetOverlayAlpha(hotOverlay, 0f);
                SetOverlayAlpha(nauseaOverlay, 0f);
                if (overlayRoot != null && overlayRoot.activeSelf)
                    overlayRoot.SetActive(false);
            }
            else
            {
                if (overlayRoot != null && !overlayRoot.activeSelf)
                    overlayRoot.SetActive(true);
                float blend = 1f - Mathf.Exp(-3.5f * Mathf.Max(0f, uiDt));
                UpdateOverlay(coldOverlay, coldAmount, blend);
                UpdateOverlay(hotOverlay, heatAmount, blend);
                float pulse = 0.8f + 0.2f * Mathf.Sin(now * 1.65f);
                UpdateOverlay(nauseaOverlay, nauseaAmount * pulse * 0.16f, blend);
            }
            UpdateNauseaDistortion(now, nauseaAmount);
        }

        private static void Simulate(Network_Player player, float step, bool inWater, bool underwater)
        {
            float fireInfluence = !underwater && !inWater ? GetFireInfluence(player) : 0f;
            fireIsWarming = fireInfluence > 0.035f && Temperature < MaximumTemperature;
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

            bool immersed = inWater || underwater;
            float reliefTarget = immersed && Temperature > 37.2f ? 1f : 0f;
            waterHeatRelief = Mathf.MoveTowards(waterHeatRelief, reliefTarget,
                step * (reliefTarget > waterHeatRelief ? 0.5f : 0.25f));
            if (!immersed) waterExposureSeconds = 0f;

            float regulation = (NormalTemperature - Temperature) *
                (Temperature >= NormalTemperature ? 0.013f : 0.008f);
            regulation = Mathf.Clamp(regulation, -0.075f, 0.045f);

            // Индекс жары влияет на теплообмен, но не равен нагреву тела
            float sunHeat = shaded || raining || underwater || inWater ? 0f : 5.5f * daylight;
            float humidityBurden = Mathf.Min(Mathf.Max(0f, airHeatIndex - outsideTemperature), 25f) * 0.20f;
            float effectiveHeat = outsideTemperature + humidityBurden + sunHeat;
            sunHeatIndex = airHeatIndex + sunHeat;

            float heatStrain = underwater || inWater ? 0f :
                Mathf.Clamp((effectiveHeat - 37f) * 0.0032f, 0f, 0.05f);
            if (shaded)
            {
                float extremeHeat = Mathf.Clamp01((outsideTemperature - 37f) / 3f);
                heatStrain *= Mathf.Lerp(0.12f, 0.37f, extremeHeat);
            }
            bool exposedToRain = raining && !sheltered && !underwater && !inWater;
            float wetFraction = Mathf.Clamp01(Wetness / 100f);
            // В очень жарком воздухе дождь охлаждает слабее
            float rainRelief = exposedToRain ? Mathf.Clamp01((45f - outsideTemperature) / 10f) : 0f;
            heatStrain *= 1f - rainRelief * Mathf.Lerp(0.45f, 0.80f, wetFraction);
            float cooling = 0f;
            float rapidWaterCooling = 0f;
            if (underwater || inWater)
            {
                float waterTemperature = outsideTemperature < 16f
                    ? Mathf.Clamp(outsideTemperature - 1.5f, -1.5f, 30f)
                    : Mathf.Clamp(outsideTemperature - 1.5f, 19f, 30f);
                float waterCooling = 0.018f + Mathf.Max(0f, 30f - waterTemperature) * 0.0016f;
                cooling = underwater ? waterCooling : waterCooling * 0.7f;

                // В тёплой воде охлаждение постепенно сходит на нет возле нормальной температуры тела
                float tropicalWater = Mathf.InverseLerp(20f, 24f, waterTemperature);
                float excessAboveNormal = Mathf.Max(0f, Temperature - NormalTemperature);
                float warmWaterCooling = Mathf.Clamp01(excessAboveNormal / 0.55f);
                cooling *= Mathf.Lerp(1f, warmWaterCooling, tropicalWater);

                // При перегреве первые секунды в воде быстро снимают излишек тепла
                float excessHeat = Mathf.Clamp01(excessAboveNormal / 2f);
                float contrast = Mathf.Clamp01((Temperature - waterTemperature) / 8f);
                float entryBoost = 0.70f * excessHeat * contrast *
                    Mathf.Exp(-waterExposureSeconds / 14f);
                rapidWaterCooling = Mathf.Min(entryBoost * (underwater ? 1f : 0.9f),
                    excessAboveNormal / Mathf.Max(0.01f, step));
                waterExposureSeconds += step;
            }
            else
            {
                // Прохладный воздух охлаждает сильнее при намокании и под дождём
                float airCooling = Mathf.Min(18f, Mathf.Max(0f, 27f - outsideTemperature)) * 0.0035f;
                if (sheltered) airCooling *= 0.5f;
                airCooling *= 1f + Wetness / 100f * 0.85f;
                if (exposedToRain)
                    airCooling += rainRelief * (0.005f + 0.012f * wetFraction);
                cooling += airCooling;
                if (Wetness > 0f)
                {
                    float evaporation = Mathf.Lerp(1f, 0.28f,
                        Mathf.Clamp01((airHumidity - 55f) / 45f));
                    cooling += Wetness / 100f * 0.009f * evaporation;
                }
            }

            float fireWarming = fireInfluence * FireWarmingPerSecond *
                (Temperature < NormalTemperature ? 1f : 0.25f);
            float nextTemperature = Temperature +
                ((regulation + heatStrain - cooling) * PassiveTemperatureRate +
                fireWarming - rapidWaterCooling) * step;
            if (immersed && outsideTemperature >= 25.5f && Temperature >= NormalTemperature)
                nextTemperature = Mathf.Max(NormalTemperature, nextTemperature);
            if (!immersed && outsideTemperature >= 30f && Temperature >= NormalTemperature)
                nextTemperature = Mathf.Max(NormalTemperature, nextTemperature);
            Temperature = Mathf.Clamp(nextTemperature, MinimumTemperature, MaximumTemperature);

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
            BodyTemperatureSettings s = BodyTemperatureSettings.Current;
            if (s.HeatEnabled && Temperature >= 38f &&
                stats.stat_thirst != null && stats.stat_thirst.Normal != null)
            {
                float dehydration = (Temperature - 38f) * 0.012f * dt;
                stats.stat_thirst.RemoveValue(dehydration);
            }

            if (!s.DamageEnabled || (!s.ColdEnabled && !s.HeatEnabled))
            {
                injuryTimer = 0f;
                return;
            }
            bool cold = s.ColdEnabled && Temperature <= s.ColdDamageStart;
            bool hot = s.HeatEnabled && Temperature >= s.HeatDamageStart;
            if (!cold && !hot)
            {
                injuryTimer = 0f;
                return;
            }
            injuryTimer += dt;
            float interval = Mathf.Max(.5f, s.DamageInterval);
            if (injuryTimer < interval) return;
            injuryTimer %= interval;
            float severity = cold ? s.ColdDamageStart - Temperature : Temperature - s.HeatDamageStart;
            float damage = cold
                ? s.ColdBaseDamage + severity * s.ColdDamagePerDegree
                : s.HeatBaseDamage + severity * s.HeatDamagePerDegree;
            if (damage <= 0f) return;
            stats.Damage(damage, player.transform.position, Vector3.up, EntityType.Environment, false, null);
        }

        public static string Command(string[] args)
        {
            bool russian = Localizer.Code == "ru";
            if (!installed || !HasPlayer)
                return russian ? "Сначала войдите в игровой мир" : "Enter a world first";
            if (args == null || args.Length == 0) return Status();
            string input = args[0];
            if (string.Equals(input, "bodytemp", StringComparison.OrdinalIgnoreCase))
            {
                if (args.Length < 2) return Status();
                input = args[1];
            }
            if (string.Equals(input, "get", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(input, "status", StringComparison.OrdinalIgnoreCase)) return Status();
            if (string.Equals(input, "climate", StringComparison.OrdinalIgnoreCase))
            {
                int argument = string.Equals(args[0], "bodytemp", StringComparison.OrdinalIgnoreCase) ? 2 : 1;
                if (args.Length > argument)
                {
                    int previewDay;
                    if (!int.TryParse(args[argument], NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out previewDay))
                        return russian ? "Использование: bodytemp climate [игровой день]" :
                            "Usage: bodytemp climate [game day]";
                    return WorldEnvironment.ClimateDiagnostic(previewDay);
                }
                return WorldEnvironment.CurrentClimateDiagnostic();
            }
            if (string.Equals(input, "forecast", StringComparison.OrdinalIgnoreCase))
                return WorldEnvironment.ClimateForecast();
            if (string.Equals(input, "region", StringComparison.OrdinalIgnoreCase))
                return ColdBiomeSystem.Diagnostic();
            if (string.Equals(input, "effects", StringComparison.OrdinalIgnoreCase)) return Status();
            if (string.Equals(input, "sources", StringComparison.OrdinalIgnoreCase)) return ListHeatSources();
            if (string.Equals(input, "inspect", StringComparison.OrdinalIgnoreCase)) return InspectNearbyBlocks();
            if (string.Equals(input, "reset", StringComparison.OrdinalIgnoreCase))
            {
                Temperature = NormalTemperature;
                injuryTimer = 0f;
                return Status();
            }
            if (string.Equals(input, "resume", StringComparison.OrdinalIgnoreCase))
                return Status() + (russian ? " (симуляция всегда работает)" : " (simulation is always running)");

            float value;
            if (!float.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
                !float.TryParse(input, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
                return russian ? "Использование: bodytemp <31..41|get|reset|climate|forecast|region|sources|inspect>" :
                    "Usage: bodytemp <31..41|get|reset|climate|forecast|region|sources|inspect>";
            if (float.IsNaN(value) || float.IsInfinity(value) ||
                value < MinimumTemperature || value > MaximumTemperature)
                return russian ? "Температура тела должна быть от 31 до 41 °C" :
                    "Body temperature must be between 31 and 41 C";

            Temperature = value;
            injuryTimer = 0f;
            return Status() + (russian ? " (естественное изменение температуры включено)" :
                " (natural temperature changes are active)");
        }

        private static string Status()
        {
            string body = Temperature.ToString("0.0", CultureInfo.InvariantCulture);
            string wet = Mathf.RoundToInt(Wetness).ToString(CultureInfo.InvariantCulture);
            string air = outsideTemperature.ToString("0.0", CultureInfo.InvariantCulture);
            string humidity = Mathf.RoundToInt(airHumidity).ToString(CultureInfo.InvariantCulture);
            string feels = sunHeatIndex.ToString("0.0", CultureInfo.InvariantCulture);
            return Localizer.F("bodytemp-status", body, wet, air, humidity, feels,
                Mathf.RoundToInt(MovementMultiplier * 100f),
                Localizer.T(NauseaIntensity > 0f ? "bodytemp-nausea-yes" : "bodytemp-nausea-no"));
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
            Vector3 position = player.transform.position + Vector3.up * 0.9f;
            float maxInfluence = 0f;
            foreach (HeatSource source in HeatSources)
            {
                if (source.Block == null || !IsHeatActive(source)) continue;
                Vector3 heatPosition = source.HeatPoint != null ? source.HeatPoint.position :
                    source.Block.transform.position + Vector3.up * 0.8f;
                float sqrDistance = (heatPosition - position).sqrMagnitude;
                if (sqrDistance >= source.Radius * source.Radius) continue;
                float proximity = 1f - Mathf.Sqrt(sqrDistance) / source.Radius;
                maxInfluence = Mathf.Max(maxInfluence, proximity * source.Strength);
            }
            return maxInfluence;
        }

        private static bool IsHeatActive(HeatSource source)
        {
            if (source.Block == null || !source.Block.gameObject.activeInHierarchy) return false;
            if (source.Fuel != null) return source.Fuel.IsBurning();
            if (source.IsTorch)
                return source.FlameLight == null || source.FlameLight.isActiveAndEnabled;
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
            bool brazier = name.Contains("brazier") || name.Contains("firebasket") ||
                name.Contains("fire_basket") ||
                (name.Contains("basket") && (name.Contains("torch") || name.Contains("fire")));
            bool torch = !brazier && name.Contains("torch");
            bool cooking = block is Block_CookingStand || name.Contains("cooking") ||
                name.Contains("cookpot") || name.Contains("grill") ||
                name.Contains("campfire") || name.Contains("fireplace") ||
                name.Contains("bonfire") || name.Contains("stove");

            // Пламя определяется по компоненту, если название блока не содержит тип источника
            LightFlickering flameLight = null;
            if (!torch && !brazier && !cooking)
            {
                flameLight = block.GetComponentInChildren<LightFlickering>(true);
                if (flameLight != null) torch = true;
                else cooking = block.GetComponentInChildren<CookingSlot>(true) != null;
            }
            if (!torch && !brazier && !cooking) return null;

            Fuel fuel = null;
            Block_CookingStand stand = block as Block_CookingStand;
            if (stand != null) fuel = stand.fuel;
            if (fuel == null) fuel = block.GetComponentInChildren<Fuel>(true);
            if ((torch || brazier) && flameLight == null)
                flameLight = block.GetComponentInChildren<LightFlickering>(true);
            ParticleSystem flame = !torch && !brazier && fuel == null ?
                block.GetComponentInChildren<ParticleSystem>(true) : null;
            if (fuel == null && !torch && !brazier && flame == null) return null;
            return new HeatSource
            {
                Block = block,
                Fuel = fuel,
                CookingFlame = flame,
                FlameLight = flameLight,
                HeatPoint = flameLight != null ? flameLight.transform : null,
                IsTorch = torch || brazier,
                Radius = brazier ? BrazierRadius : torch ? TorchRadius : HeatRadius,
                Strength = brazier ? 1.5f : torch ? 0.9f : 1f
            };
        }

        private static string ListHeatSources()
        {
            bool russian = Localizer.Code == "ru";
            if (currentPlayer == null) return russian ? "Игрок не найден" : "No local player";
            RefreshHeatSources(currentPlayer);
            nextHeatScan = Time.unscaledTime + HeatScanInterval;
            if (HeatSources.Count == 0)
                return russian ? "В радиусе 10 м не найдено распознанных костров, котлов, грилей или факелов" :
                    "No recognized campfire, cooking pot, grill or torch within 10 m";
            var result = new System.Text.StringBuilder(russian ? "Источники тепла рядом: " : "Nearby heat sources: ");
            int count = 0;
            foreach (HeatSource source in HeatSources)
            {
                if (count++ >= 7) break;
                if (count > 1) result.Append("; ");
                result.Append(source.Block.gameObject.name);
                result.Append(IsHeatActive(source) ? (russian ? " [ГОРИТ]" : " [ON]") :
                    (russian ? " [НЕ ГОРИТ]" : " [OFF]"));
                result.Append(" ");
                result.Append(Vector3.Distance(source.Block.transform.position,
                    currentPlayer.transform.position).ToString("0.0", CultureInfo.InvariantCulture));
                result.Append(russian ? " м" : "m");
            }
            return result.ToString();
        }

        private static string InspectNearbyBlocks()
        {
            bool russian = Localizer.Code == "ru";
            if (currentPlayer == null) return russian ? "Игрок не найден" : "No local player";
            List<Block> blocks = BlockCreator.GetPlacedBlocks();
            if (blocks == null) return russian ? "Построенные объекты не найдены" : "No placed blocks";
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
            if (nearest.Count == 0) return russian ? "В радиусе 6 м нет установленных объектов" :
                "No placed objects within 6m";
            var result = new System.Text.StringBuilder(russian ? "Объекты рядом: " : "Objects near player: ");
            for (int i = 0; i < nearest.Count && i < 8; i++)
            {
                Block block = nearest[i];
                if (i > 0) result.Append("; ");
                result.Append(block.gameObject.name);
                Fuel fuel = block.GetComponentInChildren<Fuel>(true);
                if (fuel != null) result.Append(fuel.IsBurning() ?
                    (russian ? " [ТОПЛИВО ГОРИТ]" : " [FUEL ON]") :
                    (russian ? " [ТОПЛИВО НЕ ГОРИТ]" : " [FUEL OFF]"));
                if (block.GetComponentInChildren<CookingSlot>(true) != null)
                    result.Append(russian ? " [ГОТОВКА]" : " [COOK]");
                result.Append(" ");
                result.Append(Vector3.Distance(block.transform.position, position)
                    .ToString("0.0", CultureInfo.InvariantCulture));
                result.Append(russian ? " м" : "m");
            }
            return result.ToString();
        }

        private static bool IsUnderShelter(Network_Player player)
        {
            Transform eye = player.PlayerHeadBone != null ? player.PlayerHeadBone : player.CameraTransform;
            Vector3 start = eye != null ? eye.position : player.transform.position + Vector3.up;
            return Physics.Raycast(start + Vector3.up * 0.15f, Vector3.up, 30f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }

        private static bool CheckShade(Network_Player player, bool underShelter)
        {
            if (daylight < 0.02f) return false;
            Light sun = RenderSettings.sun;
            if (sun == null || !sun.isActiveAndEnabled || sun.intensity <= 0f)
                return underShelter;
            Vector3 toSun = -sun.transform.forward;
            if (Vector3.Dot(toSun, Vector3.up) < 0.1f) return false;

            int coveredSamples;
            CountShadeHits(player, sun, toSun, out coveredSamples);
            return coveredSamples >= ShadeRequiredSamples;
        }

        private static void AddShadeRenderer(Renderer renderer, Vector3 position)
        {
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy ||
                renderer.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.Off ||
                renderer is ParticleSystemRenderer || renderer.transform.IsChildOf(currentPlayer.transform))
                return;

            Bounds bounds = renderer.bounds;
            Vector3 size = bounds.size;
            if (size.x < 0.05f || size.z < 0.05f || size.y < 0.05f ||
                size.x > 65f || size.z > 65f || size.y > 65f)
                return;
            float dx = bounds.center.x - position.x;
            float dz = bounds.center.z - position.z;
            float searchRadius = ShadeRenderRadius + Mathf.Max(size.x, size.z) * 0.5f;
            if (dx * dx + dz * dz > searchRadius * searchRadius) return;

            if (ShadeRendererSet.Add(renderer)) ShadeRenderers.Add(renderer);
        }

        private static void RefreshShadeRenderers(Network_Player player)
        {
            if (player == null) return;
            Vector3 position = player.transform.position;
            float now = Time.unscaledTime;
            Vector3 delta = position - lastShadeRendererPosition;
            if (now < nextShadeRendererRefresh &&
                delta.x * delta.x + delta.z * delta.z < 12f * 12f)
                return;

            nextShadeRendererRefresh = now + 8f;
            lastShadeRendererPosition = position;
            ShadeRenderers.Clear();
            ShadeRendererSet.Clear();

            // Геометрия деревьев часто отбрасывает тени без физических коллайдеров
            if (WorldManager.AllLandmarks != null)
            {
                foreach (Landmark landmark in WorldManager.AllLandmarks)
                {
                    if (landmark == null || !landmark.gameObject.activeInHierarchy) continue;
                    Vector3 offset = landmark.transform.position - position;
                    if (offset.x * offset.x + offset.z * offset.z > 250f * 250f) continue;
                    Renderer[] renderers = landmark.GetComponentsInChildren<Renderer>(true);
                    foreach (Renderer renderer in renderers)
                        AddShadeRenderer(renderer, position);
                }
            }

            // Отдельно посаженные деревья и постройки не всегда принадлежат Landmark
            int count = Physics.OverlapSphereNonAlloc(position, ShadeRenderRadius,
                ShadeNearbyColliders, ~0, QueryTriggerInteraction.Ignore);
            var visitedRoots = new HashSet<Transform>();
            for (int i = 0; i < count; i++)
            {
                Collider collider = ShadeNearbyColliders[i];
                if (collider == null || collider.transform.IsChildOf(player.transform)) continue;

                Transform root = collider.transform;
                Block buildingBlock = collider.GetComponentInParent<Block>();
                if (buildingBlock != null)
                    root = buildingBlock.transform;
                else
                {
                    Landmark landmark = collider.GetComponentInParent<Landmark>();
                    if (landmark != null) continue;
                    if (root.parent != null) root = root.parent;
                }
                if (!visitedRoots.Add(root)) continue;
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                    AddShadeRenderer(renderer, position);
            }
        }

        private static ShadeMeshData GetShadeMeshData(Mesh mesh)
        {
            if (mesh == null) return null;
            ShadeMeshData data;
            if (ShadeMeshCache.TryGetValue(mesh, out data)) return data;

            if (ShadeMeshCache.Count >= 128) ShadeMeshCache.Clear();
            if (mesh.isReadable)
            {
                try
                {
                    int[] triangles = mesh.triangles;
                    if (triangles.Length > 0 && triangles.Length / 3 <= ShadeMaxMeshTriangles)
                    {
                        data = new ShadeMeshData
                        {
                            Vertices = mesh.vertices,
                            Triangles = triangles
                        };
                    }
                }
                catch (UnityException) { }
            }
            ShadeMeshCache[mesh] = data;
            return data;
        }

        private static bool IsThinVegetation(Renderer renderer)
        {
            string name = renderer.name;
            return name.IndexOf("bamboo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("reed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("grass", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("cane", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool RayIntersectsMesh(Ray worldRay, Renderer renderer,
            ShadeMeshData data, float maxDistance, out float hitDistance)
        {
            hitDistance = maxDistance;
            Matrix4x4 inverse = renderer.transform.worldToLocalMatrix;
            Vector3 origin = inverse.MultiplyPoint3x4(worldRay.origin);
            Vector3 direction = inverse.MultiplyVector(worldRay.direction);
            Vector3[] vertices = data.Vertices;
            int[] triangles = data.Triangles;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 v0 = vertices[triangles[i]];
                Vector3 edge1 = vertices[triangles[i + 1]] - v0;
                Vector3 edge2 = vertices[triangles[i + 2]] - v0;
                Vector3 p = Vector3.Cross(direction, edge2);
                float determinant = Vector3.Dot(edge1, p);
                if (Mathf.Abs(determinant) < 0.000001f) continue;
                float reciprocal = 1f / determinant;
                Vector3 offset = origin - v0;
                float u = Vector3.Dot(offset, p) * reciprocal;
                if (u < 0f || u > 1f) continue;
                Vector3 q = Vector3.Cross(offset, edge1);
                float v = Vector3.Dot(direction, q) * reciprocal;
                if (v < 0f || u + v > 1f) continue;
                float distance = Vector3.Dot(edge2, q) * reciprocal;
                if (distance > 0.08f && distance < maxDistance)
                {
                    hitDistance = distance;
                    return true;
                }
            }
            return false;
        }

        private static bool TryRendererShade(Ray ray, Light sun)
        {
            float nearestDistance = ShadeRayLength;
            bool found = false;
            int trianglesRemaining = ShadeTriangleBudgetPerRay;
            for (int i = 0; i < ShadeRenderers.Count; i++)
            {
                Renderer renderer = ShadeRenderers[i];
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy ||
                    renderer.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.Off ||
                    (sun.cullingMask & (1 << renderer.gameObject.layer)) == 0 ||
                    IsThinVegetation(renderer))
                    continue;

                float boundsDistance;
                if (!renderer.bounds.IntersectRay(ray, out boundsDistance) ||
                    boundsDistance > nearestDistance || boundsDistance > ShadeRayLength)
                    continue;

                // Bounds используются только для предварительного отсечения, не как доказательство тени
                MeshRenderer meshRenderer = renderer as MeshRenderer;
                if (meshRenderer == null) continue;
                MeshFilter meshFilter = meshRenderer.GetComponent<MeshFilter>();
                if (meshFilter == null) continue;
                ShadeMeshData data = GetShadeMeshData(meshFilter.sharedMesh);
                if (data == null) continue;
                int triangleCount = data.Triangles.Length / 3;
                if (triangleCount > trianglesRemaining) continue;
                trianglesRemaining -= triangleCount;

                float hitDistance;
                if (!RayIntersectsMesh(ray, renderer, data, nearestDistance,
                    out hitDistance)) continue;
                found = true;
                nearestDistance = hitDistance;
            }
            return found;
        }

        private static void CountShadeHits(Network_Player player, Light sun, Vector3 toSun,
            out int coveredSamples)
        {
            RefreshShadeRenderers(player);
            Transform eye = player.PlayerHeadBone != null ? player.PlayerHeadBone : player.CameraTransform;
            Vector3 center = eye != null ? eye.position : player.transform.position + Vector3.up * 1.6f;
            coveredSamples = 0;
            for (int i = 0; i < ShadeSampleCount; i++)
            {
                Vector2 offset = ShadeSampleOffsets[i];
                Vector3 start = center + Vector3.up * 0.12f +
                    new Vector3(offset.x, 0f, offset.y) + toSun * 0.20f;
                Ray ray = new Ray(start, toSun);

                int count = Physics.RaycastNonAlloc(start, toSun, ShadeRayHits,
                    ShadeRayLength, ~0, QueryTriggerInteraction.Ignore);
                bool physicsBlocked = false;
                for (int j = 0; j < count; j++)
                {
                    Collider candidate = ShadeRayHits[j].collider;
                    if (candidate == null || candidate.transform.IsChildOf(player.transform) ||
                        candidate.gameObject.layer == LayerMask.NameToLayer("Water")) continue;
                    physicsBlocked = true;
                    break;
                }

                bool geometryBlocked = TryRendererShade(ray, sun);
                if (physicsBlocked || geometryBlocked) coveredSamples++;
            }
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
            nauseaOverlay = CreateImage(overlayRoot.transform, "Nausea overlay", "hot");
            nauseaOverlay.color = new Color(0.62f, 0.67f, 0.38f, 0f);
            CreateNauseaDistortion();
            overlayRoot.SetActive(false);
        }

        private static void CreateNauseaDistortion()
        {
            distortionImages = new RawImage[DistortionBands];
            for (int i = 0; i < distortionImages.Length; i++)
            {
                var go = new GameObject("Nausea wave " + i, typeof(RectTransform), typeof(RawImage));
                RectTransform rect = (RectTransform)go.transform;
                rect.SetParent(overlayRoot.transform, false);
                rect.anchorMin = new Vector2(0f, (float)i / DistortionBands);
                rect.anchorMax = new Vector2(1f, (float)(i + 1) / DistortionBands);
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                RawImage image = go.GetComponent<RawImage>();
                image.color = new Color(1f, 1f, 1f, 0f);
                image.raycastTarget = false;
                image.enabled = false;
                distortionImages[i] = image;
            }
        }

        private static void UpdateNauseaDistortion(float now, float amount)
        {
            if (distortionImages == null) return;
            if (!NeedsNauseaCapture || !isInWorld)
            {
                StopNauseaCapture();
                return;
            }
            if (nauseaCapture == null)
            {
                if (now < nextNauseaCameraCheck) return;
                nextNauseaCameraCheck = now + 1f;
                Camera camera = Camera.main;
                if (camera == null || !camera.isActiveAndEnabled) return;
                nauseaCapture = camera.gameObject.AddComponent<TemperatureNauseaCapture>();
            }
            if (!nauseaCapture.enabled) nauseaCapture.enabled = true;
            RenderTexture frame = nauseaCapture.CapturedFrame;
            float strength = Mathf.Clamp01(amount / 1.5f);
            for (int i = 0; i < distortionImages.Length; i++)
            {
                RawImage image = distortionImages[i];
                if (frame == null)
                {
                    image.enabled = false;
                    continue;
                }
                float y = (float)i / DistortionBands;
                float wave = Mathf.Sin(now * 3.2f + y * 13f) * 0.011f * strength;
                float drift = Mathf.Sin(now * 1.1f + y * 8f) * 0.003f * strength;
                if (image.texture != frame) image.texture = frame;
                image.uvRect = new Rect(wave, y + drift, 1f, 1f / DistortionBands);
                image.color = new Color(1f, 1f, 1f, 0.68f * strength);
                image.enabled = true;
                distortionVisible = true;
            }
        }

        private static void StopNauseaCapture()
        {
            if (!distortionVisible && nauseaCapture == null) return;
            distortionVisible = false;
            if (distortionImages != null)
                for (int i = 0; i < distortionImages.Length; i++)
                {
                    if (distortionImages[i] == null) continue;
                    distortionImages[i].enabled = false;
                    distortionImages[i].texture = null;
                }
            // Обработчик изображения полностью убирается с игровой камеры после перегрева
            if (nauseaCapture != null)
            {
                nauseaCapture.enabled = false;
                UnityEngine.Object.Destroy(nauseaCapture);
                nauseaCapture = null;
                nextNauseaCameraCheck = Time.unscaledTime + 0.3f;
            }
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

        private static bool HasVisibleOverlay(Image image)
        {
            return image != null && image.enabled && image.color.a > 0.002f;
        }

        private static void UpdateOverlay(Image image, float intensity, float blend)
        {
            if (image == null || (!image.enabled && intensity <= 0f)) return;
            float maxAlpha = image.sprite != null ? 0.88f : 0.16f;
            Color color = image.color;
            float alpha = Mathf.Lerp(color.a, intensity * maxAlpha, blend);
            if (intensity <= 0f && alpha < 0.002f)
            {
                SetOverlayAlpha(image, 0f);
                return;
            }
            // Во время затухания нельзя прекращать обновление из-за маленькой разницы альфы
            if (intensity > 0f && Mathf.Abs(alpha - color.a) < 0.001f) return;
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
            ColdBiomeSystem.Reset();
            StopNauseaCapture();
            SetOverlayAlpha(coldOverlay, 0f);
            SetOverlayAlpha(hotOverlay, 0f);
            SetOverlayAlpha(nauseaOverlay, 0f);
            if (overlayRoot != null && overlayRoot.activeSelf)
                overlayRoot.SetActive(false);
            nextNauseaCameraCheck = 0f;
            currentPlayer = null;
            isInWorld = false;
            deathLatched = false;
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
            waterExposureSeconds = 0f;
            waterHeatRelief = 0f;
            sunHeatIndex = 26f;
            HeatSources.Clear();
            CachedHeatSources.Clear();
            CachedNonHeatBlocks.Clear();
            nextClassificationReset = 0f;
            simulationTime = 0f;
            airHeatIndex = 26f;
            nextHeatScan = 0f;
            nextShadeCheck = 0f;
            nextShadeRendererRefresh = 0f;
            lastShadeRendererPosition = Vector3.zero;
            ShadeRenderers.Clear();
            ShadeRendererSet.Clear();
            ShadeMeshCache.Clear();
            nextWeatherCheck = 0f;
        }

        public static void Dispose()
        {
            if (!installed) return;
            PersonController.CameraSubmersionAction -= OnCameraSubmersion;
            if (movementHarmony != null)
            {
                movementHarmony.UnpatchAll(MovementHarmonyId);
                movementHarmony = null;
            }
            installed = false;
            ResetWorldState();
            BodyTemperatureSettings.Flush();
            nextPlayerPoll = 0f;
            if (overlayRoot != null) UnityEngine.Object.Destroy(overlayRoot);
            overlayRoot = null;
            coldOverlay = null;
            hotOverlay = null;
            nauseaOverlay = null;
            distortionImages = null;
        }
    }
}
