namespace Cathei.BakingSheet.AdvancedExamples
{
    public sealed class DifficultyPresetSheet : Sheet<DifficultyPresetSheet.Row>
    {
        public sealed class Row : SheetRow
        {
            public string DisplayName { get; private set; }
            public float EnemyHealthMultiplier { get; private set; }
            public float EnemyDamageMultiplier { get; private set; }
            public float ExperienceMultiplier { get; private set; }
            public float LootQuantityMultiplier { get; private set; }
            public float GoldMultiplier { get; private set; }
            public int ReviveTokenCount { get; private set; }
            public float CheckpointHealingPercent { get; private set; }
            public bool FriendlyFire { get; private set; }
            public int EnemyLevelOffset { get; private set; }
            public int EliteAffixCount { get; private set; }
        }
    }
}
