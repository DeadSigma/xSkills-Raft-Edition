namespace xSkills.Skills
{
    public sealed class Cooking : XSkill
    {
        public Cooking() : base("cooking", "xskills:skill-cooking", "xskills:group-processing")
        {
            AddAbility(new Ability("chef", "xskills:ability-chef",
                "xskills:abilitydesc-chef", 1, 3, new[] { 10, 20, 30 }));
            AddAbility(new Ability("eggtimer", "xskills:ability-eggtimer",
                "xskills:abilitydesc-eggtimer", 3, 3, new[] { 10, 20, 30 }));
            AddAbility(new Ability("preserver", "xskills:ability-preserver",
                "xskills:abilitydesc-preserver", 5, 3, new[] { 10, 20, 30 }));
            AddAbility(new Ability("happymeal", "xskills:ability-happymeal",
                "xskills:abilitydesc-happymeal", 7, 3, new[] { 10, 20, 30 }));
            AddAbility(new Ability("fastfood", "xskills:ability-fastfood",
                "xskills:abilitydesc-fastfood", 9, 3, new[] { 10, 20, 30 }));
        }
    }
}
