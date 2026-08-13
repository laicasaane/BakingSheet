namespace Cathei.BakingSheet.AdvancedExamples
{
    public sealed class EncounterRewardSheet : Sheet<EncounterRewardSheet.Row>
    {
        public sealed class Row : SheetRow
        {
            public string DisplayName { get; private set; }
            public VerticalList<VerticalDictionary<string, int>> RewardBundles { get; private set; }
        }
    }
}
