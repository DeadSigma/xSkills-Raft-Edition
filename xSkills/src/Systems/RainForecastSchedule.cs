using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using UnityEngine;
using UnityEngine.AzureSky;
using xSkills.Services;

namespace xSkills.Systems
{
    // Расписание заранее создаётся хостом и применяется при смене погоды
    internal static class RainForecastSchedule
    {
        private const double HoursPerDay = 24d;
        private const double LookAheadHours = 12d * HoursPerDay;
        private const double TimeToleranceHours = 0.065d;
        private const int MaxSegments = 1024;
        private static readonly DataContractJsonSerializer Serializer =
            new DataContractJsonSerializer(typeof(SavedPlan));

        private sealed class Segment
        {
            internal Weather Weather;
            internal double Start;
            internal double End;
            internal float Duration;
        }

        [DataContract]
        private sealed class SavedPlan
        {
            [DataMember(Order = 1)] public int Version = 2;
            [DataMember(Order = 2)] public int Pool;
            [DataMember(Order = 3)] public int Index;
            [DataMember(Order = 4)] public List<SavedSegment> Segments;
            [DataMember(Order = 5)] public int SettingsSignature;
        }

        [DataContract]
        private sealed class SavedSegment
        {
            [DataMember(Order = 1)] public string Name;
            [DataMember(Order = 2)] public int Type;
            [DataMember(Order = 3)] public double Start;
            [DataMember(Order = 4)] public double End;
            [DataMember(Order = 5)] public float Duration;
        }

        private static readonly List<Segment> segments = new List<Segment>();
        private static Guid worldId;
        private static WeatherManager manager;
        private static AzureSkyController sky;
        private static int currentIndex;
        private static int poolId;
        private static int pendingIndex = -1;
        private static double previousHour = double.NaN;
        private static bool loadAttempted;
        private static int revision;
        private static int activeSettingsSignature;

        internal static int Revision { get { return revision; } }

        private static bool IsHostReady()
        {
            return Raft_Network.IsHost && Raft_Network.WorldHasBeenRecieved &&
                SaveAndLoad.WorldGuid != Guid.Empty;
        }

        private static double Now()
        {
            return WorldManager.DayCounter * HoursPerDay + sky.timeOfDay.hour;
        }

        private static float HoursPerSecond()
        {
            return sky.timeOfDay.GetDayLength();
        }

        internal static void Tick()
        {
            if (!IsHostReady() || IsCreative())
            {
                if (manager != null || segments.Count != 0) Reset();
                return;
            }
            WeatherManager active = ComponentManager<WeatherManager>.Value;
            AzureSkyController activeSky = ComponentManager<AzureSkyController>.Value;
            Guid id = SaveAndLoad.WorldGuid;
            if (active == null || activeSky == null || active.GetCurrentWeather() == null) return;
            if (manager != active || sky != activeSky || worldId != id)
            {
                Reset();
                manager = active;
                sky = activeSky;
                worldId = id;
                activeSettingsSignature = SettingsSignature();
            }
            double now = Now();
            if (HoursPerSecond() <= 0f || double.IsNaN(now)) return;
            if (!double.IsNaN(previousHour) && now < previousHour - 0.15d)
                Invalidate();
            previousHour = now;
            int currentSignature = SettingsSignature();
            if (currentSignature != activeSettingsSignature)
            {
                activeSettingsSignature = currentSignature;
                Invalidate();
                loadAttempted = true;
            }

            if (segments.Count == 0)
            {
                if (!loadAttempted)
                {
                    loadAttempted = true;
                    TryLoad();
                }
                if (segments.Count == 0) CreatePlan(now);
            }
            if (segments.Count == 0) return;

            if (pendingIndex >= 0)
            {
                if (manager.currentPoolType.GetHashCode() != poolId)
                {
                    Invalidate();
                    CreatePlan(now);
                    return;
                }
                if (manager.GetCurrentWeather() == segments[pendingIndex].Weather)
                {
                    if (manager.changingWeather) return;
                    currentIndex = pendingIndex;
                    pendingIndex = -1;
                    AlignToRealTimer(now);
                    Save();
                    revision++;
                }
                else if (now > segments[pendingIndex].Start + 0.5d)
                {
                    Invalidate();
                    CreatePlan(now);
                }
                else return;
            }

            if (segments.Count == 0 || manager.changingWeather) return;
            if (manager.currentPoolType.GetHashCode() != poolId ||
                manager.GetCurrentWeather() != segments[currentIndex].Weather)
            {
                Invalidate();
                CreatePlan(now);
                return;
            }

            double expiry = now + Math.Max(0f, manager.WeatherTimer) * HoursPerSecond();
            if (Math.Abs(expiry - segments[currentIndex].End) > TimeToleranceHours)
            {
                // При изменении таймера или скорости времени прогноз пересоздаётся
                Invalidate();
                CreatePlan(now);
                return;
            }
            if (currentIndex > 128)
            {
                segments.RemoveRange(0, currentIndex);
                currentIndex = 0;
                Save();
            }
            if (segments[segments.Count - 1].End < now + LookAheadHours)
            {
                ExtendPlan();
                Save();
                revision++;
            }
        }

