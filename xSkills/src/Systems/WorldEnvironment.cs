using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.AzureSky;

using xSkills.Services;
namespace xSkills.Systems
{
    internal static class WorldEnvironment
    {
        // Превью календаря начинается первого мая первого года
        private static readonly DateTime CalendarEpoch = new DateTime(1, 5, 1);
        private static readonly CultureInfo RussianCalendar = CultureInfo.GetCultureInfo("ru-RU");
        private static readonly CultureInfo EnglishCalendar = CultureInfo.GetCultureInfo("en-US");
        private const float HoursPerDay = 24f;
        private const float FullCircle = Mathf.PI * 2f;

        internal struct Snapshot
        {
            public string Date;
            public string Clock;
            public string MonthYear;
            public string Weather;
            public float Temperature;
            public float RelativeHumidity;
            public float Daylight;
            public string WeatherCode;
            public string Climate;
        }

        internal struct ThermalSnapshot
        {
            public float Temperature;
            public float RelativeHumidity;
            public float Daylight;
            public bool IsRaining;
        }

        private struct RawEnvironment
        {
            public int Day;
            public DateTime Date;
            public float Hour;
            public string WeatherCode;
            public string NormalizedWeather;
        }

        private enum WeatherKind { Fair, Cloudy, Rain, Storm }
        private enum PeriodKind { None, Heatwave, Cool, SultryNights, Dry, Humid }

        private struct ClimatePeriod
        {
            public PeriodKind Kind;
            public int StartDay;
            public int Duration;
        }

        private struct ClimateState
        {
            public float Heatwave;
            public float Cool;
            public float SultryNights;
            public float Dry;
            public float Humid;
        }

        private struct DayPattern
        {
            public float Amplitude;
            public float Variation;
            public float MiddayVariation;
            public float Season;
            public float HeatOffset;
            public float CoolOffset;
            public float SultryTarget;
        }

        private struct AirState
        {
            public float Temperature;
            public float Humidity;
            public float Daylight;
            public ClimateState Climate;
        }

        private static string lastWeatherCode;
        private static string lastNormalizedWeather;
        private static string lastWeatherKindCode;
        private static WeatherKind lastWeatherKind;
        private static int cachedCalendarDay = int.MinValue;
        private static DateTime cachedCalendarDate;
        private static Weather lastDefaultWeatherObject;
        private static object lastDefaultWeatherProfile;
        private static string lastDefaultWeatherCode;
        private static int cachedPatternDay = int.MinValue;
        private static DayPattern todayPattern;
        private static DayPattern yesterdayPattern;
        private static int cachedExtremeCycle = int.MinValue;
        private static int cachedHumidityCycle = int.MinValue;
        private static int cachedSultryCycle = int.MinValue;
        private static ClimatePeriod extremePeriod;
        private static ClimatePeriod humidityPeriod;
        private static ClimatePeriod sultryPeriod;

        private const int ExtremeCycleDays = 32;
        private const int HumidityCycleDays = 11;
        private const int SultryCycleDays = 19;

        internal static bool TryRead(out Snapshot result)
        {
            result = new Snapshot();
            RawEnvironment raw;
            if (!TryReadRaw(out raw)) return false;

            int minuteOfDay = Mathf.FloorToInt(raw.Hour * 60f);
            result.Date = FormatDate(raw.Date);
            result.Clock = (minuteOfDay / 60).ToString("00") + ":" +
                (minuteOfDay % 60).ToString("00");
            string month = raw.Date.ToString("MMMM", Localizer.Code == "ru" ? RussianCalendar : EnglishCalendar);
            result.MonthYear = Localizer.F("hud-month-year", month, raw.Date.Year);
            result.Weather = FormatWeather(raw.NormalizedWeather);
            result.WeatherCode = raw.WeatherCode;
            AirState climate = CalculateAir(raw.Day, raw.Date.DayOfYear, raw.Hour,
                ClassifyWeather(raw.NormalizedWeather));
            bool coldBiome;
            float localTemperature = ColdBiomeSystem.Apply(climate.Temperature,
                climate.Daylight, out coldBiome);
            result.Temperature = localTemperature;
            result.RelativeHumidity = coldBiome ? Mathf.Lerp(climate.Humidity, 75f,
                Mathf.Clamp01((climate.Temperature - localTemperature) / 35f)) : climate.Humidity;
            result.Daylight = climate.Daylight;
            result.Climate = coldBiome ? (Localizer.Code == "ru" ? "Холодный биом" :
                "Cold biome") : DescribeClimatePeriod(climate.Climate, raw.Hour);
            return true;
        }

