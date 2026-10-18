using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace xSkills.GUI
{
    internal static class Localizer
    {
        private static readonly Dictionary<string, string> English = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> Selected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> Missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static string code = "en";
        private static bool initialized;

        public static string Code { get { return code; } }
        public static bool UseBitmapFont { get { return code == "en"; } }

        public static void Reset()
        {
            English.Clear();
            Selected.Clear();
            Missing.Clear();
            code = "en";
            initialized = false;
        }

        public static bool Refresh()
        {
            string wanted = FindGameLanguage();
            if (initialized && code == wanted) return false;
            code = wanted;
            English.Clear();
            Selected.Clear();
            Missing.Clear();
            LoadLanguage("en", English);
            if (code != "en") LoadLanguage(code, Selected);
            initialized = true;
            Debug.Log("[xSkills] Lang selected=" + code + ", native keys=" + Selected.Count + ", en fallback=" + English.Count);
            return true;
        }

        private static void LoadLanguage(string language, Dictionary<string, string> target)
        {
            ReadDictionary("assets/lang/" + language + ".json", target);
        }

        private static void ReadDictionary(string path, Dictionary<string, string> target)
        {
            byte[] data = ResourceLoader.FileBytes(path, false);
            if (data == null || data.Length == 0) return;
            try
            {
                using (var stream = new MemoryStream(data))
                {
                    var serializer = new DataContractJsonSerializer(typeof(Dictionary<string, string>),
                        new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
                    var values = serializer.ReadObject(stream) as Dictionary<string, string>;
                    if (values == null) return;
                    foreach (var entry in values)
                        if (!string.IsNullOrEmpty(entry.Key) && !string.IsNullOrEmpty(entry.Value))
                            target[entry.Key] = entry.Value;
                    Debug.Log("[xSkills] Lang loaded: " + path + " (" + values.Count + " keys)");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[xSkills] Lang read error: " + path + " - " + ex.Message);
            }
        }

        public static string T(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            int colon = key.IndexOf(':');
            if (colon >= 0) key = key.Substring(colon + 1);
            string value;
            if (Selected.TryGetValue(key, out value)) return value;
            if (English.TryGetValue(key, out value))
            {
                if (code != "en") LogMissing(key, "no '" + code + "' translation - English fallback");
                return value;
            }
            LogMissing(key, "not found in any loaded lang file");
            return key;
        }

        private static void LogMissing(string key, string reason)
        {
            if (Missing.Add(key) && Missing.Count <= 80)
                Debug.LogWarning("[xSkills] Lang: " + key + " - " + reason);
        }

        public static string SkillName(string id, string ignoredFallback = null) { return T("skill-" + id); }
        public static string AbilityName(string id, string ignoredFallback = null) { return T("ability-" + id); }
        public static string AbilityDescription(string id, string ignoredFallback = null)
        {
            string key = "abilitydesc-" + id;
            string value;
            if (Selected.TryGetValue(key, out value) || English.TryGetValue(key, out value))
                return FormatText(value);
            return FormatText(T("ui-ability-description-unavailable"));
        }

        public static string F(string key, params object[] values)
        {
            string template = T(key);
            try { return string.Format(template, values); }
            catch (FormatException) { return template; }
        }

        public static string FormatText(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            text = Regex.Replace(text, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"<font\s+color\s*=\s*['""']?(#?[0-9a-fA-F]{6})['""']?\s*>",
                m => "<color=#" + m.Groups[1].Value.TrimStart('#') + ">", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"</font>", "</color>", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\{\d+(?::[^}]*)?\}", "?", RegexOptions.None);
            return text;
        }

        private static string FindGameLanguage()
        {
            try
            {
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type type = asm.GetType("I2.Loc.LocalizationManager", false);
                    if (type == null) continue;
                    foreach (string name in new[] { "CurrentLanguageCode", "CurrentLanguage" })
                    {
                        PropertyInfo property = type.GetProperty(name, BindingFlags.Static | BindingFlags.Public);
                        if (property == null) continue;
                        string language = Normalize(property.GetValue(null, null) as string);
                        if (language != null) return language;
                    }
                }
            }
            catch (Exception ex) { Debug.LogWarning("[xSkills] Language API: " + ex.Message); }
            foreach (string name in new[] { "Language", "language", "SelectedLanguage", "selectedLanguage", "Lang" })
            {
                try
                {
                    string language = Normalize(PlayerPrefs.GetString(name, ""));
                    if (language != null) return language;
                }
                catch { }
            }
            byte[] selected = ResourceLoader.FileBytes("assets/lang/language.txt", false);
            if (selected != null)
            {
                string language = Normalize(Encoding.UTF8.GetString(selected).Trim());
                if (language != null) return language;
            }
            return Normalize(Application.systemLanguage.ToString()) ?? "en";
        }

        private static string Normalize(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            string text = raw.Trim().ToLowerInvariant().Replace('_', '-');
            if (text == "ru" || text.StartsWith("ru-") || text.Contains("рус") || text.Contains("russian")) return "ru";
            if (text == "en" || text.StartsWith("en-") || text.Contains("english")) return "en";
            if (text.Length == 2 && char.IsLetter(text[0]) && char.IsLetter(text[1])) return text;
            if (text.Length >= 3 && text[2] == '-') return text.Substring(0, 2);
            return null;
        }
    }
}
