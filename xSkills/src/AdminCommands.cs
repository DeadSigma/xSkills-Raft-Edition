using System;
using System.Globalization;
using System.Reflection;
using HMLLibrary;
using UnityEngine;
using xSkills.GUI;
using xSkills.Skills;

namespace xSkills
{
    public static class AdminCommands
    {
        public static bool Active;

        public static string Level(string[] args)
        {
            return RunSkillCommand("level", NormalizeArgs("level", args));
        }

        public static string Exp(string[] args)
        {
            return RunSkillCommand("exp", NormalizeArgs("exp", args));
        }

        public static string Tier(string[] args)
        {
            return RunSkillCommand("tier", NormalizeArgs("tier", args));
        }

        public static string UnlearnPoints(string[] args)
        {
            args = NormalizeArgs("unlearnpoints", args);
            string failure = CheckAccess();
            if (failure != null) return failure;
            if (args == null || args.Length < 1) return Localizer.T("command-usage-unlearnpoints");
            string action = args[0].ToLowerInvariant();
            if (action == "get")
            {
                if (args.Length != 1) return Localizer.T("command-usage-unlearnpoints");
                return Localizer.F("command-unlearn-value", SkillCatalog.UnlearnPoints);
            }
            if ((action != "add" && action != "set") || args.Length != 2)
                return Localizer.T("command-usage-unlearnpoints");
            int value;
            if (!int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out value) || value < 0)
                return Localizer.T("command-invalid-number");
            if (action == "add" && value > int.MaxValue - SkillCatalog.UnlearnPoints)
                return Localizer.T("command-invalid-number");
            SkillCatalog.SetUnlearnPoints(action == "add" ? SkillCatalog.UnlearnPoints + value : value);
            return Localizer.F("command-unlearn-value", SkillCatalog.UnlearnPoints);
        }