        internal static bool TryReadThermal(out ThermalSnapshot result)
        {
            result = new ThermalSnapshot();
            RawEnvironment raw;
            if (!TryReadRaw(out raw)) return false;

            WeatherKind weather = ClassifyWeather(raw.NormalizedWeather);
            AirState climate = CalculateAir(raw.Day, raw.Date.DayOfYear, raw.Hour, weather);
            bool coldBiome;
            result.Temperature = ColdBiomeSystem.Apply(climate.Temperature,
                climate.Daylight, out coldBiome);
            result.RelativeHumidity = coldBiome ? Mathf.Lerp(climate.Humidity, 75f,
                Mathf.Clamp01((climate.Temperature - result.Temperature) / 35f)) : climate.Humidity;
            result.Daylight = climate.Daylight;
            result.IsRaining = weather == WeatherKind.Rain || weather == WeatherKind.Storm;
            return true;
        }

        private static bool TryReadRaw(out RawEnvironment result)
        {
            result = new RawEnvironment();
            if (!Raft_Network.WorldHasBeenRecieved) return false;

            AzureSkyController sky = ComponentManager<AzureSkyController>.Value;
            WeatherManager weatherManager = ComponentManager<WeatherManager>.Value;
            if (sky == null || weatherManager == null) return false;

            result.Hour = Mathf.Repeat(sky.timeOfDay.hour, HoursPerDay);
            int day = Math.Max(0, WorldManager.DayCounter);
            if (day > (DateTime.MaxValue - CalendarEpoch).Days)
                day = (DateTime.MaxValue - CalendarEpoch).Days;
            result.Day = day;
            if (cachedCalendarDay != day)
            {
                cachedCalendarDate = CalendarEpoch.AddDays(day);
                cachedCalendarDay = day;
            }
            result.Date = cachedCalendarDate;
            result.WeatherCode = GetWeatherCode(weatherManager);
            result.NormalizedWeather = NormalizeWeatherCode(result.WeatherCode);
            return true;
        }

        internal static string FormatForecastDate(int gameDay)
        {
            if (gameDay < 0 || gameDay > (DateTime.MaxValue - CalendarEpoch).Days)
                return string.Empty;
            return FormatDate(CalendarEpoch.AddDays(gameDay));
        }

        private static string FormatDate(DateTime date)
        {
            string month = Localizer.T("calendar-month-" + date.Month);
            return Localizer.F("calendar-date", date.Day, month, date.Year);
        }

