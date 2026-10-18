using System;
using System.Collections.Generic;

namespace xSkills.Skills
{
    public class SkillData
    {
        public readonly string Id;
        public readonly string Title;
        public readonly string Description;
        public readonly string Group;
        public readonly List<AbilityData> Abilities = new List<AbilityData>();

        public int Level { get; private set; } = 1;
        public float Experience { get; private set; }
        public int MaxLevel { get; protected set; }
        public float ExpBase { get; protected set; }
        public float ExpMult { get; protected set; }
        public float ExpEquationValue { get; protected set; }
        public bool QuadraticExperience { get; protected set; }

        public int PreviewLevel { get { return Level; } }
        public int PreviewPoints
        {
            get
            {
                int points = Level - 1;
                foreach (AbilityData ability in Abilities) points -= ability.PreviewTier;
                return Math.Max(0, points);
            }
        }
        public int PreviewUnlearnPoints { get { return SkillCatalog.UnlearnPoints; } }

        public float RequiredExperience
        {
            get
            {
                if (Level >= MaxLevel) return 0;
                int current = Level - 1;
                double value = QuadraticExperience
                    ? ExpEquationValue * current * current + ExpMult * current + ExpBase
                    : ExpBase * Math.Pow(ExpMult, current);
                return Math.Max(1f, (float)Math.Min(value, 1000000000d));
            }
        }

        protected SkillData(string id, string title, string group, int expBase, float expMult, int maxLevel)
        {
            Id = id;
            Title = title;
            Group = group;
            Description = "";
            ExpBase = expBase;
            ExpMult = expMult;
            MaxLevel = maxLevel;
        }

        public int AddAbility(AbilityData ability)
        {
            if (ability == null || Abilities.Exists(a => a.Id == ability.Id)) return -1;
            Abilities.Add(ability);
            return Abilities.Count - 1;
        }

        public AbilityData FindAbility(string id)
        {
            return Abilities.Find(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        public void SetLevel(int newLevel)
        {
            int oldLevel = Level;
            Level = Math.Max(1, Math.Min(MaxLevel, newLevel));
            Experience = 0;
            ClampTiersToLevel();
            if (Level > oldLevel) SkillCatalog.ReportLevelUp(this, Level);
        }

        public void Reset()
        {
            Level = 1;
            Experience = 0;
            foreach (AbilityData a in Abilities) a.PreviewTier = 0;
        }

        public void AddExperience(float amount)
        {
            if (float.IsNaN(amount) || float.IsInfinity(amount)) return;
            ApplyExperience(Experience + amount, Math.Max(0f, amount));
        }

        public void SetExperience(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return;
            ApplyExperience(value, Math.Max(0f, value - Experience));
        }

        private void ApplyExperience(float value, float gained)
        {
            int previousLevel = Level;
            Experience = Math.Max(0f, Math.Min(1000000000f, value));
            while (Level < MaxLevel)
            {
                float required = RequiredExperience;
                if (required <= 0f || Experience < required) break;
                Experience -= required;
                Level++;
            }
            if (Level >= MaxLevel) Experience = 0f;
            if (gained > 0f) SkillCatalog.ReportExperience(this, gained);
            if (Level > previousLevel) SkillCatalog.ReportLevelUp(this, Level);
        }

        public bool SetTier(AbilityData ability, int tier, bool adminOverride = false)
        {
            if (ability == null || tier < 0 || tier > ability.MaxTier) return false;
            if (!adminOverride && tier > ability.PreviewTier)
            {
                if (Level < ability.RequiredLevel + tier - 1) return false;
                if (tier - ability.PreviewTier > PreviewPoints) return false;
            }
            ability.PreviewTier = tier;
            return true;
        }

        private void ClampTiersToLevel()
        {
            int points = Level - 1;
            foreach (AbilityData ability in Abilities)
            {
                if (Level < ability.RequiredLevel) ability.PreviewTier = 0;
                ability.PreviewTier = Math.Min(ability.PreviewTier, points);
                points -= ability.PreviewTier;
            }
        }
    }

    public abstract class XSkill : SkillData
    {
        protected XSkill(string id, string title, string group, int expBase = 200,
            float expMult = 1.33f, int maxLevel = 25)
            : base(id, title, group, expBase, expMult, maxLevel) { }
    }

    public class AbilityData
    {
        public readonly string Id;
        public readonly string Title;
        public readonly string Description;
        public readonly int RequiredLevel;
        public readonly int MaxTier;
        public readonly int[] Values;
        public bool PreviewLocked;
        public int PreviewTier;
        public float VisualTier;
        public float Glow;

        public AbilityData(string id, string title, string description, int requiredLevel, int maxTier, int[] values)
        {
            Id = id;
            Title = title;
            Description = description;
            RequiredLevel = requiredLevel;
            MaxTier = maxTier;
            Values = values ?? new int[0];
        }
    }

    public sealed class Ability : AbilityData
    {
        public Ability(string id, string nameKey, string descKey, int minLevel, int maxTier,
            int[] values, bool hideUntilLearnable = false)
            : base(id, nameKey, descKey, minLevel, maxTier, values)
        {
            PreviewLocked = hideUntilLearnable;
        }

        public Ability(string id, string nameKey, string descKey, int minLevel, int maxTier,
            int value, bool hideUntilLearnable = false)
            : this(id, nameKey, descKey, minLevel, maxTier, new[] { value }, hideUntilLearnable) { }
    }

    public static class SkillCatalog
    {
        private static readonly List<SkillData> Registered = new List<SkillData>();
        public static int UnlearnPoints { get; private set; }
        public static event Action Changed;
        public static event Action<SkillData, float> ExperienceGained;
        public static event Action<SkillData, int> LevelGained;

        internal static void ReportExperience(SkillData skill, float amount)
        {
            Action<SkillData, float> handler = ExperienceGained;
            if (handler != null) handler(skill, amount);
        }

        internal static void ReportLevelUp(SkillData skill, int level)
        {
            Action<SkillData, int> handler = LevelGained;
            if (handler != null) handler(skill, level);
        }

        public static List<SkillData> Create()
        {
            if (Registered.Count == 0)
            {
                Registered.Add(new Cooking());
                Registered.Add(new Fishing());
                Registered.Add(new Sailing());
                Registered.Add(new Survival());
            }
            return Registered;
        }

        public static SkillData Find(string id)
        {
            Create();
            return Registered.Find(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        public static void SetUnlearnPoints(int value)
        {
            UnlearnPoints = Math.Max(0, value);
            NotifyChanged();
        }

        public static void NotifyChanged()
        {
            Action handler = Changed;
            if (handler != null) handler();
        }

        public static void Clear()
        {
            Registered.Clear();
            UnlearnPoints = 0;
            Changed = null;
            ExperienceGained = null;
            LevelGained = null;
        }
    }
}
