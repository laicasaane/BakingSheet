// BakingSheet, Maxwell Keonwoo Kang <code.athei@gmail.com>, 2022

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Cathei.BakingSheet.Tests
{
    public class RawHeaderCommentTests
    {
        private const string ConsumerSheetCsv =
            "id,,$,lineup_info,,,,,,,,,,,,$đánh_số$,\n" +
            "map,stage,,id,$mô_tả$,position,,enemy,loot,,box,,modifier,,,\"Giá trị X là số cột\nGiá trị Y là số dòng\"\n" +
            ",,,,,x,y,,type,amount,add_amount,add_limit,hp,power,,,\n" +
            "$,,,,,,,,,,,,,,,,\n" +
            "1,1,,1XY,1 unit,1,1,1,Currency1,1,2,1,1,1,,,\n" +
            ",,,1XY,1 unit,2,1,1,Currency1,1,2,1,1,1,,,\n" +
            "1,2,,2XY,$$ note,1,2,3,Currency2,4,5,6,7,8,,,\n";

        private const string CompactData =
            "1,1,ignored,A,ignored,3,4,7\n" +
            ",,ignored,B,ignored,5,6,8\n";

        [Fact]
        public async Task ImportIgnoresNestedCommentHeaderInConsumerSheet()
        {
            using var fileSystem = new TestFileSystem();
            var logger = new TestLogger();
            var container = new StageContainer(logger);
            fileSystem.SetTestData(Path.Combine("testdata", "map_stages.1.csv"), ConsumerSheetCsv);

            var result = await container.Bake(new SnakeCaseCsvSheetConverter(fileSystem));

            logger.VerifyNoError();
            Assert.True(result);
            Assert.Equal(2, container.MapStages.Count);

            var first = container.MapStages[new StageId(1, 1)];
            Assert.Equal(2, first.LineupInfo.Count);
            Assert.Equal("1XY", first.LineupInfo[0].Id);
            Assert.Equal(1, first.LineupInfo[0].Position.X);
            Assert.Equal(1, first.LineupInfo[0].Position.Y);
            Assert.Equal(2, first.LineupInfo[1].Position.X);
            Assert.Equal("Currency1", first.LineupInfo[0].Loot.Type);
            Assert.Equal(2, first.LineupInfo[0].Box.AddAmount);
            Assert.Equal(1, first.LineupInfo[0].Box.AddLimit);

            var second = container.MapStages[new StageId(1, 2)];
            Assert.Single(second.LineupInfo);
            Assert.Equal(2, second.LineupInfo[0].Position.Y);
            Assert.Equal(3, second.LineupInfo[0].Enemy);
            Assert.Equal(4f, second.LineupInfo[0].Loot.Amount);
            Assert.Equal(7, second.LineupInfo[0].Modifier.Hp);
            Assert.Equal(8, second.LineupInfo[0].Modifier.Power);
        }

        [Theory]
        [InlineData(
            "Id,,$,LineupInfo,,,,\n" +
            "Map,Stage,,Id,$Note,Position,,Enemy\n" +
            ",,,,,X,Y,\n", 3, 4, 7)]
        [InlineData(
            "Id,,$,LineupInfo,,,,\n" +
            "Map,Stage,,Id, \t$Note,Position,,Enemy\n" +
            ",,,,,X,Y,\n", 3, 4, 7)]
        [InlineData(
            "Id,,$,LineupInfo,,,,\n" +
            "Map,Stage,,Id,$$Note,Position,,Enemy\n" +
            ",,,,,X,Y,\n", 3, 4, 7)]
        [InlineData(
            "Id,,$,LineupInfo,,,,\n" +
            "Map,Stage,,Id,$Note,Position,,Enemy\n" +
            ",,,,Child,X,Y,\n", 3, 4, 7)]
        [InlineData(
            "Id,,$,LineupInfo,,,,\n" +
            "Map,Stage,,Id,Position,,,Enemy\n" +
            ",,,,$Z,X,Y,\n", 3, 4, 7)]
        [InlineData(
            "Id,,$,LineupInfo,,,,\n" +
            "Map,Stage,,Id,Position:$Z$,Position,,Enemy\n" +
            ",,,,,X,Y,\n", 3, 4, 7)]
        [InlineData(
            "Id,,$,LineupInfo,,,,\n" +
            "Map,Stage,,Id,$Note,,Position,Enemy\n" +
            ",,,,A,B,X,\n", 4, 0, 7)]
        [InlineData(
            "Id,,$,LineupInfo,,,,\n" +
            "Map,Stage,,Id,$Note,$Position,,Enemy\n" +
            ",,,,,X,Y,\n", 0, 0, 7)]
        public async Task ImportIgnoresCommentCellAtAnyHeaderLevel(
            string header, int expectedX, int expectedY, int expectedEnemy)
        {
            using var fileSystem = new TestFileSystem();
            var logger = new TestLogger();
            var container = new StageContainer(logger);
            fileSystem.SetTestData(
                Path.Combine("testdata", "MapStages.csv"), header + CompactData);

            var result = await container.Bake(CreateConverter(fileSystem));

            logger.VerifyNoError();
            Assert.True(result);
            var row = Assert.Single(container.MapStages);
            Assert.Equal(2, row.LineupInfo.Count);
            Assert.Equal("A", row.LineupInfo[0].Id);
            Assert.Equal(expectedX, row.LineupInfo[0].Position.X);
            Assert.Equal(expectedY, row.LineupInfo[0].Position.Y);
            Assert.Equal(expectedEnemy, row.LineupInfo[0].Enemy);
            Assert.Equal("B", row.LineupInfo[1].Id);
        }

        [Theory]
        [InlineData("Id,Prices:$Note$,Prices:EUR,Tail\nA,ignored,2,3\n")]
        [InlineData("Id,Prices: $Note$ ,Prices:EUR,Tail\nA,ignored,2,3\n")]
        [InlineData("Id,Prices:EUR, \t$ Note,Tail\nA,2,ignored,3\n")]
        [InlineData("Id,Prices,,Tail\n,$USD,EUR,\nA,ignored,2,3\n")]
        public async Task ImportIgnoresCommentInDictionaryKeyPosition(string csv)
        {
            using var fileSystem = new TestFileSystem();
            var logger = new TestLogger();
            var container = new PriceContainer(logger);
            fileSystem.SetTestData(Path.Combine("testdata", "Prices.csv"), csv);

            var result = await container.Bake(CreateConverter(fileSystem));

            logger.VerifyNoError();
            Assert.True(result);
            var row = Assert.Single(container.Prices);
            Assert.Equal(new[] { "EUR" }, row.Prices.Keys.ToArray());
            Assert.Equal(2, row.Prices["EUR"]);
            Assert.Equal(3, row.Tail);
        }

        [Fact]
        public async Task ImportKeepsDollarPrefixedKeyInFlatDictionaryHeader()
        {
            using var fileSystem = new TestFileSystem();
            var logger = new TestLogger();
            var container = new PriceContainer(logger);
            fileSystem.SetTestData(
                Path.Combine("testdata", "Prices.csv"),
                "Id,Prices:$USD,Prices:EUR,Tail\nA,1,2,3\n");

            var result = await container.Bake(CreateConverter(fileSystem));

            logger.VerifyNoError();
            Assert.True(result);
            var row = Assert.Single(container.Prices);
            Assert.Equal(new[] { "$USD", "EUR" }, row.Prices.Keys.OrderBy(x => x, StringComparer.Ordinal).ToArray());
            Assert.Equal(1, row.Prices["$USD"]);
            Assert.Equal(2, row.Prices["EUR"]);
            Assert.Equal(3, row.Tail);
        }

        [Fact]
        public async Task ImportRejectsCommentCellInIdHeaderPath()
        {
            using var fileSystem = new TestFileSystem();
            var logger = new TestLogger();
            var container = new StageContainer(logger);
            fileSystem.SetTestData(
                Path.Combine("testdata", "MapStages.csv"),
                "Id,,LineupInfo\n" +
                "$Map,Stage,Id\n" +
                "1,1,A\n");

            var result = await container.Bake(CreateConverter(fileSystem));

            Assert.True(result);
            Assert.Empty(container.MapStages);
            logger.VerifyLog(
                LogLevel.Error,
                "Sheet MapStages (property MapStages), page (default), cell A2: invalid header $Map. " +
                "The Id header path cannot contain a comment prefix. Remove $ from the first column header.");
        }

        private static CsvSheetConverter CreateConverter(TestFileSystem fileSystem)
        {
            return new CsvSheetConverter("testdata", TimeZoneInfo.Utc, fileSystem: fileSystem);
        }

        private sealed class StageId : IEquatable<StageId>
        {
            public int Map { get; set; }
            public int Stage { get; set; }

            public StageId() { }

            public StageId(int map, int stage)
            {
                Map = map;
                Stage = stage;
            }

            public bool Equals(StageId other)
                => other != null && Map == other.Map && Stage == other.Stage;

            public override bool Equals(object obj)
                => obj is StageId other && Equals(other);

            public override int GetHashCode()
                => (Map * 397) ^ Stage;

            public override string ToString()
                => $"{Map}:{Stage}";
        }

        private struct LineupPosition
        {
            public int X { get; set; }
            public int Y { get; set; }
        }

        private struct LineupLoot
        {
            public string Type { get; set; }
            public float Amount { get; set; }
        }

        private struct LineupBox
        {
            public int AddAmount { get; set; }
            public int AddLimit { get; set; }
        }

        private struct LineupModifier
        {
            public int Hp { get; set; }
            public int Power { get; set; }
        }

        private struct Lineup
        {
            public string Id { get; set; }
            public LineupPosition Position { get; set; }
            public int Enemy { get; set; }
            public LineupLoot Loot { get; set; }
            public LineupBox Box { get; set; }
            public LineupModifier Modifier { get; set; }
        }

        private sealed class StageRow : SheetRow<StageId>
        {
            public VerticalList<Lineup> LineupInfo { get; set; }
        }

        private sealed class StageSheet : Sheet<StageId, StageRow> { }

        private sealed class StageContainer : SheetContainerBase
        {
            public StageSheet MapStages { get; set; }

            public StageContainer(ILogger logger) : base(logger) { }
        }

        private sealed class PriceRow : SheetRow
        {
            public Dictionary<string, int> Prices { get; set; }
            public int Tail { get; set; }
        }

        private sealed class PriceSheet : Sheet<PriceRow> { }

        private sealed class PriceContainer : SheetContainerBase
        {
            public PriceSheet Prices { get; set; }

            public PriceContainer(ILogger logger) : base(logger) { }
        }

        private sealed class SnakeCaseCsvSheetConverter : CsvSheetConverter
        {
            public SnakeCaseCsvSheetConverter(TestFileSystem fileSystem)
                : base("testdata", TimeZoneInfo.Utc, fileSystem: fileSystem) { }

            protected override string GetImportSheetName(PropertyInfo sheetProperty)
            {
                return "map_stages";
            }

            protected override string ToPropertyName(
                PropertyInfo sheetProperty, ISheet sheet, string externalName)
            {
                return string.Concat(externalName
                    .Split('_')
                    .Select(x => x.Length == 0 ? x : char.ToUpperInvariant(x[0]) + x.Substring(1)));
            }
        }
    }
}
