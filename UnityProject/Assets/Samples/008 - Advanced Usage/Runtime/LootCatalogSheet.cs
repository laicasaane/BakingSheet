namespace Cathei.BakingSheet.AdvancedExamples
{
    public sealed class LootCatalogSheet : Sheet<LootCatalogSheet.Row>
    {
        public sealed class Row : SheetRow
        {
            public string DisplayName { get; private set; }

            public VerticalDictionary<string, VerticalDictionary<string, int>> WeightedEntries
            {
                get;
                private set;
            }
        }
    }
}