        private static void AlignToRealTimer(double now)
        {
            double realEnd = now + Math.Max(0f, manager.WeatherTimer) * HoursPerSecond();
            double drift = realEnd - segments[currentIndex].End;
            segments[currentIndex].Start = now;
            for (int i = currentIndex; i < segments.Count; i++)
            {
                segments[i].End += drift;
                if (i > currentIndex) segments[i].Start += drift;
            }
        }

        private static void CreatePlan(double now)
        {
            Weather current = manager.GetCurrentWeather();
            float rate = HoursPerSecond();
            if (current == null || rate <= 0f || manager.WeatherTimer < 0f) return;
            segments.Clear();
            currentIndex = 0;
            pendingIndex = -1;
            poolId = manager.currentPoolType.GetHashCode();
            segments.Add(new Segment
            {
                Weather = current,
                Start = now,
                End = now + Math.Max(0f, manager.WeatherTimer) * rate
            });
            ExtendPlan();
            Save();
            revision++;
        }

        private static void ExtendPlan()
        {
            if (segments.Count == 0) return;
            WeatherPool pool = GetPool((WeatherPoolType)poolId);
            if (pool == null || pool.randomizer == null || !pool.randomizer.HasItems) return;
            int salt = unchecked((int)(segments[segments.Count - 1].End * 1024d));
            int seed = StableSeed(worldId, poolId, salt, segments.Count);
            UnityEngine.Random.State previousState = UnityEngine.Random.state;
            Dictionary<Randomizer, uint> previousIndices = new Dictionary<Randomizer, uint>();
            CaptureIndices(pool.randomizer, previousIndices);
            try
            {
                UnityEngine.Random.InitState(seed);
                float rate = HoursPerSecond();
                float durationMultiplier = GetDurationMultiplier();
                double horizon = Now() + LookAheadHours;
                for (int n = 0; n < MaxSegments && segments.Count < MaxSegments &&
                    segments[segments.Count - 1].End < horizon; n++)
                {
                    Weather before = segments[segments.Count - 1].Weather;
                    Weather next = ChooseWeather(pool, before);
                    if (next == null) next = manager.startingWeather;
                    if (next == null || next.so_weather == null) break;
                    float duration = next.so_weather.GetRandomDuration;
                    if (IsRain(next))
                    {
                        TimeControlSettings settings = GetRainSettings();
                        if (settings != null)
                        {
                            float variation = settings.RainVariationPercent / 100f;
                            float randomFactor = UnityEngine.Random.Range(1f - variation, 1f + variation);
                            duration *= settings.RainDurationMultiplier * randomFactor;
                        }
                    }
                    duration = Mathf.Max(1f, duration);
                    float seconds = (duration + next.so_weather.GreatestTransitionTime) * durationMultiplier;
                    double start = segments[segments.Count - 1].End;
                    segments.Add(new Segment
                    {
                        Weather = next,
                        Start = start,
                        End = start + seconds * rate,
                        Duration = duration
                    });
                }
            }
            finally
            {
                UnityEngine.Random.state = previousState;
                foreach (KeyValuePair<Randomizer, uint> entry in previousIndices)
                    entry.Key.lastRandomizedIndex = entry.Value;
            }
        }

