namespace Cathei.BakingSheet.AdvancedExamples
{
    public sealed class ClassRotationSheet : Sheet<ClassRotationSheet.Row>
    {
        public sealed class Row : SheetRow
        {
            public string DisplayName { get; private set; }
            public VerticalDictionary<string, VerticalList<string>> Rotations { get; private set; }
        }
    }
}
