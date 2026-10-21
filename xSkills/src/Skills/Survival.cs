namespace xSkills.Skills
{
    public sealed class Survival : XSkill
    {
        public Survival() : base("survival", "xskills:skill-survival", "xskills:group-survival")
        {
            ExpBase = 10f;
            ExpMult = 5f;
            ExpEquationValue = 0.4f;
            QuadraticExperience = true;
            AddAbility(new Ability("longlife", "xskills:ability-longlife",
                "xskills:abilitydesc-longlife", 1, 3, new[] { 5, 1, 15, 10, 2, 30, 10, 2, 50 }));
            AddAbility(new Ability("forecast", "xskills:ability-forecast",
                "xskills:abilitydesc-forecast", 2, 1, new[] { 10 }));
            AddAbility(new Ability("hugestomach", "xskills:ability-hugestomach",
                "xskills:abilitydesc-hugestomach", 3, 3, new[] { 10, 20, 30 }));
            AddAbility(new Ability("strongback", "xskills:ability-strongback",
                "xskills:abilitydesc-strongback", 5, 3, new[] { 10, 20, 30 }));
            AddAbility(new Ability("scout", "xskills:ability-scout",
                "xskills:abilitydesc-scout", 7, 1, new[] { 5 }));
            AddAbility(new Ability("meatshield", "xskills:ability-meatshield",
                "xskills:abilitydesc-meatshield", 3, 3, new[] { 10, 20, 20, 15, 30, 10 }));
            AddAbility(new Ability("diver", "xskills:ability-diver",
                "xskills:abilitydesc-diver", 3, 2, new[] { 50, 75 }));
            AddAbility(new Ability("featherfall", "xskills:ability-featherfall",
                "xskills:abilitydesc-featherfall", 3, 2, new[] { 1, 10, 2, 20 }));
            AddAbility(new Ability("photosynthesis", "xskills:ability-photosynthesis",
                "xskills:abilitydesc-photosynthesis", 5, 3, new[] { 15, 25, 40, 50, 50, 50 }));
            AddAbility(new Ability("laststand", "xskills:ability-laststand",
                "xskills:abilitydesc-laststand", 10, 1, new[] { 200 }));
            AddAbility(new Ability("sprinter", "xskills:ability-sprinter",
                "xskills:abilitydesc-sprinter", 9, 2, new[] { 5, 10, 10, 20 }));
            SurvivalAbilityBalance.Apply(this);
        }
    }
}
