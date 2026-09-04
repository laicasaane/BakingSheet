namespace Cathei.BakingSheet.AdvancedExamples
{
    public sealed class HeroSheet : Sheet<HeroSheet.Id, HeroSheet.Row>
    {
        public sealed class Id
        {
            public CharacterKind Kind { get; private set; }
            public int SubId { get; private set; }
        }

        public sealed class Row : SheetRow<Id>
        {
            public string Name { get; private set; }
        }
    }
}
