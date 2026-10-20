namespace xSkills.Skills
{
    public sealed class Fishing : XSkill
    {
        public Fishing() : base("fishing", "xskills:skill-fishing", "xskills:group-collecting")
        {
            AddAbility(new Ability("angler", "xskills:ability-angler",
                "xskills:abilitydesc-angler", 2, 5, new[] { 20, 40, 60, 80, 100 }));
            AddAbility(new Ability("fisher", "xskills:ability-fisher",
                "xskills:abilitydesc-fisher", 1, 3, new[] { 10, 20, 30 }));
            AddAbility(new Ability("strongline", "xskills:ability-strongline",
                "xskills:abilitydesc-strongline", 3, 3, new[] { 10, 20, 30 }));
            AddAbility(new Ability("carefulcaster", "xskills:ability-carefulcaster",
                "xskills:abilitydesc-carefulcaster", 5, 3, new[] { 10, 20, 30 }));
            AddAbility(new Ability("baitmaster", "xskills:ability-baitmaster",
                "xskills:abilitydesc-baitmaster", 7, 3, new[] { 10, 20, 30 }));
            AddAbility(new Ability("doublehook", "xskills:ability-doublehook",
                "xskills:abilitydesc-doublehook", 9, 3, new[] { 10, 20, 30 }));
        }
    }
}
