using UnityEngine;
using xSkills.Systems;

namespace xSkills.Skills
{
    public sealed class Fishing : XSkill
    {
        // Опыт за успешный улов
        private const string CatchXpKey = "xskills_fishing_catch_xp_v1";
        public const float DefaultExperiencePerCatch = 1f;
        public const float MaximumExperiencePerCatch = 1000f;
        private const float PreviousDefaultExperiencePerCatch = 12f;
        private static bool experienceDefaultMigrated;

        private static void MigrateExperienceDefault()
        {
            if (experienceDefaultMigrated) return;
            experienceDefaultMigrated = true;
            if (!PlayerPrefs.HasKey(CatchXpKey)) return;
            if (!Mathf.Approximately(PlayerPrefs.GetFloat(CatchXpKey), PreviousDefaultExperiencePerCatch)) return;
            PlayerPrefs.SetFloat(CatchXpKey, DefaultExperiencePerCatch);
            PlayerPrefs.Save();
        }

        public static float ExperiencePerCatch
        {
            get
            {
                MigrateExperienceDefault();
                float fallback = DefaultExperiencePerCatch;
                WaterSurvivalSettings previous = WaterSurvivalSettings.Current;
                if (previous != null && !Mathf.Approximately(previous.FishingExperiencePerCatch,
                        PreviousDefaultExperiencePerCatch))
                    fallback = previous.FishingExperiencePerCatch;
                float value = PlayerPrefs.GetFloat(CatchXpKey, fallback);
                return !float.IsNaN(value) && !float.IsInfinity(value)
                    ? Mathf.Clamp(value, 0f, MaximumExperiencePerCatch) : DefaultExperiencePerCatch;
            }
        }

        public static void SetExperiencePerCatch(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return;
            PlayerPrefs.SetFloat(CatchXpKey, Mathf.Clamp(Mathf.Round(value * 10f) / 10f,
                0f, MaximumExperiencePerCatch));
            PlayerPrefs.Save();
        }

        public static void RestoreExperienceDefaults()
        {
            PlayerPrefs.SetFloat(CatchXpKey, DefaultExperiencePerCatch);
            PlayerPrefs.Save();
        }

        // Идентификаторы перков
        public int AnglerId { get; private set; }

        // Неиспользуемые идентификаторы
        // public int FisherId { get; private set; }
        // public int StrongLineId { get; private set; }
        // public int CarefulCasterId { get; private set; }
        // public int BaitMasterId { get; private set; }
        // public int DoubleHookId { get; private set; }

        public Fishing() : base("fishing", "xskills:skill-fishing", "xskills:group-collecting")
        {
            // Скорость поклёвки увеличивается
            // 0 - значение уровня перка
            AnglerId = AddAbility(new Ability(
                "angler",
                "xskills:ability-angler",
                "xskills:abilitydesc-angler",
                2, 5, new[] { 20, 40, 60, 80, 100 }));

            FishingAbilityBalance.Apply(this);

            ExpBase = 25f;

            // Неиспользуемые перки - механики пока не реализованы

            // Перк Fisher отключён
            // AddAbility(new Ability(
            //     "fisher",
            //     "xskills:ability-fisher",
            //     "xskills:abilitydesc-fisher",
            //     1, 3, new[] { 10, 20, 30 }));

            // Перк Strong Line отключён
            // AddAbility(new Ability(
            //     "strongline",
            //     "xskills:ability-strongline",
            //     "xskills:abilitydesc-strongline",
            //     3, 3, new[] { 10, 20, 30 }));

            // Перк Careful Caster отключён
            // AddAbility(new Ability(
            //     "carefulcaster",
            //     "xskills:ability-carefulcaster",
            //     "xskills:abilitydesc-carefulcaster",
            //     5, 3, new[] { 10, 20, 30 }));

            // Перк Bait Master отключён
            // AddAbility(new Ability(
            //     "baitmaster",
            //     "xskills:ability-baitmaster",
            //     "xskills:abilitydesc-baitmaster",
            //     7, 3, new[] { 10, 20, 30 }));

            // Перк Double Hook отключён
            // AddAbility(new Ability(
            //     "doublehook",
            //     "xskills:ability-doublehook",
            //     "xskills:abilitydesc-doublehook",
            //     9, 3, new[] { 10, 20, 30 }));
        }
    }
}
