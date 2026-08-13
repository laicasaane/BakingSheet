namespace Cathei.BakingSheet.AdvancedExamples
{
    public sealed class RaidBlueprintSheet : Sheet<RaidBlueprintSheet.Row>
    {
        public sealed class Row : SheetRow
        {
            public string DisplayName { get; private set; }
            public int MinimumPower { get; private set; }
            public VerticalList<VerticalList<VerticalList<VerticalList<RaidEnemy>>>> Acts { get; private set; }
        }
    }

    public sealed class RaidEnemy
    {
        public string Name { get; private set; }
        public string Role { get; private set; }
        public VerticalList<VerticalDictionary<string, int>> RewardPools { get; private set; }
    }
}