        private static string GetWeatherCode(WeatherManager weatherManager)
        {
            string weatherType = weatherManager.GetCurrentWeatherType().ToString();
            if (!string.Equals(weatherType, "Default", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrEmpty(weatherType))
                return weatherType;

            // Для Default проверяется и объект погоды, и его профиль
            Weather active = weatherManager.GetCurrentWeather();
            if (active == null) return weatherType;
            if (active == lastDefaultWeatherObject &&
                ReferenceEquals(active.so_weather, lastDefaultWeatherProfile) &&
                lastDefaultWeatherCode != null)
                return lastDefaultWeatherCode;
            string name = active.name ?? string.Empty;
            string profileName = active.so_weather != null
                ? active.so_weather.name ?? string.Empty : string.Empty;
            lastDefaultWeatherObject = active;
            lastDefaultWeatherProfile = active.so_weather;
            lastDefaultWeatherCode = name + " " + profileName;
            return lastDefaultWeatherCode;
        }

        private static string NormalizeWeatherCode(string code)
        {
            if (string.IsNullOrEmpty(code)) return string.Empty;
            if (string.Equals(code, lastWeatherCode, StringComparison.Ordinal))
                return lastNormalizedWeather;
            char[] buffer = new char[code.Length];
            int count = 0;
            foreach (char c in code)
                if (char.IsLetterOrDigit(c)) buffer[count++] = char.ToLowerInvariant(c);
            lastWeatherCode = code;
            lastNormalizedWeather = new string(buffer, 0, count);
            return lastNormalizedWeather;
        }

        private static string FormatWeather(string code)
        {
            string weather = code;

            if (weather.Contains("thunder") || weather.Contains("storm"))
                return Localizer.T("weather-storm");
            if (weather.Contains("rain") || weather.Contains("drizzle"))
                return Localizer.T("weather-rain");
            if (weather.Contains("snow"))
                return Localizer.T("weather-snow");
            if (weather.Contains("fog") || weather.Contains("mist"))
                return Localizer.T("weather-fog");
            if (weather.Contains("bigwave") || weather.Contains("wavebig") ||
                weather.Contains("wavesbig") ||
                weather.Contains("largewave") || weather.Contains("wavelarge") ||
                weather.Contains("waveslarge") ||
                weather.Contains("roughsea"))
                return Localizer.T("weather-big-waves");
            if (weather.Contains("smallwave") || weather.Contains("wavesmall") ||
                weather.Contains("wavessmall") ||
                weather.Contains("gentlewave") || weather.Contains("wavegentle") ||
                weather.Contains("wavesgentle") ||
                weather.Contains("lowwave") || weather.Contains("wavelow") ||
                weather.Contains("waveslow"))
                return Localizer.T("weather-small-waves");
            if (weather.Contains("defaultweather") || weather.Contains("normalweather") ||
                weather == "default")
                return Localizer.T("weather-good");
            if (weather.Contains("cloud"))
                return Localizer.T("weather-cloudy");
            if (weather.Contains("wind"))
                return Localizer.T("weather-windy");
            if (weather.Contains("calm") || weather.Contains("clear") ||
                weather.Contains("sunny"))
                return Localizer.T("weather-clear");
            if (weather.Contains("wave"))
                return Localizer.T("weather-small-waves");

            // Нераспознанный спокойный профиль показывается как небольшие волны
            return Localizer.T("weather-small-waves");
        }

        private static WeatherKind ClassifyWeather(string code)
        {
            if (string.Equals(code, lastWeatherKindCode, StringComparison.Ordinal))
                return lastWeatherKind;

            WeatherKind result = WeatherKind.Fair;
            if (code.Contains("storm") || code.Contains("thunder")) result = WeatherKind.Storm;
            else if (code.Contains("rain") || code.Contains("drizzle")) result = WeatherKind.Rain;
            else if (code.Contains("cloud") || code.Contains("fog") || code.Contains("mist"))
                result = WeatherKind.Cloudy;

            lastWeatherKindCode = code;
            lastWeatherKind = result;
            return result;
        }

        private static AirState CalculateAir(int day, int dayOfYear, float hour, WeatherKind weather)
        {
            EnsureDayPattern(day, dayOfYear);
            ClimateState climate = GetClimate(day, hour);
            float phase = (hour - 14f) / HoursPerDay * FullCircle;
            float daylight = Mathf.Max(0f, Mathf.Sin((hour - 6f) / 12f * Mathf.PI));
            float dayBlend = Mathf.SmoothStep(0f, 1f, hour / HoursPerDay);
            float variation = Mathf.Lerp(yesterdayPattern.Variation, todayPattern.Variation, dayBlend);
            float amplitude = Mathf.Lerp(yesterdayPattern.Amplitude, todayPattern.Amplitude, dayBlend);
            float middayVariation = Mathf.Lerp(yesterdayPattern.MiddayVariation,
                todayPattern.MiddayVariation, dayBlend);
            float precipitation = weather == WeatherKind.Storm ? 3.8f :
                weather == WeatherKind.Rain ? 2.7f : weather == WeatherKind.Cloudy ? 1.1f : 0f;

            float temperature = 27.7f + Mathf.Cos(phase) * amplitude +
                todayPattern.Season + variation + Mathf.Sin(phase * 2f + 0.4f) *
                middayVariation - precipitation;
            temperature = Mathf.Clamp(temperature + 0.75f * climate.Dry -
                0.55f * climate.Humid, 21f, 36f);

            if (climate.Heatwave > 0f)
            {
                float heatOffset = Mathf.Lerp(yesterdayPattern.HeatOffset,
                    todayPattern.HeatOffset, dayBlend);
                float hotDay = 34.4f + Mathf.Cos(phase) * 4.4f + heatOffset -
                    precipitation * 0.65f;
                temperature = Mathf.Lerp(temperature, Mathf.Clamp(hotDay, 26f, 40f), climate.Heatwave);
            }
            if (climate.Cool > 0f)
            {
                float coolOffset = Mathf.Lerp(yesterdayPattern.CoolOffset,
                    todayPattern.CoolOffset, dayBlend);
                float coolDay = 23.3f + Mathf.Cos(phase) * 1.8f + coolOffset -
                    precipitation * 0.65f;
                temperature = Mathf.Lerp(temperature, Mathf.Clamp(coolDay, 19f, 27f), climate.Cool);
            }

            float nightFactor = Mathf.Max(1f - Mathf.SmoothStep(5.5f, 9f, hour),
                Mathf.SmoothStep(18.5f, 22f, hour));
            float sultry = climate.SultryNights * nightFactor * (1f - climate.Cool);
            if (weather == WeatherKind.Rain) sultry *= 0.4f;
            else if (weather == WeatherKind.Storm) sultry *= 0.1f;
            if (sultry > 0f)
            {
                float target = Mathf.Lerp(yesterdayPattern.SultryTarget,
                    todayPattern.SultryTarget, dayBlend);
                temperature = Mathf.Lerp(temperature, Mathf.Max(temperature, target), sultry);
            }

            float humidity = Mathf.Lerp(88f, 70f, daylight) - 13f * climate.Dry +
                11f * climate.Humid;
            if (climate.Heatwave > 0f) humidity -= climate.Heatwave * 2.5f;
            if (sultry > 0f) humidity = Mathf.Max(humidity, Mathf.Lerp(humidity, 95f, sultry));
            if (weather == WeatherKind.Rain || weather == WeatherKind.Storm)
                humidity = Mathf.Max(humidity, 94f);
            else if (weather == WeatherKind.Cloudy)
                humidity = Mathf.Max(humidity, 82f);

            float sunlight = weather == WeatherKind.Storm ? daylight * 0.12f :
                weather == WeatherKind.Rain ? daylight * 0.22f :
                weather == WeatherKind.Cloudy ? daylight * 0.45f : daylight;

            return new AirState
            {
                Temperature = Mathf.Clamp(temperature, 19f, 40f),
                Humidity = Mathf.Clamp(humidity, 52f, 98f),
                Daylight = sunlight,
                Climate = climate
            };
        }

        private static void EnsureDayPattern(int day, int dayOfYear)
        {
            if (cachedPatternDay == day) return;
            todayPattern = BuildDayPattern(day, dayOfYear);
            yesterdayPattern = BuildDayPattern(day - 1, dayOfYear - 1);
            cachedPatternDay = day;
        }

        private static DayPattern BuildDayPattern(int day, int dayOfYear)
        {
            return new DayPattern
            {
                Amplitude = Mathf.Lerp(2.7f, 3.5f, DailyNoise(day, 23)),
                Variation = (DailyNoise(day, 109) - 0.5f) * 4.6f,
                MiddayVariation = 0.55f * (0.4f + DailyNoise(day, 71)),
                Season = Mathf.Cos((dayOfYear - 205f) / 365.25f * FullCircle) * 1.1f,
                HeatOffset = (DailyNoise(day, 341) - 0.5f) * 1.8f,
                CoolOffset = (DailyNoise(day, 359) - 0.5f) * 1.8f,
                SultryTarget = 29f + DailyNoise(day, 389) * 1.8f
            };
        }

        private static ClimateState GetClimate(int day, float hour)
        {
            int extremeCycle = day / ExtremeCycleDays;
            if (cachedExtremeCycle != extremeCycle)
            {
                cachedExtremeCycle = extremeCycle;
                float roll = DailyNoise(extremeCycle, 1701);
                extremePeriod = new ClimatePeriod
                {
                    Kind = roll < 0.46f ? PeriodKind.Heatwave :
                        roll < 0.80f ? PeriodKind.Cool : PeriodKind.None,
                    StartDay = extremeCycle * ExtremeCycleDays + 5 +
                        Mathf.FloorToInt(DailyNoise(extremeCycle, 1702) * 18f),
                    Duration = 3 + Mathf.FloorToInt(DailyNoise(extremeCycle, 1703) * 2f)
                };
            }

            int humidityCycle = day / HumidityCycleDays;
            if (cachedHumidityCycle != humidityCycle)
            {
                cachedHumidityCycle = humidityCycle;
                float roll = DailyNoise(humidityCycle, 2701);
                humidityPeriod = new ClimatePeriod
                {
                    Kind = roll < 0.39f ? PeriodKind.Dry :
                        roll < 0.78f ? PeriodKind.Humid : PeriodKind.None,
                    StartDay = humidityCycle * HumidityCycleDays + 1 +
                        Mathf.FloorToInt(DailyNoise(humidityCycle, 2702) * 2f),
                    Duration = 5 + Mathf.FloorToInt(DailyNoise(humidityCycle, 2703) * 3f)
                };
            }

            int sultryCycle = day / SultryCycleDays;
            if (cachedSultryCycle != sultryCycle)
            {
                cachedSultryCycle = sultryCycle;
                sultryPeriod = new ClimatePeriod
                {
                    Kind = DailyNoise(sultryCycle, 3701) < 0.4f ?
                        PeriodKind.SultryNights : PeriodKind.None,
                    StartDay = sultryCycle * SultryCycleDays + 2 +
                        Mathf.FloorToInt(DailyNoise(sultryCycle, 3702) * 7f),
                    Duration = 2 + Mathf.FloorToInt(DailyNoise(sultryCycle, 3703) * 3f)
                };
            }

            float extreme = PeriodStrength(extremePeriod, day, hour);
            float humidity = PeriodStrength(humidityPeriod, day, hour);
            float sultry = PeriodStrength(sultryPeriod, day, hour);
            return new ClimateState
            {
                Heatwave = extremePeriod.Kind == PeriodKind.Heatwave ? extreme : 0f,
                Cool = extremePeriod.Kind == PeriodKind.Cool ? extreme : 0f,
                SultryNights = sultryPeriod.Kind == PeriodKind.SultryNights ? sultry : 0f,
                Dry = humidityPeriod.Kind == PeriodKind.Dry ? humidity : 0f,
                Humid = humidityPeriod.Kind == PeriodKind.Humid ? humidity : 0f
            };
        }

        private static float PeriodStrength(ClimatePeriod period, int day, float hour)
        {
            if (period.Kind == PeriodKind.None) return 0f;
            float elapsed = day - period.StartDay + hour / HoursPerDay;
            if (elapsed <= 0f || elapsed >= period.Duration) return 0f;
            float strength = Mathf.Min(Mathf.Clamp01(elapsed / 0.33f),
                Mathf.Clamp01((period.Duration - elapsed) / 0.33f));
            return Mathf.SmoothStep(0f, 1f, strength);
        }

        private static string DescribeClimatePeriod(ClimateState climate, float hour)
        {
            bool russian = Localizer.Code == "ru";
            if (climate.Heatwave > 0.45f) return russian ? "Волна жары" : "Heatwave";
            if (climate.Cool > 0.45f) return russian ? "Прохладный период" : "Cool spell";
            if (climate.SultryNights > 0.45f && (hour < 8f || hour >= 19f))
                return russian ? "Душная ночь" : "Sultry night";
            if (climate.Dry > 0.45f) return russian ? "Сухой период" : "Dry spell";
            if (climate.Humid > 0.45f) return russian ? "Влажный период" : "Humid spell";
            return string.Empty;
        }

        internal struct ForecastDay
        {
            public int Day;
            public float DayTemperature;
            public float NightTemperature;
            public float DayHumidity;
            public float NightHumidity;
            public string DayClimate;
            public string NightClimate;
        }

        // Прогноз рассчитывается по климату без неизвестных будущих осадков
        internal static bool TryForecastDay(int day, out ForecastDay result)
        {
            result = new ForecastDay();
            if (day < 0 || day > (DateTime.MaxValue - CalendarEpoch).Days)
                return false;

            int dayOfYear = CalendarEpoch.AddDays(day).DayOfYear;
            AirState daytime = CalculateAir(day, dayOfYear, 14f, WeatherKind.Fair);
            AirState nighttime = CalculateAir(day, dayOfYear, 2f, WeatherKind.Fair);
            AirState late = CalculateAir(day, dayOfYear, 23f, WeatherKind.Fair);
            result.Day = day;
            result.DayTemperature = daytime.Temperature;
            result.NightTemperature = nighttime.Temperature;
            result.DayHumidity = daytime.Humidity;
            result.NightHumidity = nighttime.Humidity;
            result.DayClimate = DescribeClimatePeriod(daytime.Climate, 14f);
            result.NightClimate = DescribeClimatePeriod(late.Climate, 23f);
            return true;
        }

        internal static string CurrentClimateDiagnostic()
        {
            bool russian = Localizer.Code == "ru";
            RawEnvironment raw;
            if (!TryReadRaw(out raw)) return russian ? "Данные о климате недоступны" : "Climate data unavailable";
            AirState state = CalculateAir(raw.Day, raw.Date.DayOfYear, raw.Hour,
                ClassifyWeather(raw.NormalizedWeather));
            string climate = DescribeClimatePeriod(state.Climate, raw.Hour);
            if (climate.Length == 0) climate = russian ? "обычная погода" : "normal";
            string clock = Mathf.FloorToInt(raw.Hour).ToString("00") + ":" +
                Mathf.FloorToInt((raw.Hour % 1f) * 60f).ToString("00");
            string temperature = state.Temperature.ToString("0.0", CultureInfo.InvariantCulture);
            string humidity = Mathf.RoundToInt(state.Humidity).ToString(CultureInfo.InvariantCulture);
            return russian
                ? "Игровой день " + raw.Day + ", " + clock + ", воздух: " + temperature +
                    " °C, влажность: " + humidity + "%, климат: " + climate
                : "Day " + raw.Day + ", " + clock + ", air: " + temperature +
                    " C, humidity: " + humidity + "%, climate: " + climate;
        }

        internal static string ClimateDiagnostic(int previewDay)
        {
            bool russian = Localizer.Code == "ru";
            if (previewDay < 0 || previewDay > (DateTime.MaxValue - CalendarEpoch).Days)
                return russian ? "Некорректный номер игрового дня" : "Invalid day number";

            int dayOfYear = CalendarEpoch.AddDays(previewDay).DayOfYear;
            AirState noon = CalculateAir(previewDay, dayOfYear, 14f, WeatherKind.Fair);
            AirState night = CalculateAir(previewDay, dayOfYear, 23f, WeatherKind.Fair);
            string active = DescribeClimatePeriod(noon.Climate, 14f);
            if (active.Length == 0) active = DescribeClimatePeriod(night.Climate, 23f);
            if (active.Length == 0) active = russian ? "обычная погода" : "normal";
            string values = "14:00 " + noon.Temperature.ToString("0.0", CultureInfo.InvariantCulture) +
                " °C / " + Mathf.RoundToInt(noon.Humidity) + "%, 23:00 " +
                night.Temperature.ToString("0.0", CultureInfo.InvariantCulture) +
                " °C / " + Mathf.RoundToInt(night.Humidity) + "%";
            return russian
                ? "Игровой день " + previewDay + " (расчёт при ясной погоде): " + values + ", климат: " + active
                : "Day " + previewDay + " (clear-weather preview): " + values + ", climate: " + active;
        }

        internal static string ClimateForecast()
        {
            int day = Math.Max(0, WorldManager.DayCounter);
            int heat = -1, cool = -1, night = -1, dry = -1, humid = -1;
            for (int offset = 0; offset < 160; offset++)
            {
                int targetDay = day + offset;
                if (targetDay > (DateTime.MaxValue - CalendarEpoch).Days) break;
                ClimateState mid = GetClimate(targetDay, 14f);
                ClimateState late = GetClimate(targetDay, 23f);
                if (heat < 0 && mid.Heatwave > 0.8f) heat = targetDay;
                if (cool < 0 && mid.Cool > 0.8f) cool = targetDay;
                if (night < 0 && late.SultryNights > 0.8f && late.Cool < 0.2f) night = targetDay;
                if (dry < 0 && mid.Dry > 0.8f) dry = targetDay;
                if (humid < 0 && mid.Humid > 0.8f) humid = targetDay;
                if (heat >= 0 && cool >= 0 && night >= 0 && dry >= 0 && humid >= 0) break;
            }
            bool russian = Localizer.Code == "ru";
            if (russian)
                return "Ближайшие климатические периоды (номера игровых дней): жара " +
                    FormatForecastDay(heat, true) + ", прохлада " + FormatForecastDay(cool, true) +
                    ", душные ночи " + FormatForecastDay(night, true) +
                    ", сухой период " + FormatForecastDay(dry, true) +
                    ", влажный период " + FormatForecastDay(humid, true);
            return "Next climate periods (game days): heat " + FormatForecastDay(heat, false) +
                ", cool " + FormatForecastDay(cool, false) + ", sultry night " +
                FormatForecastDay(night, false) + ", dry " + FormatForecastDay(dry, false) +
                ", humid " + FormatForecastDay(humid, false);
        }

        private static string FormatForecastDay(int day, bool russian)
        {
            return day < 0 ? (russian ? "нет в ближайшие 160 дней" : "not in next 160 days") :
                day.ToString(CultureInfo.InvariantCulture);
        }

        private static float DailyNoise(int day, int salt)
        {
            unchecked
            {
                uint seed = (uint)day + (uint)(salt * 1973);
                seed ^= seed >> 16;
                seed *= 0x7feb352dU;
                seed ^= seed >> 15;
                seed *= 0x846ca68bU;
                seed ^= seed >> 16;
                return (seed & 0x00ffffffU) / 16777215f;
            }
        }
    }
}