        // Веса дождливой и сухой погоды изменяются без модификации ванильных пулов
        private static Weather ChooseWeather(WeatherPool pool, Weather previous)
        {
            TimeControlSettings settings = GetRainSettings();
            float frequency = settings == null ? 1f : settings.RainFrequencyPercent / 100f;
            Weather candidate = null;
            for (int i = 0; i < 256; i++)
            {
                candidate = pool.randomizer.GetRandomItem<Weather>();
                if (candidate == null || candidate.so_weather == null || candidate == previous) continue;
                float acceptance = IsRain(candidate) ? Mathf.Min(1f, frequency) :
                    frequency <= 1f ? 1f : 1f / frequency;
                if (UnityEngine.Random.value <= acceptance) return candidate;
            }
            // При полном отключении дождя выбирается сухой вариант, если он доступен
            if (frequency <= 0f)
            {
                Weather[] available = pool.randomizer.GetAllItems<Weather>();
                List<Weather> dry = new List<Weather>();
                foreach (Weather weather in available)
                    if (weather != null && weather.so_weather != null &&
                        weather != previous && !IsRain(weather)) dry.Add(weather);
                if (dry.Count > 0) return dry[UnityEngine.Random.Range(0, dry.Count)];
            }
            return candidate != null && candidate != previous ? candidate : null;
        }

        private static TimeControlSettings GetRainSettings()
        {
            TimeControlSystem control = TimeControlSystem.Instance;
            return control == null ? null : control.Settings;
        }

        private static int SettingsSignature()
        {
            TimeControlSettings settings = GetRainSettings();
            if (settings == null) return 0;
            unchecked
            {
                int hash = 23;
                hash = hash * 31 + Mathf.RoundToInt(settings.RainFrequencyPercent);
                hash = hash * 31 + Mathf.RoundToInt(settings.RainDurationMultiplier * 100f);
                hash = hash * 31 + Mathf.RoundToInt(settings.RainVariationPercent);
                hash = hash * 31 + Mathf.RoundToInt(settings.WeatherDurationMultiplier * 100f);
                hash = hash * 31 + Mathf.RoundToInt(settings.DayLengthSeconds);
                return hash;
            }
        }

        internal static void SettingsChanged()
        {
            // Текущая погода не прерывается - пересоздаются только будущие события
            Invalidate();
            loadAttempted = true;
            activeSettingsSignature = SettingsSignature();
        }

        private static void CaptureIndices(Randomizer randomizer,
            Dictionary<Randomizer, uint> previous)
        {
            if (randomizer == null || previous.ContainsKey(randomizer)) return;
            previous.Add(randomizer, randomizer.lastRandomizedIndex);
            if (randomizer.items == null) return;
            foreach (RandomItem item in randomizer.items)
            {
                if (item == null) continue;
                SO_RandomizerCategory category = item.obj as SO_RandomizerCategory;
                if (category != null) CaptureIndices(category.randomizer, previous);
            }
        }

        private static bool IsCreative()
        {
            return GameModeValueManager.GetCurrentGameModeValue().gameMode == GameMode.Creative;
        }

        private static float GetDurationMultiplier()
        {
            TimeControlSystem system = TimeControlSystem.Instance;
            return system == null || system.Settings == null ? 1f :
                system.Settings.WeatherDurationMultiplier;
        }

        private static int StableSeed(Guid id, int pool, int salt, int count)
        {
            unchecked
            {
                int hash = 216613626;
                foreach (byte value in id.ToByteArray()) hash = (hash ^ value) * 16777619;
                return ((hash ^ pool) * 16777619 ^ salt) * 16777619 ^ count;
            }
        }

        private static WeatherPool GetPool(WeatherPoolType type)
        {
            if (manager == null || manager.weatherPools == null) return null;
            foreach (WeatherPool item in manager.weatherPools)
                if (item != null && item.poolType == type) return item;
            return null;
        }

        internal static bool SelectNext(WeatherManager target, WeatherPoolType pool,
            bool excludeCurrent, ref Weather value)
        {
            if (!IsHostReady() || IsCreative() || target != manager || segments.Count == 0) return false;
            if (pendingIndex >= 0) return false;
            if (pool.GetHashCode() != poolId) return false;
            int next = currentIndex + 1;
            if (next >= segments.Count) return false;
            Segment scheduled = segments[next];
            if (scheduled.Weather == null) return false;
            pendingIndex = next;
            value = scheduled.Weather;
            return true;
        }

