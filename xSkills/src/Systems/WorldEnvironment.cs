using System;
using UnityEngine;
using UnityEngine.AzureSky;

using xSkills.Services;
namespace xSkills.Systems
{
    internal static class WorldEnvironment
    {
        // Превью календаря начинается первого мая первого года
        private static readonly DateTime CalendarEpoch = new DateTime(1, 5, 1);
        private const float HoursPerDay = 24f;
        private const float FullCircle = Mathf.PI * 2f;

        internal struct Snapshot
        {
            public string Date;
            public string Clock;
            public string Weather;
            public float Temperature;
            public float RelativeHumidity;
            public float Daylight;
            public string WeatherCode;
        }

        internal static bool TryRead(out Snapshot result)
        {
            result = new Snapshot();
            if (!Raft_Network.WorldHasBeenRecieved) return false;

            AzureSkyController sky = ComponentManager<AzureSkyController>.Value;
            WeatherManager weatherManager = ComponentManager<WeatherManager>.Value;
            if (sky == null || weatherManager == null) return false;

            // Состояние считывается из Raft, которое уже синхронизирует хост
            float hour = Mathf.Repeat(sky.timeOfDay.hour, HoursPerDay);
            int minuteOfDay = Mathf.FloorToInt(hour * 60f);
            int day = Math.Max(0, WorldManager.DayCounter);
            if (day > (DateTime.MaxValue - CalendarEpoch).Days)
                day = (DateTime.MaxValue - CalendarEpoch).Days;
            DateTime date = CalendarEpoch.AddDays(day);

            result.Date = FormatDate(date);
            result.Clock = (minuteOfDay / 60).ToString("00") + ":" +
                (minuteOfDay % 60).ToString("00");

            string weatherCode = GetWeatherCode(weatherManager);
            result.Weather = FormatWeather(weatherCode);
            result.WeatherCode = weatherCode;
            result.Temperature = CalculateTropicalTemperature(day, date.DayOfYear, hour, weatherCode);
            result.RelativeHumidity = CalculateTropicalHumidity(hour, weatherCode);
            result.Daylight = CalculateDaylight(hour, weatherCode);
            return true;
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
            string name = active.name ?? string.Empty;
            string profileName = active.so_weather != null
                ? active.so_weather.name ?? string.Empty : string.Empty;
            return name + " " + profileName;
        }

        private static string NormalizeWeatherCode(string code)
        {
            if (string.IsNullOrEmpty(code)) return string.Empty;
            char[] buffer = new char[code.Length];
            int count = 0;
            foreach (char c in code)
                if (char.IsLetterOrDigit(c)) buffer[count++] = char.ToLowerInvariant(c);
            return new string(buffer, 0, count);
        }

        private static string FormatWeather(string code)
        {
            string weather = NormalizeWeatherCode(code);

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

        private static float CalculateTropicalTemperature(int day, int dayOfYear, float hour, string code)
        {
            // Суточные изменения и погодные колебания зависят от игрового дня
            float phase = (hour - 14f) / HoursPerDay * FullCircle;
            float amplitude = Mathf.Lerp(2.7f, 3.5f, DailyNoise(day, 23));
            float dailyCycle = Mathf.Cos(phase) * amplitude;
            float seasonalCycle = Mathf.Cos((dayOfYear - 205f) / 365.25f * FullCircle) * 1.1f;
            float dayBlend = Mathf.SmoothStep(0f, 1f, hour / HoursPerDay);
            float previousVariation = (DailyNoise(day - 1, 109) - 0.5f) * 4.6f;
            float nextVariation = (DailyNoise(day, 109) - 0.5f) * 4.6f;
            float dailyVariation = Mathf.Lerp(previousVariation, nextVariation, dayBlend);
            float middayVariation = Mathf.Sin(phase * 2f + 0.4f) * 0.55f *
                (0.4f + DailyNoise(day, 71));

            string weather = NormalizeWeatherCode(code);
            float precipitation = 0f;
            if (weather.Contains("storm") || weather.Contains("thunder"))
                precipitation = 3.8f;
            else if (weather.Contains("rain") || weather.Contains("drizzle"))
                precipitation = 2.7f;
            else if (weather.Contains("cloud") || weather.Contains("fog") || weather.Contains("mist"))
                precipitation = 1.1f;

            return Mathf.Clamp(27.7f + dailyCycle + seasonalCycle + dailyVariation +
                middayVariation - precipitation, 21f, 36f);
        }

        private static float CalculateTropicalHumidity(float hour, string code)
        {
            float sunlight = Mathf.Max(0f, Mathf.Sin((hour - 6f) / 12f * Mathf.PI));
            float humidity = Mathf.Lerp(88f, 70f, sunlight);
            string weather = NormalizeWeatherCode(code);
            if (weather.Contains("storm") || weather.Contains("thunder") ||
                weather.Contains("rain") || weather.Contains("drizzle"))
                humidity = Mathf.Max(humidity, 94f);
            else if (weather.Contains("cloud") || weather.Contains("fog") || weather.Contains("mist"))
                humidity = Mathf.Max(humidity, 82f);
            return Mathf.Clamp(humidity, 55f, 98f);
        }

        private static float CalculateDaylight(float hour, string code)
        {
            float sunlight = Mathf.Max(0f, Mathf.Sin((hour - 6f) / 12f * Mathf.PI));
            string weather = NormalizeWeatherCode(code);
            if (weather.Contains("storm") || weather.Contains("thunder")) return sunlight * 0.12f;
            if (weather.Contains("rain") || weather.Contains("drizzle")) return sunlight * 0.22f;
            if (weather.Contains("cloud") || weather.Contains("fog") || weather.Contains("mist"))
                return sunlight * 0.45f;
            return sunlight;
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
