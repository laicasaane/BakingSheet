namespace Cathei.BakingSheet.AdvancedExamples
{
    public sealed class DungeonWaveSheet : Sheet<DungeonWaveSheet.Row>
    {
        public sealed class Row : SheetRow
        {
            public string DisplayName { get; private set; }
            public int RecommendedPower { get; private set; }
            public VerticalList<VerticalList<VerticalList<string>>> Stages { get; private set; }
        }
    }
}
