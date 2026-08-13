// BakingSheet, Maxwell Keonwoo Kang <code.athei@gmail.com>, 2022

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Cathei.BakingSheet.Tests
{
    public class TestSheet : Sheet<TestSheet.Row>
    {
        public class Row : SheetRow
        {
            public string Content { get; set; }
        }
    }

    public class TestArraySheet : Sheet<TestArraySheet.Row>
    {
        public class Elem : SheetRowElem
        {
            public string ElemContent { get; set; }
        }

        public class Row : SheetRowArray<Elem>
        {
            public string Content { get; set; }
        }
    }

    public class TestNestedSheet : Sheet<TestNestedSheet.Row>
    {
        public struct NestedStruct
        {
            public int XInt { get; set; }
            public float YFloat { get; set; }
            public List<string> ZList { get; set; }
        }

        public class Elem : SheetRowElem
        {
            public List<int> IntList { get; set; }
        }

        public class Row : SheetRowArray<Elem>
        {
            public NestedStruct Struct { get; set; }
            public List<NestedStruct> StructList { get; set; }
        }
    }

    public enum TestEnum
    {
        Alpha, Bravo, Charlie
    }

    public class TestTypeSheet : Sheet<TestEnum, TestTypeSheet.Row>
    {
        public class Row : SheetRow<TestEnum>
        {
            [SheetValueConverter(typeof(MyIntConverter))]
            public int IntColumn { get; set; }
            public float FloatColumn { get; set; }
            public decimal DecimalColumn { get; set; }
            public DateTime DateTimeColumn { get; set; }
            public TimeSpan TimeSpanColumn { get; set; }
            public TestEnum? EnumColumn { get; set; }
        }
    }

    public class MyIntConverter : SheetValueConverter<int>
    {
        protected override int StringToValue(Type type, string value, SheetValueConvertingContext context)
        {
            return int.Parse(value) - 1;
        }

        protected override string ValueToString(Type type, int value, SheetValueConvertingContext context)
        {
            return (value + 1).ToString();
        }
    }

    public class TestReferenceSheet : Sheet<TestReferenceSheet.Row>
    {
        public class Elem : SheetRowElem
        {
            public TestSheet.Reference NestedReferColumn { get; set; }
        }

        public class Row : SheetRowArray<Elem>
        {
            public TestSheet.Reference ReferColumn { get; set; }
            public TestReferenceSheet.Reference SelfReferColumn { get; set; }
            public List<TestSheet.Reference> ReferList { get; set; }
        }
    }

    public class TestDictSheet : Sheet<TestDictSheet.Row>
    {
        public class Elem : SheetRowElem
        {
            public Dictionary<int, List<string>> NestedDict { get; set; }
            public int Value { get; set; }
        }

        public class Row : SheetRowArray<Elem>
        {
            public Dictionary<string, float> Dict { get; set; }
        }
    }

    public class TestVerticalSheet : Sheet<TestVerticalSheet.Row>
    {
        public class Elem : SheetRowElem
        {
            public string Value { get; set; }
        }

        public struct NestedStruct
        {
            public int X { get; set; }
            public int Y { get; set; }
        }

        public class Row : SheetRowArray<Elem>
        {
            public VerticalList<NestedStruct> Coord { get; set; }
            public List<VerticalList<int>> Levels { get; set; }
        }
    }

    public class TestVerticalDictionarySheet : Sheet<TestVerticalDictionarySheet.Row>
    {
        public struct CompositeKey
        {
            public string Name { get; set; }
            public int Level { get; set; }

            public override string ToString() => $"{Name}:{Level}";
        }

        public struct CompositeValue
        {
            public int Amount { get; set; }
            public string Label { get; set; }
        }

        public class Row : SheetRow
        {
            public VerticalDictionary<string, int> Primitive { get; set; }
            public VerticalDictionary<int, int> Numeric { get; set; }
            public VerticalDictionary<string, int> CaseInsensitive { get; set; } =
                new VerticalDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            public VerticalDictionary<CompositeKey, CompositeValue> Composite { get; set; }
            public VerticalDictionary<string, VerticalList<int>> Lists { get; set; }
            public VerticalDictionary<string, VerticalDictionary<string, int>> Nested { get; set; }
            public VerticalDictionary<string, VerticalDictionary<string, VerticalList<int>>> NestedLists { get; set; }
            public VerticalDictionary<int, VerticalDictionary<int, VerticalList<int>>> NestedNumeric { get; set; }
        }
    }

    public class TestVerticalDictionaryExportSheet : Sheet<TestVerticalDictionaryExportSheet.Row>
    {
        public class Row : SheetRow
        {
            public VerticalDictionary<string, VerticalDictionary<string, int>> Rewards { get; set; }
            public VerticalList<int> Other { get; set; }
        }
    }

    public class TestUnsupportedVerticalDictionarySheet : Sheet<TestUnsupportedVerticalDictionarySheet.Row>
    {
        public class NoExportableLeaf
        {
        }

        public class Row : SheetRow
        {
            public VerticalDictionary<VerticalList<int>, int> VerticalKey { get; set; }
            public VerticalDictionary<TestSheet.Reference, int> ReferenceKey { get; set; }
            public VerticalList<NoExportableLeaf> NoExportableLeaf { get; set; }
            public VerticalDictionary<string, int> Valid { get; set; }
        }
    }

    public class TestVerticalCollectionNestingSheet : Sheet<TestVerticalCollectionNestingSheet.Row>
    {
        public class DictionaryValue
        {
            public VerticalList<int> ListValues { get; set; }
            public VerticalDictionary<string, int> DictionaryValues { get; set; }
        }

        public class DictionaryBranch
        {
            public VerticalDictionary<string, DictionaryValue> Entries { get; set; }
        }

        public class ListBranch
        {
            public VerticalList<int> ListValues { get; set; }
            public VerticalDictionary<string, int> DictionaryValues { get; set; }
        }

        public class Root
        {
            public List<ListBranch> ListBranches { get; set; }
            public Dictionary<string, DictionaryBranch> DictionaryBranches { get; set; }
        }

        public class Row : SheetRow
        {
            public VerticalList<Root> Roots { get; set; }
            public VerticalList<VerticalList<int>> EmptyItems { get; set; }
            public VerticalList<int> Legacy { get; set; }
        }
    }

    public class TestInvalidMarkerSheet : Sheet<TestInvalidMarkerSheet.Row>
    {
        public class Row : SheetRow
        {
            public VerticalList<int> Values { get; set; }
            public string Content { get; set; }
        }
    }

    public class TestExplicitVerticalCollectionSheet : Sheet<TestExplicitVerticalCollectionSheet.Row>
    {
        public class Reward
        {
            public string Item { get; set; }
            public int Amount { get; set; }
        }

        public class Stage
        {
            public string Name { get; set; }
            public VerticalList<VerticalList<Reward>> RewardPools { get; set; }
        }

        public class Row : SheetRow
        {
            public VerticalList<VerticalList<VerticalList<Stage>>> Stages { get; set; }
            public VerticalList<VerticalDictionary<string, int>> WaveRewards { get; set; }
            public string Content { get; set; }
        }
    }

    public class TestVerticalDictionaryReferenceSheet : Sheet<TestVerticalDictionaryReferenceSheet.Row>
    {
        public class Row : SheetRow
        {
            public VerticalDictionary<string, TestSheet.Reference> References { get; set; }
            public VerticalDictionary<string, string> Text { get; set; }
        }
    }

    public class TestVerticalDictionaryJsonSheet : Sheet<TestVerticalDictionaryJsonSheet.Row>
    {
        public class Row : SheetRow
        {
            public VerticalDictionary<string, int> Values { get; set; }
        }
    }

    public class InheritBaseRow : SheetRow
    {
        // checking if private setter works as intended
        public int Value { get; private set; }
    }

    public class InheritedSheet : Sheet<InheritedSheet.Row>
    {
        public class Row : InheritBaseRow
        {
        }
    }

    public class TestSheetContainer : SheetContainerBase
    {
        public TestSheetContainer(ILogger logger) : base(logger) { }

        public TestSheet Tests { get; set; }
        public TestArraySheet Arrays { get; set; }
        public TestTypeSheet Types { get; set; }
        public TestReferenceSheet Refers { get; set; }
        public TestNestedSheet Nested { get; set; }
        public TestDictSheet Dict { get; set; }
        public TestVerticalSheet Vertical { get; set; }
        public TestVerticalDictionarySheet VerticalDictionary { get; set; }
        public TestVerticalDictionaryExportSheet VerticalDictionaryExport { get; set; }
        public TestUnsupportedVerticalDictionarySheet UnsupportedVerticalDictionary { get; set; }
        public TestVerticalDictionaryReferenceSheet VerticalDictionaryReferences { get; set; }
        public TestVerticalDictionaryJsonSheet VerticalDictionaryJson { get; set; }
        public TestVerticalCollectionNestingSheet VerticalCollectionNesting { get; set; }
        public TestInvalidMarkerSheet InvalidMarker { get; set; }
        public TestExplicitVerticalCollectionSheet ExplicitVerticalCollection { get; set; }
        public InheritedSheet Inherited { get; set; }
    }
}
