namespace Cathei.BakingSheet.AdvancedExamples
{
    public sealed class FactionProgressionSheet : Sheet<FactionProgressionSheet.Row>
    {
        public sealed class Row : SheetRow
        {
            public string DisplayName { get; private set; }
            public string Region { get; private set; }
            public VerticalDictionary<string, int> RenownThresholds { get; private set; }
        }
    }
}