        private static string[] NormalizeArgs(string command, string[] raw)
        {
            if (raw == null) return new string[0];
            var result = new System.Collections.Generic.List<string>();
            foreach (string value in raw)
            {
                if (string.IsNullOrWhiteSpace(value)) continue;
                result.AddRange(value.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
            }
            while (result.Count > 0 &&
                string.Equals(result[0].TrimStart('/'), command, StringComparison.OrdinalIgnoreCase))
                result.RemoveAt(0);
            return result.ToArray();
        }

        private static string RunSkillCommand(string kind, string[] args)
        {
            string failure = CheckAccess();
            if (failure != null) return failure;
            if (args == null || args.Length < 2) return Usage(kind);
            string action = args[0].ToLowerInvariant();
            if (action != "get" && action != "add" && action != "set" && !(kind == "level" && action == "reset"))
                return Usage(kind);

            bool hasValue = action == "add" || action == "set";
            int selfArgCount = kind == "tier" ? 3 : 2;
            if (hasValue) selfArgCount++;
            if (args.Length != selfArgCount && args.Length != selfArgCount + 1) return Usage(kind);

            bool explicitPlayer = args.Length == selfArgCount + 1;
            int offset = explicitPlayer ? 2 : 1;
            if (explicitPlayer && !IsLocalPlayer(args[1]))
                return Localizer.T("command-remote-unsupported");

            string skillCode = args[offset];
            SkillData skill = SkillCatalog.Find(skillCode);
            if (skill == null) return Localizer.F("command-unknown-skill", skillCode);

            AbilityData ability = null;
            if (kind == "tier")
            {
                string abilityCode = args[offset + 1];
                ability = skill.FindAbility(abilityCode);
                if (ability == null)
                    return Localizer.F("command-unknown-ability", abilityCode, skill.Id);
            }

            if (action == "get") return Describe(kind, skill, ability);
            if (action == "reset")
            {
                skill.Reset();
                SkillCatalog.NotifyChanged();
                return Describe(kind, skill, ability);
            }

            string amount = args[args.Length - 1];
            if (kind == "exp")
            {
                float value;
                if (!float.TryParse(amount, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                    || value < 0f || float.IsNaN(value) || float.IsInfinity(value) || value > 1000000000f)
                    return Localizer.T("command-invalid-number");
                if (action == "set") skill.SetExperience(value);
                else skill.AddExperience(value);
            }
            else
            {
                int value;
                if (!int.TryParse(amount, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) || value < 0)
                    return Localizer.T("command-invalid-number");
                if (kind == "level")
                {
                    int level = action == "set" ? value :
                        (int)Math.Min(skill.MaxLevel, (long)skill.Level + value);
                    if (level < 1 || level > skill.MaxLevel)
                        return Localizer.F("command-level-range", skill.MaxLevel);
                    skill.SetLevel(level);
                }
                else
                {
                    long tier = action == "set" ? value : (long)ability.PreviewTier + value;
                    if (tier < 0 || tier > ability.MaxTier)
                        return Localizer.F("command-tier-range", ability.MaxTier);
                    skill.SetTier(ability, (int)tier, true);
                }
            }
            SkillCatalog.NotifyChanged();
            return Describe(kind, skill, ability);
        }

        private static string Usage(string kind)
        {
            return Localizer.T("command-usage-" + kind);
        }

        private static string Describe(string kind, SkillData skill, AbilityData ability)
        {
            if (kind == "level")
                return Localizer.F("command-level-value", Localizer.SkillName(skill.Id), skill.Level);
            if (kind == "exp")
                return Localizer.F("command-exp-value", Localizer.SkillName(skill.Id),
                    skill.Experience.ToString("0.##", CultureInfo.InvariantCulture),
                    skill.RequiredExperience.ToString("0.##", CultureInfo.InvariantCulture), skill.Level);
            return Localizer.F("command-tier-value", Localizer.AbilityName(ability.Id),
                Localizer.SkillName(skill.Id), ability.PreviewTier, ability.MaxTier);
        }

        private static bool IsLocalPlayer(string name)
        {
            if (string.Equals(name, "me", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "self", StringComparison.OrdinalIgnoreCase)) return true;

            if (RAPI.GetLocalPlayer() == null) return false;

            string localName = GetLocalSteamName();
            return !string.IsNullOrEmpty(localName) &&
                string.Equals(localName, name, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetLocalSteamName()
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type steamFriends = assembly.GetType("Steamworks.SteamFriends", false);
                if (steamFriends == null) continue;

                MethodInfo getPersonaName = steamFriends.GetMethod(
                    "GetPersonaName",
                    BindingFlags.Public | BindingFlags.Static,
                    null, Type.EmptyTypes, null);
                if (getPersonaName == null) return null;

                try
                {
                    return getPersonaName.Invoke(null, null) as string;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[xSkills] Cannot resolve local Steam name: " + ex.Message);
                    return null;
                }
            }

            return null;
        }

        private static string CheckAccess()
        {
            if (!Active)
            {
                return Localizer.T("command-not-loaded");
            }
            Localizer.Refresh();
            bool inWorld = RAPI.IsCurrentSceneGame();
            object localPlayer = RAPI.GetLocalPlayer();
            if (!inWorld || localPlayer == null)
                return Localizer.T("command-not-in-world");
            // При отсутствии достоверного флага хоста доступ не разрешается
            bool isHost;
            if (!HostAccess.TryIsHost(out isHost))
            {
                Debug.LogWarning("[xSkills] Unable to resolve Raft host status; admin commands are blocked");
                return Localizer.T("command-host-unknown");
            }
            if (!isHost) return Localizer.T("command-host-only");
            return null;
        }
    }

    internal static class HostAccess
    {
        private static readonly string[] HostProperties =
        {
            "IsHost", "isHost", "Host", "isServer", "IsServer"
        };

        public static bool TryIsHost(out bool host)
        {
            host = false;
            object local = RAPI.GetLocalPlayer();
            if (local == null) return false;
            bool result;
            if (TryBooleanMember(local.GetType(), local, out result))
            {
                host = result;
                return true;
            }

            Assembly game = local.GetType().Assembly;
            foreach (string typeName in new[] { "Semih_Network", "Raft_Network", "NetworkManager" })
            {
                Type type = game.GetType(typeName);
                if (type == null) continue;
                if (TryBooleanMember(type, null, out result))
                {
                    host = result;
                    return true;
                }
                object instance = GetInstance(type);
                if (instance != null && TryBooleanMember(type, instance, out result))
                {
                    host = result;
                    return true;
                }
            }
            return false;
        }

        private static object GetInstance(Type type)
        {
            foreach (string name in new[] { "Instance", "instance", "Singleton" })
            {
                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
                try
                {
                    PropertyInfo property = type.GetProperty(name, flags);
                    if (property != null) return property.GetValue(null, null);
                    FieldInfo field = type.GetField(name, flags);
                    if (field != null) return field.GetValue(null);
                }
                catch (Exception) { }
            }
            return null;
        }

        private static bool TryBooleanMember(Type type, object instance, out bool value)
        {
            value = false;
            BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                (instance == null ? BindingFlags.Static : BindingFlags.Instance);
            foreach (string name in HostProperties)
            {
                try
                {
                    PropertyInfo p = type.GetProperty(name, flags);
                    if (p != null && p.PropertyType == typeof(bool))
                    {
                        value = (bool)p.GetValue(instance, null);
                        return true;
                    }
                    FieldInfo f = type.GetField(name, flags);
                    if (f != null && f.FieldType == typeof(bool))
                    {
                        value = (bool)f.GetValue(instance);
                        return true;
                    }
                    MethodInfo m = type.GetMethod(name, flags, null, Type.EmptyTypes, null);
                    if (m != null && m.ReturnType == typeof(bool))
                    {
                        value = (bool)m.Invoke(instance, null);
                        return true;
                    }
                }
                catch (Exception) { }
            }
            return false;
        }
    }
}
