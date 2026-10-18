namespace xSkills.Skills
{
    public sealed class Sailing : XSkill
    {
        public int SteadyHelmId { get; private set; }
        public int CrewCaptainId { get; private set; }
        public int ShipwrightId { get; private set; }
        public int CoastalPilotId { get; private set; }
        public int OpenSeaId { get; private set; }

        public Sailing() : base("sailing", "xskills:skill-sailing", "xskills:group-travel", 200, 1.33f, 25)
        {
            SteadyHelmId = AddAbility(new Ability("steadyhelm", "xskills:ability-steadyhelm",
                "xskills:abilitydesc-steadyhelm", 1, 3,
                new[] { 10, 1, 20, 20, 2, 40, 20, 2, 60 }));
            ShipwrightId = AddAbility(new Ability("shipwright", "xskills:ability-shipwright",
                "xskills:abilitydesc-shipwright", 3, 3, new[] { 20, 40, 60 }));
            CrewCaptainId = AddAbility(new Ability("crewcaptain", "xskills:ability-crewcaptain",
                "xskills:abilitydesc-crewcaptain", 5, 3, new[] { 5, 10, 15 }));
            CoastalPilotId = AddAbility(new Ability("coastalpilot", "xskills:ability-coastalpilot",
                "xskills:abilitydesc-coastalpilot", 5, 3, new[] { 20, 35, 50 }));
            OpenSeaId = AddAbility(new Ability("opensea", "xskills:ability-opensea",
                "xskills:abilitydesc-opensea", 5, 3, new[] { 20, 35, 50 }));
            QuadraticExperience = true;
            ExpBase = 40f;
            ExpMult = 10f;
            ExpEquationValue = .8f;
        }
    }
}
