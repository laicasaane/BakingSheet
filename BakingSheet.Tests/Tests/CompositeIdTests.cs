// BakingSheet, Maxwell Keonwoo Kang <code.athei@gmail.com>, 2022

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Cathei.BakingSheet.Raw;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Cathei.BakingSheet.Tests
{
    public class CompositeIdTests
    {
        [Fact]
        public async Task ImportSupportsTwoPartCompositeIdInSplitHeader()
        {
            using var fileSystem = new TestFileSystem();
            var logger = new TestLogger();
            var container = new TwoPartContainer(logger);
            var converter = CreateConverter(fileSystem);
            fileSystem.SetTestData(
                TestPath(nameof(TwoPartContainer.Rows)),
                "Id,,Data\n" +
                "Kind,SubId,Name\n" +
                "Enemy,2,Goblin\n" +
                "Item,7,Potion\n");

            var result = await container.Bake(converter);

            logger.VerifyNoError();
            Assert.True(result);
            Assert.Equal(2, container.Rows.Count);
            Assert.Equal("Enemy", container.Rows[0].Id.Kind);
            Assert.Equal(2, container.Rows[0].Id.SubId);
            Assert.Equal("Goblin", container.Rows[0].Data.Name);
            Assert.Equal("Item", container.Rows[1].Id.Kind);
            Assert.Equal(7, container.Rows[1].Id.SubId);
            Assert.Equal("Potion", container.Rows[1].Data.Name);
            Assert.Same(container.Rows[0], container.Rows[new TwoPartId("Enemy", 2)]);
        }

        [Theory]
        [InlineData(HeaderMode.Hybrid)]
        [InlineData(HeaderMode.Split)]
        [InlineData(HeaderMode.Flat)]
        public async Task RoundTripSupportsNestedCompositeIdInEveryHeaderMode(HeaderMode mode)
        {
            using var fileSystem = new TestFileSystem();
            var logger = new TestLogger();
            var source = CreateNestedContainer(logger);
            var exporter = CreateConverter(fileSystem);
            exporter.HeaderMode = mode;

            var exportResult = await source.Store(exporter);
            string csv = GetTestData(fileSystem, TestPath(nameof(NestedContainer.Rows)));

            logger.VerifyNoError();
            Assert.True(exportResult);
            Assert.StartsWith(GetExpectedNestedHeader(mode), csv);

            fileSystem.SetTestData(TestPath(nameof(NestedContainer.Rows)), csv);
            var imported = new NestedContainer(logger);
            var importResult = await imported.Bake(CreateConverter(fileSystem));

            logger.VerifyNoError();
            Assert.True(importResult);
            Assert.Equal(2, imported.Rows.Count);
            Assert.Equal("Enemy", imported.Rows[0].Id.EntityId.Kind);
            Assert.Equal(2, imported.Rows[0].Id.EntityId.SubId);
            Assert.Equal(3, imported.Rows[0].Id.Rarity);
            Assert.Equal("Goblin", imported.Rows[0].Data.Metadata.Display.Name);
            Assert.Same(
                imported.Rows[1],
                imported.Rows[new NestedId(new TwoPartId("Item", 7), 4)]);
        }

        [Fact]
        public async Task RoundTripMapsEveryCompositeIdComponent()
        {
            using var fileSystem = new TestFileSystem();
            var logger = new TestLogger();
            var source = CreateNestedContainer(logger);
            var exporter = new MappingCsvSheetConverter("testdata", fileSystem)
            {
                HeaderMode = HeaderMode.Flat,
            };

            var exportResult = await source.Store(exporter);
            string csv = GetTestData(fileSystem, TestPath(nameof(NestedContainer.Rows)));

            logger.VerifyNoError();
            Assert.True(exportResult);
            Assert.StartsWith(
                "id:entity_id:Kind,id:entity_id:sub_id,id:rarity,Data:Metadata:Display:Name\n",
                csv);

            fileSystem.SetTestData(TestPath(nameof(NestedContainer.Rows)), csv);
            var imported = new NestedContainer(logger);
            var importResult = await imported.Bake(
                new MappingCsvSheetConverter("testdata", fileSystem));

            logger.VerifyNoError();
            Assert.True(importResult);
            var row = imported.Rows[new NestedId(new TwoPartId("Enemy", 2), 3)];
            Assert.NotNull(row);
            Assert.Equal("Enemy", row.Id.EntityId.Kind);
            Assert.Equal(2, row.Id.EntityId.SubId);
            Assert.Equal(3, row.Id.Rarity);
        }

        [Fact]
        public async Task ImportSupportsTransposedCompositeId()
        {
            using var fileSystem = new TestFileSystem();
            var logger = new TestLogger();
            var container = new TransposedContainer(logger);
            fileSystem.SetTestData(
                TestPath(nameof(TransposedContainer.Rows)),
                "Id,Kind,Enemy,Item\n" +
                ",SubId,2,7\n" +
                "Data,Name,Goblin,Potion\n");

            var result = await container.Bake(CreateConverter(fileSystem));

            logger.VerifyNoError();
            Assert.True(result);
            Assert.Equal(2, container.Rows.Count);
            Assert.Equal("Enemy", container.Rows[0].Id.Kind);
            Assert.Equal(2, container.Rows[0].Id.SubId);
            Assert.Equal("Goblin", container.Rows[0].Data.Name);
            Assert.Equal("Item", container.Rows[1].Id.Kind);
            Assert.Equal(7, container.Rows[1].Id.SubId);
            Assert.Equal("Potion", container.Rows[1].Data.Name);
        }

        [Theory]
        [InlineData("Key:Kind,Data\nEnemy,Goblin\n", "First column \"Key:Kind\" must be named \"Id\"")]
        [InlineData("$ Header,Data\nEnemy,Goblin\n", "Invalid sheet header at cell \"A1\".")]
        [InlineData("Id::Kind,Data\nEnemy,Goblin\n", "Invalid sheet header at cell \"A1\".")]
        [InlineData("Id,Data\nMissing,Name\nEnemy,Goblin\n", "Invalid sheet header at cell \"A2\".")]
        [InlineData("Id,Data\n,Name\nEnemy,Goblin\n", "Invalid sheet header at cell \"A1\".")]
        public async Task ImportRejectsInvalidOrIncompleteCompositeIdHeader(
            string csv, string expectedMessage)
        {
            using var fileSystem = new TestFileSystem();
            var logger = new TestLogger();
            var container = new TwoPartContainer(logger);
            fileSystem.SetTestData(TestPath(nameof(TwoPartContainer.Rows)), csv);

            var result = await container.Bake(CreateConverter(fileSystem));

            Assert.True(result);
            Assert.Empty(container.Rows);
            logger.VerifyLog(
                LogLevel.Error,
                expectedMessage,
                new[] { nameof(TwoPartContainer.Rows) });
        }

        [Fact]
        public async Task ImportRejectsValueEqualDuplicateCompositeIds()
        {
            using var fileSystem = new TestFileSystem();
            var logger = new TestLogger();
            var container = new TwoPartContainer(logger);
            fileSystem.SetTestData(
                TestPath(nameof(TwoPartContainer.Rows)),
                "Id:Kind,Id:SubId,Data:Name\n" +
                "Enemy,2,First\n" +
                "Enemy,2,Second\n");

            var result = await container.Bake(CreateConverter(fileSystem));

            Assert.True(result);
            Assert.Single(container.Rows);
            Assert.Equal("First", container.Rows[0].Data.Name);
            logger.VerifyLog(
                LogLevel.Error,
                "Already has row with id \"Enemy:2\"",
                new object[] { nameof(TwoPartContainer.Rows), "Enemy" });
        }

        private static CsvSheetConverter CreateConverter(TestFileSystem fileSystem)
        {
            return new CsvSheetConverter(
                "testdata", TimeZoneInfo.Utc, fileSystem: fileSystem);
        }

        private static NestedContainer CreateNestedContainer(TestLogger logger)
        {
            var container = new NestedContainer(logger)
            {
                Rows = new NestedSheet
                {
                    new NestedRow
                    {
                        Id = new NestedId(new TwoPartId("Enemy", 2), 3),
                        Data = new RowData
                        {
                            Metadata = new RowMetadata
                            {
                                Display = new RowDisplay { Name = "Goblin" },
                            },
                        },
                    },
                    new NestedRow
                    {
                        Id = new NestedId(new TwoPartId("Item", 7), 4),
                        Data = new RowData
                        {
                            Metadata = new RowMetadata
                            {
                                Display = new RowDisplay { Name = "Potion" },
                            },
                        },
                    },
                },
            };
            container.PostLoad();
            return container;
        }

        private static string GetExpectedNestedHeader(HeaderMode mode)
        {
            switch (mode)
            {
                case HeaderMode.Hybrid:
                case HeaderMode.Split:
                    return "Id,,,Data\n" +
                           "EntityId,,Rarity,Metadata\n" +
                           "Kind,SubId,,Display\n" +
                           ",,,Name\n";
                case HeaderMode.Flat:
                    return "Id:EntityId:Kind,Id:EntityId:SubId,Id:Rarity," +
                           "Data:Metadata:Display:Name\n";
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode));
            }
        }

        private static string GetTestData(TestFileSystem fileSystem, string path)
        {
            return Encoding.UTF8.GetString(fileSystem.files[path].ToArray())
                .Replace("\r\n", "\n");
        }

        private static string TestPath(string sheetName)
        {
            return Path.Combine("testdata", $"{sheetName}.csv");
        }

        private sealed class TwoPartId : IEquatable<TwoPartId>
        {
            public string Kind { get; set; }
            public int SubId { get; set; }

            public TwoPartId() { }

            public TwoPartId(string kind, int subId)
            {
                Kind = kind;
                SubId = subId;
            }

            public bool Equals(TwoPartId other)
                => other != null &&
                   string.Equals(Kind, other.Kind, StringComparison.Ordinal) &&
                   SubId == other.SubId;

            public override bool Equals(object obj)
                => obj is TwoPartId other && Equals(other);

            public override int GetHashCode()
                => ((Kind?.GetHashCode() ?? 0) * 397) ^ SubId;

            public override string ToString()
                => $"{Kind}:{SubId}";
        }

        private sealed class NestedId : IEquatable<NestedId>
        {
            public TwoPartId EntityId { get; set; }
            public int Rarity { get; set; }

            public NestedId() { }

            public NestedId(TwoPartId entityId, int rarity)
            {
                EntityId = entityId;
                Rarity = rarity;
            }

            public bool Equals(NestedId other)
                => other != null &&
                   Equals(EntityId, other.EntityId) &&
                   Rarity == other.Rarity;

            public override bool Equals(object obj)
                => obj is NestedId other && Equals(other);

            public override int GetHashCode()
                => ((EntityId?.GetHashCode() ?? 0) * 397) ^ Rarity;
        }

        private sealed class RowDisplay
        {
            public string Name { get; set; }
        }

        private sealed class RowMetadata
        {
            public RowDisplay Display { get; set; }
        }

        private sealed class RowData
        {
            public RowMetadata Metadata { get; set; }
        }

        private sealed class TwoPartData
        {
            public string Name { get; set; }
        }

        private sealed class TwoPartRow : SheetRow<TwoPartId>
        {
            public TwoPartData Data { get; set; }
        }

        private sealed class NestedRow : SheetRow<NestedId>
        {
            public RowData Data { get; set; }
        }

        private sealed class TwoPartSheet : Sheet<TwoPartId, TwoPartRow> { }
        private sealed class NestedSheet : Sheet<NestedId, NestedRow> { }

        private sealed class TwoPartContainer : SheetContainerBase
        {
            public TwoPartSheet Rows { get; set; }

            public TwoPartContainer(ILogger logger) : base(logger) { }
        }

        private sealed class NestedContainer : SheetContainerBase
        {
            public NestedSheet Rows { get; set; }

            public NestedContainer(ILogger logger) : base(logger) { }
        }

        private sealed class TransposedContainer : SheetContainerBase
        {
            [Transpose]
            public TwoPartSheet Rows { get; set; }

            public TransposedContainer(ILogger logger) : base(logger) { }
        }

        private sealed class MappingCsvSheetConverter : CsvSheetConverter
        {
            public MappingCsvSheetConverter(string loadPath, TestFileSystem fileSystem)
                : base(loadPath, TimeZoneInfo.Utc, fileSystem: fileSystem) { }

            protected override string ToPropertyName(
                System.Reflection.PropertyInfo sheetProperty, ISheet sheet, string externalName)
            {
                switch (externalName)
                {
                    case "id": return "Id";
                    case "entity_id": return "EntityId";
                    case "sub_id": return "SubId";
                    case "rarity": return "Rarity";
                    default: return externalName;
                }
            }

            protected override string ToExternalName(
                System.Reflection.PropertyInfo sheetProperty, ISheet sheet, string propertyName)
            {
                switch (propertyName)
                {
                    case "Id": return "id";
                    case "EntityId": return "entity_id";
                    case "SubId": return "sub_id";
                    case "Rarity": return "rarity";
                    default: return propertyName;
                }
            }
        }
    }
}
