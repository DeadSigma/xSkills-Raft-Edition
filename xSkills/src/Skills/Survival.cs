namespace xSkills.Skills
{
    public sealed class Survival : XSkill
    {
        // Идентификаторы перков
        public int LongLifeId { get; private set; }
        public int ForecastId { get; private set; }
        public int HugeStomachId { get; private set; }
        public int StrongBackId { get; private set; }
        public int ScoutId { get; private set; }
        public int MeatShieldId { get; private set; }
        public int DiverId { get; private set; }
        public int FeatherFallId { get; private set; }
        public int PhotosynthesisId { get; private set; }
        public int LastStandId { get; private set; }
        public int SprinterId { get; private set; }

        public Survival() : base("survival", "xskills:skill-survival", "xskills:group-survival")
        {
            // Максимальное здоровье увеличивается с уровнем навыка
            // 0 - начальный бонус
            // 1 - прирост за уровень
            // 2 - предел бонуса
            LongLifeId = AddAbility(new Ability(
                "longlife",
                "xskills:ability-longlife",
                "xskills:abilitydesc-longlife",
                1, 3, new[] { 5, 1, 15, 10, 2, 30, 10, 2, 50 }));

            // Прогноз погоды становится доступным
            // 0 - параметр прогноза
            ForecastId = AddAbility(new Ability(
                "forecast",
                "xskills:ability-forecast",
                "xskills:abilitydesc-forecast",
                2, 1, new[] { 10 }));

            // Вместимость желудка увеличивается
            // 0 - добавочная вместимость
            HugeStomachId = AddAbility(new Ability(
                "hugestomach",
                "xskills:ability-hugestomach",
                "xskills:abilitydesc-hugestomach",
                3, 3, new[] { 10, 20, 30 }));

            // Дополнительные слоты инвентаря открываются
            // 0 - количество слотов
            StrongBackId = AddAbility(new Ability(
                "strongback",
                "xskills:ability-strongback",
                "xskills:abilitydesc-strongback",
                5, 2, new[] { 3, 6 }));

            // Скорость перемещения увеличивается
            // 0 - бонус к скорости
            ScoutId = AddAbility(new Ability(
                "scout",
                "xskills:ability-scout",
                "xskills:abilitydesc-scout",
                7, 1, new[] { 5 }));

            // Получаемый урон снижается
            MeatShieldId = AddAbility(new Ability(
                "meatshield",
                "xskills:ability-meatshield",
                "xskills:abilitydesc-meatshield",
                3, 3, new[] { 10, 20, 20, 15, 30, 10 }));

            // Расход кислорода снижается
            // 0 - процент снижения
            DiverId = AddAbility(new Ability(
                "diver",
                "xskills:ability-diver",
                "xskills:abilitydesc-diver",
                3, 2, new[] { 50, 75 }));

            // Урон от падения уменьшается
            FeatherFallId = AddAbility(new Ability(
                "featherfall",
                "xskills:ability-featherfall",
                "xskills:abilitydesc-featherfall",
                3, 2, new[] { 1, 10, 2, 20 }));

            // Восстановление зависит от времени суток
            PhotosynthesisId = AddAbility(new Ability(
                "photosynthesis",
                "xskills:ability-photosynthesis",
                "xskills:abilitydesc-photosynthesis",
                5, 3, new[] { 15, 25, 40, 50, 50, 50 }));

            // Смертельный урон может быть предотвращён
            LastStandId = AddAbility(new Ability(
                "laststand",
                "xskills:ability-laststand",
                "xskills:abilitydesc-laststand",
                10, 1, new[] { 200 }));

            // Скорость увеличивается вместе с расходом сытости
            SprinterId = AddAbility(new Ability(
                "sprinter",
                "xskills:ability-sprinter",
                "xskills:abilitydesc-sprinter",
                9, 2, new[] { 5, 10, 10, 20 }));

            // Кривая опыта задаётся
            ExpBase = 10f;
            ExpMult = 5f;
            ExpEquationValue = 0.4f;
            QuadraticExperience = true;

            SurvivalAbilityBalance.Apply(this);
        }
    }
}
