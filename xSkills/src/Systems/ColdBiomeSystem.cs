using System;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace xSkills.Systems
{
    internal static class ColdBiomeSystem
    {
        private const float ScanInterval = 2f;
        private const float CoolingRatePerSecond = 1.8f;
        private const float WarmingRatePerSecond = 1.2f;
        private const float MainIslandTemperature = -11f;
        private const float SnowIslandsTemperature = -3f;
        private const float WeatherRegionTemperature = -5f;

        private static float nextScan;
        private static float lastReadTime = -1f;
        private static float currentTemperatureOffset;
        private static float targetStrength;
        private static float targetColdTemperature;
        private static string detectionSource = "none";

        internal static float Apply(float tropicalTemperature, float daylight, out bool coldBiome)
        {
            coldBiome = false;
            if (!Raft_Network.WorldHasBeenRecieved)
            {
                Reset();
                return tropicalTemperature;
            }

            float now = Time.unscaledTime;
            if (now >= nextScan || now < lastReadTime)
            {
                nextScan = now + ScanInterval;
                Scan();
            }

            float desiredOffset = 0f;
            if (targetStrength > 0f)
            {
                float nightCooling = Mathf.Lerp(-3f, 0f, Mathf.Clamp01(daylight));
                float coldTemperature = targetColdTemperature + nightCooling;
                desiredOffset = (coldTemperature - tropicalTemperature) * targetStrength;
            }

            if (lastReadTime < 0f || now < lastReadTime)
                currentTemperatureOffset = desiredOffset;
            else
            {
                float elapsed = Mathf.Clamp(now - lastReadTime, 0f, 2f);
                float speed = desiredOffset < currentTemperatureOffset
                    ? CoolingRatePerSecond : WarmingRatePerSecond;
                currentTemperatureOffset = Mathf.MoveTowards(
                    currentTemperatureOffset, desiredOffset, speed * elapsed);
            }

            lastReadTime = now;
            coldBiome = targetStrength > 0.08f || currentTemperatureOffset < -1.5f;
            return tropicalTemperature + currentTemperatureOffset;
        }

        private static void Scan()
        {
            targetStrength = 0f;
            targetColdTemperature = 0f;
            detectionSource = "none";

            Network_Player player = ComponentManager<Network_Player>.Value;
            if (player == null) return;
            Vector3 position = player.transform.position;

            // Регионы просматриваются не чаще одного раза за 2 секунды
            if (RegionManager.AllRegions != null)
                foreach (Region region in RegionManager.AllRegions)
                {
                    if (region == null || !region.gameObject.activeInHierarchy) continue;
                    bool main = ContainsColdName(region.transform, true);
                    bool snow = !main && ContainsColdName(region.transform, false);

                    if (region.subregions != null)
                        foreach (Subregion subregion in region.subregions)
                        {
                            if (subregion == null) continue;
                            bool subMain = ContainsColdName(subregion.transform, true);
                            bool subSnow = !subMain && ContainsColdName(subregion.transform, false);
                            SubRegionComponent_Weather weather =
                                subregion.GetSubRegionComponent<SubRegionComponent_Weather>();
                            bool subPool = weather != null && IsColdName(weather.weatherPoolType.ToString(), false);

                            if (subMain || subSnow || subPool)
                            {
                                float strength = AreaStrength(subregion.regionArea, position, 350f);
                                Consider(strength, subMain ? MainIslandTemperature :
                                    subSnow ? SnowIslandsTemperature : WeatherRegionTemperature,
                                    "subregion:" + subregion.name + (weather != null ?
                                    "/" + weather.weatherPoolType : ""));
                            }
                        }

                    if (main || snow)
                    {
                        float strength = AreaStrength(region.regionArea, position, 550f);
                        Consider(strength, main ? MainIslandTemperature : SnowIslandsTemperature,
                            "region:" + region.name);
                    }
                }

            // Маленькие снежные острова могут существовать вне погодного подрегиона
            if (WorldManager.AllLandmarks != null)
                foreach (Landmark landmark in WorldManager.AllLandmarks)
                {
                    if (landmark == null || !landmark.isSpawned ||
                        !landmark.gameObject.activeInHierarchy) continue;
                    bool main = ContainsColdName(landmark.transform, true);
                    if (!main && !ContainsColdName(landmark.transform, false)) continue;
                    float radius = main ? 650f : 95f;
                    float falloff = main ? 650f : 210f;
                    float strength = DistanceStrength(position, landmark.transform.position,
                        radius, falloff);
                    Consider(strength, main ? MainIslandTemperature : SnowIslandsTemperature,
                        "landmark:" + landmark.name);
                }

            WeatherManager wm = ComponentManager<WeatherManager>.Value;
            if (wm != null)
            {
                string weatherType = wm.GetCurrentWeatherType().ToString();
                Weather weather = wm.GetCurrentWeather();
                string weatherName = weather != null ? weather.name : string.Empty;
                if (IsColdName(weatherType, false) || IsColdName(weatherName, false))
                    Consider(1f, WeatherRegionTemperature, "weather:" + weatherType + "/" + weatherName);
            }
        }

        private static void Consider(float strength, float temperature, string source)
        {
            if (strength <= 0f) return;
            strength = Mathf.Clamp01(strength);
            // Выбирается наиболее сильное охлаждение при пересечении зон
            float score = strength * (30f - temperature);
            float currentScore = targetStrength * (30f - targetColdTemperature);
            if (score <= currentScore) return;
            targetStrength = strength;
            targetColdTemperature = temperature;
            detectionSource = source;
        }

        private static float AreaStrength(AreaZone area, Vector3 position, float approachDistance)
        {
            if (area == null || !area.gameObject.activeInHierarchy) return 0f;
            if (area.GetNormalizedDistanceToPoint(position) > 0f) return 1f;

            // AreaZone сообщает только 0 или 1, поэтому приближение оценивается по размеру зоны
            Vector3 center = area.GetCenterOfArea();
            Vector3 scale = area.transform.lossyScale;
            float radius = Mathf.Clamp(Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z)),
                1f, 1800f);
            return DistanceStrength(position, center, radius, approachDistance);
        }

        private static float DistanceStrength(Vector3 position, Vector3 center,
            float radius, float falloff)
        {
            float dx = position.x - center.x;
            float dz = position.z - center.z;
            float beyond = Mathf.Max(0f, Mathf.Sqrt(dx * dx + dz * dz) - radius);
            return 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(beyond / falloff));
        }

        private static bool ContainsColdName(Transform transform, bool mainOnly)
        {
            int parents = 0;
            while (transform != null && parents++ < 5)
            {
                if (IsColdName(transform.name, mainOnly)) return true;
                transform = transform.parent;
            }
            return false;
        }

        private static bool IsColdName(string name, bool mainOnly)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string n = name.ToLowerInvariant();
            if (n.Contains("temperance")) return true;
            if (mainOnly) return false;
            return n.Contains("arctic") || n.Contains("antarctic") ||
                n.Contains("snow") || n.Contains("glacier") ||
                n.Contains("blizzard") || n.Contains("frozen") ||
                n.Contains("polar") || n.Contains("iceberg") ||
                n.Contains("iceisland") || n.Contains("ice_island") ||
                n.Contains("ice island") || n.Contains("ice-island") ||
                n.Contains("icefloe") || n.Contains("ice_floe") ||
                n.Contains("ice floe") || n.Contains("_ice_") ||
                n.StartsWith("ice_", StringComparison.Ordinal) ||
                n.EndsWith("_ice", StringComparison.Ordinal) || n == "ice";
        }

        internal static string Diagnostic()
        {
            Scan();
            StringBuilder result = new StringBuilder();
            result.Append("Cold biome: ")
                .Append((targetStrength * 100f).ToString("0", CultureInfo.InvariantCulture))
                .Append("%, source: ").Append(detectionSource);
            result.Append(" | regions: ");
            if (RegionManager.CurrentRegions != null)
                foreach (Region region in RegionManager.CurrentRegions)
                    if (region != null) result.Append(region.name).Append("; ");
            result.Append(" | weather pools: ");
            if (RegionManager.CurrentSubregions != null)
                foreach (Subregion subregion in RegionManager.CurrentSubregions)
                {
                    if (subregion == null) continue;
                    SubRegionComponent_Weather weather =
                        subregion.GetSubRegionComponent<SubRegionComponent_Weather>();
                    result.Append(subregion.name).Append('=')
                        .Append(weather != null ? weather.weatherPoolType.ToString() : "none")
                        .Append("; ");
                }
            result.Append(" | landmarks: ");
            Network_Player player = ComponentManager<Network_Player>.Value;
            if (player != null && WorldManager.AllLandmarks != null)
            {
                int listed = 0;
                Vector3 position = player.transform.position;
                foreach (Landmark landmark in WorldManager.AllLandmarks)
                {
                    if (landmark == null || !landmark.isSpawned) continue;
                    Vector3 offset = landmark.transform.position - position;
                    if (offset.x * offset.x + offset.z * offset.z > 1800f * 1800f) continue;
                    result.Append(landmark.name).Append("; ");
                    if (++listed >= 6) break;
                }
            }
            return result.ToString();
        }

        internal static void Reset()
        {
            nextScan = 0f;
            lastReadTime = -1f;
            currentTemperatureOffset = 0f;
            targetStrength = 0f;
            targetColdTemperature = 0f;
            detectionSource = "none";
        }
    }
}
