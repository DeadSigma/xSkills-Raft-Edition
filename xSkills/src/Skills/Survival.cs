namespace xSkills.Skills
{
    public sealed class Survival : XSkill
    {
        public Survival() : base("survival", "xskills:skill-survival", "xskills:group-survival")
        {
            AddAbility(new Ability("longlife", "xskills:ability-longlife",
                "xskills:abilitydesc-longlife", 1, 3, new[] { 10, 20, 30 }));
            AddAbility(new Ability("hugestomach", "xskills:ability-hugestomach",
                "xskills:abilitydesc-hugestomach", 3, 3, new[] { 10, 20, 30 }));
            AddAbility(new Ability("strongback", "xskills:ability-strongback",
                "xskills:abilitydesc-strongback", 5, 3, new[] { 10, 20, 30 }));
            AddAbility(new Ability("scout", "xskills:ability-scout",
                "xskills:abilitydesc-scout", 7, 3, new[] { 10, 20, 30 }));
            AddAbility(new Ability("sprinter", "xskills:ability-sprinter",
                "xskills:abilitydesc-sprinter", 9, 3, new[] { 10, 20, 30 }));
        }
    }
}