        internal static bool UseScheduledDuration(SO_Weather asset, ref float seconds)
        {
            if (!IsHostReady() || pendingIndex < 0 || pendingIndex >= segments.Count) return false;
            Segment planned = segments[pendingIndex];
            if (planned.Weather == null || planned.Weather.so_weather != asset) return false;
            seconds = planned.Duration;
            return true;
        }

        private struct RainWindow
        {
            internal double Start;
            internal double End;
        }

        // Для сегодняшнего дня учитывается только оставшаяся часть суток
        internal static bool TryGetRainDescription(int day, out bool rain, out string detail)
        {
            rain = false;
            detail = string.Empty;
            if (!IsHostReady() || day < WorldManager.DayCounter) return false;
            if (IsCreative())
            {
                WeatherManager creativeWeather = ComponentManager<WeatherManager>.Value;
                if (creativeWeather == null) return false;
                rain = IsRain(creativeWeather.GetCurrentWeather());
                if (rain) detail = Localizer.T("forecast-rain-current");
                return true;
            }
            if (manager == null || segments.Count == 0) return false;
            double firstHour = Math.Max(Now(), day * HoursPerDay);
            double lastHour = ((double)day + 1d) * HoursPerDay;
            if (lastHour <= firstHour || segments[segments.Count - 1].End < lastHour) return false;
            List<RainWindow> windows = new List<RainWindow>();
            double covered = firstHour;
            for (int i = currentIndex; i < segments.Count && covered < lastHour; i++)
            {
                Segment item = segments[i];
                if (item.End <= covered || item.Start >= lastHour) continue;
                if (item.Start > covered + 0.001d) return false;
                double from = Math.Max(covered, item.Start);
                double to = Math.Min(lastHour, item.End);
                if (IsRain(item.Weather) && to > from)
                {
                    if (windows.Count > 0 && Math.Abs(windows[windows.Count - 1].End - from) < 0.001d)
                    {
                        RainWindow merged = windows[windows.Count - 1];
                        merged.End = to;
                        windows[windows.Count - 1] = merged;
                    }
                    else windows.Add(new RainWindow { Start = from, End = to });
                }
                covered = to;
            }
            if (covered < lastHour - 0.001d) return false;
            rain = windows.Count > 0;
            if (!rain) return true;
            if (windows.Count > 1)
            {
                detail = Localizer.T("forecast-rain-intermittent");
                return true;
            }
            double begin = windows[0].Start - day * HoursPerDay;
            double end = windows[0].End - day * HoursPerDay;
            double visibleStart = firstHour - day * HoursPerDay;
            double duration = end - begin;
            if (begin <= visibleStart + 0.2d && end >= HoursPerDay - 0.2d)
                detail = Localizer.T(day == WorldManager.DayCounter && visibleStart > 0.3d
                    ? "forecast-rain-rest-of-day" : "forecast-rain-all-day");
            else if (duration <= 3d)
                detail = Localizer.F("forecast-rain-short", Localizer.T(PeriodKey((begin + end) / 2d)));
            else if (begin <= visibleStart + 0.2d)
                detail = Localizer.F("forecast-rain-until", Localizer.T(EndPeriodKey(end)));
            else if (end >= HoursPerDay - 0.2d)
                detail = Localizer.F("forecast-rain-late", Localizer.T(PeriodKey(begin)));
            else
                detail = Localizer.F("forecast-rain-range", Localizer.T(PeriodKey(begin)),
                    Localizer.T(EndPeriodKey(end)));
            return true;
        }

        internal static bool TryGetRain(int day, out bool rain)
        {
            string unused;
            return TryGetRainDescription(day, out rain, out unused);
        }

        private static string PeriodKey(double hour)
        {
            if (hour < 5d || hour >= 21d) return "forecast-period-night";
            if (hour < 11d) return "forecast-period-morning";
            if (hour < 17d) return "forecast-period-day";
            return "forecast-period-evening";
        }

        private static string EndPeriodKey(double hour)
        {
            if (hour < 10d) return "forecast-end-morning";
            if (hour < 15d) return "forecast-end-midday";
            if (hour < 21d) return "forecast-end-evening";
            return "forecast-end-night";
        }

