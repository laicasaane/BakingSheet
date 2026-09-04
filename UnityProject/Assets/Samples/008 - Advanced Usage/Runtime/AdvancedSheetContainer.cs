using Cathei.BakingSheet.Unity;

namespace Cathei.BakingSheet.AdvancedExamples
{
    public sealed class AdvancedSheetContainer : SheetContainerBase
    {
        public AdvancedSheetContainer() : base(UnityLogger.Default) { }

        [Transpose]
        public DifficultyPresetSheet DifficultyPresets { get; private set; }

        public FactionProgressionSheet FactionProgressions { get; private set; }
        public DungeonWaveSheet DungeonWaves { get; private set; }
        public LootCatalogSheet LootCatalogs { get; private set; }
        public EncounterRewardSheet EncounterRewards { get; private set; }
        public ClassRotationSheet ClassRotations { get; private set; }
        public RaidBlueprintSheet RaidBlueprints { get; private set; }
        public HeroSheet Heroes { get; private set; }
        public EnemySheet Enemies { get; private set; }
    }
}