        private static bool IsRain(Weather weather)
        {
            if (weather == null || weather.so_weather == null) return false;
            string text = (weather.so_weather.uniqueWeatherType.ToString() + " " +
                weather.name + " " + weather.so_weather.name).ToLowerInvariant();
            return text.Contains("rain") || text.Contains("drizzle") ||
                text.Contains("storm") || text.Contains("thunder");
        }

        private static string StoragePath()
        {
            return Path.Combine(Application.persistentDataPath, "xSkills/weather/" +
                worldId.ToString("N") + ".json");
        }

        private static void Save()
        {
            if (segments.Count == 0 || worldId == Guid.Empty) return;
            try
            {
                SavedPlan plan = new SavedPlan { Pool = poolId, Index = currentIndex,
                    SettingsSignature = SettingsSignature(), Segments = new List<SavedSegment>() };
                foreach (Segment segment in segments)
                {
                    if (segment.Weather == null || segment.Weather.so_weather == null) return;
                    plan.Segments.Add(new SavedSegment
                    {
                        Name = segment.Weather.name,
                        Type = (int)segment.Weather.so_weather.uniqueWeatherType,
                        Start = segment.Start,
                        End = segment.End,
                        Duration = segment.Duration
                    });
                }
                string path = StoragePath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using (FileStream stream = File.Create(path + ".tmp"))
                    Serializer.WriteObject(stream, plan);
                if (File.Exists(path)) File.Delete(path);
                File.Move(path + ".tmp", path);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[xSkills] Rain schedule save failed: " + ex.Message);
            }
        }

        private static void TryLoad()
        {
            try
            {
                string path = StoragePath();
                if (!File.Exists(path)) return;
                SavedPlan plan;
                using (FileStream stream = File.OpenRead(path))
                    plan = Serializer.ReadObject(stream) as SavedPlan;
                if (plan == null || plan.Version != 2 || plan.SettingsSignature != SettingsSignature() || plan.Segments == null ||
                    plan.Segments.Count == 0 || plan.Segments.Count > MaxSegments ||
                    plan.Pool != manager.currentPoolType.GetHashCode()) return;
                if (plan.Index < 0 || plan.Index >= plan.Segments.Count) return;
                List<Segment> loaded = new List<Segment>();
                foreach (SavedSegment entry in plan.Segments)
                {
                    Weather asset = ResolveWeather(entry.Name, entry.Type);
                    if (asset == null || entry.End <= entry.Start) return;
                    loaded.Add(new Segment { Weather = asset, Start = entry.Start,
                        End = entry.End, Duration = entry.Duration });
                }
                double now = Now();
                double expectedEnd = now + Math.Max(0f, manager.WeatherTimer) * HoursPerSecond();
                Segment active = loaded[plan.Index];
                if (active.Weather != manager.GetCurrentWeather() ||
                    now < active.Start - TimeToleranceHours || now > active.End + TimeToleranceHours ||
                    Math.Abs(active.End - expectedEnd) > TimeToleranceHours) return;
                segments.Clear();
                segments.AddRange(loaded);
                currentIndex = plan.Index;
                poolId = plan.Pool;
                revision++;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[xSkills] Rain schedule load failed: " + ex.Message);
            }
        }

        private static Weather ResolveWeather(string name, int type)
        {
            if (manager.weatherPools == null) return null;
            foreach (WeatherPool pool in manager.weatherPools)
            {
                if (pool == null || pool.randomizer == null) continue;
                foreach (Weather candidate in pool.randomizer.GetAllItems<Weather>())
                    if (candidate != null && candidate.so_weather != null &&
                        candidate.name == name && (int)candidate.so_weather.uniqueWeatherType == type)
                        return candidate;
            }
            return null;
        }

        private static void Invalidate()
        {
            segments.Clear();
            pendingIndex = -1;
            currentIndex = 0;
            revision++;
        }

        internal static void Reset()
        {
            Invalidate();
            manager = null;
            sky = null;
            worldId = Guid.Empty;
            loadAttempted = false;
            previousHour = double.NaN;
            activeSettingsSignature = 0;
        }
    }
}
