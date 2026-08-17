// BakingSheet, Maxwell Keonwoo Kang <code.athei@gmail.com>, 2022

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Cathei.BakingSheet.Raw;
using Xunit;

namespace Cathei.BakingSheet.Tests
{
    public class RawSheetExtensibilityTests
    {
        [Fact]
        public void HooksExposeProtectedVirtualIdentityContract()
        {
            var context = new SheetConvertingContext
            {
                Container = new HookContainer(new TestLogger()),
                Logger = new TestLogger(),
            };
            var sheetProperty = typeof(HookContainer).GetProperty(nameof(HookContainer.Primary));
            var sheet = new HookSheet { Name = "Canonical" };
            var converter = new IdentityRawSheetConverter();

            var shouldProcessSheet = GetHook(
                typeof(RawSheetImporter), "ShouldProcessSheet",
                typeof(SheetConvertingContext), typeof(PropertyInfo));
            var getImportSheetName = GetHook(
                typeof(RawSheetImporter), "GetImportSheetName", typeof(PropertyInfo));
            var toPropertyName = GetHook(
                typeof(RawSheetImporter), "ToPropertyName",
                typeof(PropertyInfo), typeof(ISheet), typeof(string));
            var getExportSheetName = GetHook(
                typeof(RawSheetConverter), "GetExportSheetName",
                typeof(PropertyInfo), typeof(ISheet));
            var toExternalName = GetHook(
                typeof(RawSheetConverter), "ToExternalName",
                typeof(PropertyInfo), typeof(ISheet), typeof(string));

            Assert.True((bool)shouldProcessSheet.Invoke(
                converter, new object[] { context, sheetProperty }));
            Assert.Equal(nameof(HookContainer.Primary),
                getImportSheetName.Invoke(converter, new object[] { sheetProperty }));
            Assert.Equal("external_name",
                toPropertyName.Invoke(
                    converter, new object[] { sheetProperty, sheet, "external_name" }));
            Assert.Equal("Canonical",
                getExportSheetName.Invoke(
                    converter, new object[] { sheetProperty, sheet }));
            Assert.Equal("PropertyName",
                toExternalName.Invoke(
                    converter, new object[] { sheetProperty, sheet, "PropertyName" }));
        }

        [Fact]
        public async Task ImportSkipsUnselectedSheetsAndUsesExternalSheetName()
        {
            var logger = new TestLogger();
            var container = new HookContainer(logger);
            var converter = new RecordingRawSheetConverter
            {
                ProcessSheet = name => name == nameof(HookContainer.Primary),
                ImportSheetName = _ => "source-primary",
            };
            converter.AddImportPage(
                "source-primary",
                new[] { "Id", "Value" },
                new[] { "Row", "Imported" });

            var result = await converter.Import(CreateContext(container, logger));

            Assert.True(result);
            Assert.Equal(
                new[]
                {
                    nameof(HookContainer.Complex),
                    nameof(HookContainer.Primary),
                    nameof(HookContainer.Skipped),
                    nameof(HookContainer.TransposedComplex),
                },
                converter.ShouldProcessCalls.OrderBy(x => x));
            Assert.Equal(new[] { nameof(HookContainer.Primary) }, converter.ImportNameCalls);
            Assert.Equal(new[] { "source-primary" }, converter.GetPagesCalls);
            Assert.Equal("Imported", container.Primary["Row"].Value);
            Assert.Null(container.Skipped);
        }

        [Fact]
        public async Task ExportSkipsUnselectedSheetsAndUsesExternalSheetName()
        {
            var logger = new TestLogger();
            var container = new HookContainer(logger)
            {
                Primary = CreateHookSheet("Primary"),
                Skipped = CreateHookSheet("Skipped"),
            };
            var converter = new RecordingRawSheetConverter
            {
                ProcessSheet = name => name == nameof(HookContainer.Primary),
                ExportSheetName = (_, __) => "target-primary",
            };

            var result = await converter.Export(CreateContext(container, logger));

            Assert.True(result);
            Assert.Equal(new[] { nameof(HookContainer.Primary) }, converter.ExportNameCalls);
            Assert.Equal(new[] { "target-primary" }, converter.CreatePageCalls);
            Assert.DoesNotContain(nameof(HookContainer.Skipped), converter.CreatePageCalls);
        }

        [Fact]
        public async Task ImportMapsSemanticNamesWithoutMappingHorizontalSelectors()
        {
            var logger = new TestLogger();
            var container = new HookContainer(logger);
            var converter = new RecordingRawSheetConverter
            {
                ProcessSheet = name => name == nameof(HookContainer.Primary),
                PropertyName = (_, __, name) => ToCanonicalName(name),
            };
            converter.AddImportPage(
                nameof(HookContainer.Primary),
                new[]
                {
                    "id", "nested:name", "items:1", "lookup:alpha",
                    "rewards:key", "rewards:value",
                },
                new[] { "Row", "Mapped", "First", "7", "Gold", "11" });

            var result = await converter.Import(CreateContext(container, logger));

            Assert.True(result);
            var row = container.Primary["Row"];
            Assert.Equal("Mapped", row.Nested.Name);
            Assert.Equal("First", row.Items[0]);
            Assert.Equal(7, row.Lookup["alpha"]);
            Assert.Equal(11, row.Rewards["Gold"]);
            Assert.Contains("Id", converter.PropertyNameResults);
            Assert.Contains("Key", converter.PropertyNameResults);
            Assert.Contains("Value", converter.PropertyNameResults);
            Assert.DoesNotContain("1", converter.PropertyNameInputs);
            Assert.DoesNotContain("alpha", converter.PropertyNameInputs);
        }

        [Fact]
        public async Task ImportMapsMarkerNamesAndPreservesSelectors()
        {
            var logger = new TestLogger();
            var container = new HookContainer(logger);
            var converter = new RecordingRawSheetConverter
            {
                ProcessSheet = name => name == nameof(HookContainer.Complex),
                PropertyName = (_, __, name) => ToCanonicalName(name),
            };
            converter.AddImportPage(
                nameof(HookContainer.Complex),
                new[] { "id", "stages:[2]:name", "stages:[2]:reward_pools:[1]:item" },
                new[] { "RAID001", null, null },
                new[] { null, "<#stages:[1]#>", null },
                new[] { null, "<#stages:[2]#>", null },
                new[] { null, "Forest", null },
                new[] { null, null, "<#stages:[2]:reward_pools:[1]#>" },
                new[] { null, null, "Gold" });

            var result = await converter.Import(CreateContext(container, logger));

            Assert.True(result);
            logger.VerifyNoError();
            var stage = container.Complex["RAID001"].Stages[0][0][0];
            Assert.Equal("Forest", stage.Name);
            Assert.Equal("Gold", stage.RewardPools[0][0].Item);
            Assert.DoesNotContain("[1]", converter.PropertyNameInputs);
            Assert.DoesNotContain("[2]", converter.PropertyNameInputs);
        }

        [Fact]
        public async Task CollectionLabelsBypassImportAndExportNameHooks()
        {
            var logger = new TestLogger();
            var container = new HookContainer(logger);
            var converter = new RecordingRawSheetConverter
            {
                HeaderMode = HeaderMode.Flat,
                ProcessSheet = name => name == nameof(HookContainer.Complex),
                PropertyName = (_, __, name) => ToCanonicalName(name),
                ExternalName = (_, __, name) => ToExternalName(name),
            };
            converter.AddImportPage(
                nameof(HookContainer.Complex),
                new[] { "id", "stages:[stage_label]:[class]:name" },
                new[] { "RAID001", null },
                new[] { null, "<#[stage_label]#>" },
                new[] { null, "<#[class]#>" },
                new[] { null, "Forest" });

            var importResult = await converter.Import(CreateContext(container, logger));
            container.Complex.Name = "Complex";
            var exportResult = await converter.Export(CreateContext(container, logger));

            Assert.True(importResult);
            Assert.True(exportResult);
            logger.VerifyNoError();
            Assert.Equal("Forest", container.Complex["RAID001"].Stages[0][0][0].Name);
            Assert.Contains("stages", converter.PropertyNameInputs);
            Assert.Contains("name", converter.PropertyNameInputs);
            Assert.DoesNotContain("stage_label", converter.PropertyNameInputs);
            Assert.DoesNotContain("class", converter.PropertyNameInputs);
            Assert.Contains("Stages", converter.ExternalNameInputs);
            Assert.Contains("Name", converter.ExternalNameInputs);
            Assert.DoesNotContain("stage_label", converter.ExternalNameInputs);
            Assert.DoesNotContain("class", converter.ExternalNameInputs);
            Assert.DoesNotContain(
                converter.ExportPages["Complex"].Values,
                value => value.Contains("stage_label", StringComparison.Ordinal) ||
                         value.Contains("class", StringComparison.Ordinal));
        }

        [Theory]
        [InlineData(HeaderMode.Hybrid)]
        [InlineData(HeaderMode.Split)]
        [InlineData(HeaderMode.Flat)]
        public async Task ExportMapsHeadersAndMarkersInEveryHeaderMode(HeaderMode mode)
        {
            var logger = new TestLogger();
            var sheet = CreateComplexSheet();
            sheet.Name = "Complex";
            var container = new HookContainer(logger) { Complex = sheet };
            var converter = new RecordingRawSheetConverter
            {
                HeaderMode = mode,
                ProcessSheet = name => name == nameof(HookContainer.Complex),
                ExternalName = (_, __, name) => ToExternalName(name),
            };

            var result = await converter.Export(CreateContext(container, logger));

            Assert.True(result);
            var values = converter.ExportPages["Complex"].Values.ToList();
            Assert.Contains(values, x => x.Contains("stages", StringComparison.Ordinal));
            Assert.Contains("<#stages:[1]#>", values);
            Assert.Contains("<#stages:[2]#>", values);
            Assert.Contains("<#stages:[2]:reward_pools:[1]#>", values);
            Assert.Contains("Key", converter.ExternalNameInputs);
            Assert.Contains("Value", converter.ExternalNameInputs);
            Assert.DoesNotContain("[1]", converter.ExternalNameInputs);
            Assert.DoesNotContain("[2]", converter.ExternalNameInputs);
            Assert.DoesNotContain("{}", converter.ExternalNameInputs);
        }

        [Fact]
        public async Task ExportMapsMarkersOnTransposedPages()
        {
            var logger = new TestLogger();
            var sheet = CreateComplexSheet();
            sheet.Name = "TransposedComplex";
            var container = new HookContainer(logger) { TransposedComplex = sheet };
            var converter = new RecordingRawSheetConverter
            {
                HeaderMode = HeaderMode.Flat,
                ProcessSheet = name => name == nameof(HookContainer.TransposedComplex),
                ExternalName = (_, __, name) => ToExternalName(name),
            };

            var result = await converter.Export(CreateContext(container, logger));

            Assert.True(result);
            Assert.Contains(
                "<#stages:[2]:reward_pools:[1]#>",
                converter.ExportPages["TransposedComplex"].Values);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("bad name")]
        [InlineData("bad:name")]
        [InlineData("bad[name")]
        [InlineData("bad]name")]
        [InlineData("bad{name")]
        [InlineData("bad}name")]
        public async Task ExportRejectsInvalidMappedMemberName(string invalidName)
        {
            var logger = new TestLogger();
            var container = new HookContainer(logger)
            {
                Primary = CreateHookSheet("Primary"),
            };
            var converter = new RecordingRawSheetConverter
            {
                ProcessSheet = name => name == nameof(HookContainer.Primary),
                ExternalName = (_, __, name) => name == nameof(HookSheet.Row.Value)
                    ? invalidName
                    : name,
            };

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => converter.Export(CreateContext(container, logger)));
        }

        [Fact]
        public async Task ExportRejectsMappedSemanticPathCollisionBeforeDataRows()
        {
            var logger = new TestLogger();
            var container = new HookContainer(logger)
            {
                Primary = CreateHookSheet("Primary"),
            };
            var converter = new RecordingRawSheetConverter
            {
                ProcessSheet = name => name == nameof(HookContainer.Primary),
                ExternalName = (_, __, name) =>
                    name == nameof(ISheetRow.Id) || name == nameof(HookSheet.Row.Value)
                        ? "same"
                        : name,
            };

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => converter.Export(CreateContext(container, logger)));

            Assert.DoesNotContain(
                converter.ExportPages["Primary"].Cells,
                x => x.Key.Row > 0 && x.Value != null);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public async Task ImportRejectsEmptySheetNameBeforeReadingPages(string invalidName)
        {
            var logger = new TestLogger();
            var container = new HookContainer(logger);
            var converter = new RecordingRawSheetConverter
            {
                ProcessSheet = name => name == nameof(HookContainer.Primary),
                ImportSheetName = _ => invalidName,
            };

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => converter.Import(CreateContext(container, logger)));

            Assert.Empty(converter.GetPagesCalls);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public async Task ExportRejectsEmptySheetNameBeforeCreatingPage(string invalidName)
        {
            var logger = new TestLogger();
            var container = new HookContainer(logger)
            {
                Primary = CreateHookSheet("Primary"),
            };
            var converter = new RecordingRawSheetConverter
            {
                ProcessSheet = name => name == nameof(HookContainer.Primary),
                ExportSheetName = (_, __) => invalidName,
            };

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => converter.Export(CreateContext(container, logger)));

            Assert.Empty(converter.CreatePageCalls);
        }

        [Fact]
        public async Task ImportReportsMappedInvalidNameAtPhysicalHeaderCell()
        {
            var logger = new TestLogger();
            var container = new HookContainer(logger);
            var converter = new RecordingRawSheetConverter
            {
                ProcessSheet = name => name == nameof(HookContainer.Primary),
                PropertyName = (_, __, name) => name == "value" ? "bad name" : ToCanonicalName(name),
            };
            converter.AddImportPage(
                nameof(HookContainer.Primary),
                new[] { "id", "value" },
                new[] { "Row", "Ignored" });

            var result = await converter.Import(CreateContext(container, logger));

            Assert.True(result);
            Assert.Empty(container.Primary);
            logger.VerifyLog(
                Microsoft.Extensions.Logging.LogLevel.Error,
                "Invalid sheet header at cell \"B1\".",
                new[] { nameof(HookContainer.Primary) });
        }

        private static MethodInfo GetHook(Type type, string name, params Type[] parameterTypes)
        {
            var method = type.GetMethod(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                parameterTypes,
                null);

            Assert.NotNull(method);
            Assert.True(method.IsFamily);
            Assert.True(method.IsVirtual);
            Assert.False(method.IsFinal);
            return method;
        }

        private static SheetConvertingContext CreateContext(
            SheetContainerBase container, TestLogger logger)
        {
            return new SheetConvertingContext
            {
                Container = container,
                Logger = logger,
            };
        }

        private static HookSheet CreateHookSheet(string name)
        {
            var sheet = new HookSheet
            {
                Name = name,
            };
            sheet.Add(new HookSheet.Row
            {
                Id = "Row",
                Value = "Stored",
            });
            return sheet;
        }

        private static TestExplicitVerticalCollectionSheet CreateComplexSheet()
        {
            return new TestExplicitVerticalCollectionSheet
            {
                new TestExplicitVerticalCollectionSheet.Row
                {
                    Id = "RAID001",
                    Stages = new VerticalList<VerticalList<VerticalList<TestExplicitVerticalCollectionSheet.Stage>>>
                    {
                        new VerticalList<VerticalList<TestExplicitVerticalCollectionSheet.Stage>>
                        {
                            new VerticalList<TestExplicitVerticalCollectionSheet.Stage>
                            {
                                new TestExplicitVerticalCollectionSheet.Stage
                                {
                                    Name = "Forest",
                                    RewardPools = new VerticalList<VerticalList<TestExplicitVerticalCollectionSheet.Reward>>
                                    {
                                        new VerticalList<TestExplicitVerticalCollectionSheet.Reward>
                                        {
                                            new TestExplicitVerticalCollectionSheet.Reward
                                            {
                                                Item = "Gold",
                                                Amount = 100,
                                            },
                                        },
                                    },
                                },
                            },
                        },
                    },
                    WaveRewards = new VerticalList<VerticalDictionary<string, int>>
                    {
                        new VerticalDictionary<string, int> { ["Wave"] = 1 },
                    },
                },
            };
        }

        private static string ToCanonicalName(string externalName)
        {
            switch (externalName)
            {
                case "id": return "Id";
                case "nested": return "Nested";
                case "name": return "Name";
                case "items": return "Items";
                case "lookup": return "Lookup";
                case "rewards": return "Rewards";
                case "key": return "Key";
                case "value": return "Value";
                case "stages": return "Stages";
                case "reward_pools": return "RewardPools";
                case "item": return "Item";
                case "amount": return "Amount";
                case "wave_rewards": return "WaveRewards";
                case "content": return "Content";
                default: return externalName;
            }
        }

        private static string ToExternalName(string propertyName)
        {
            switch (propertyName)
            {
                case "Id": return "id";
                case "Stages": return "stages";
                case "RewardPools": return "reward_pools";
                case "WaveRewards": return "wave_rewards";
                default: return propertyName.ToLowerInvariant();
            }
        }

        private sealed class HookContainer : SheetContainerBase
        {
            public HookSheet Primary { get; set; }
            public HookSheet Skipped { get; set; }
            public TestExplicitVerticalCollectionSheet Complex { get; set; }

            [Transpose]
            public TestExplicitVerticalCollectionSheet TransposedComplex { get; set; }

            public HookContainer(TestLogger logger) : base(logger) { }
        }

        private sealed class HookSheet : Sheet<HookSheet.Row>
        {
            public sealed class NestedValue
            {
                public string Name { get; set; }
            }

            public sealed class Row : SheetRow
            {
                public string Value { get; set; }
                public NestedValue Nested { get; set; }
                public List<string> Items { get; set; }
                public Dictionary<string, int> Lookup { get; set; }
                public VerticalDictionary<string, int> Rewards { get; set; }
            }
        }

        private sealed class IdentityRawSheetConverter : RawSheetConverter
        {
            public IdentityRawSheetConverter()
                : base(null, null) { }

            protected override Task<bool> LoadData()
            {
                return Task.FromResult(true);
            }

            protected override IEnumerable<IRawSheetImporterPage> GetPages(string sheetName)
            {
                return Array.Empty<IRawSheetImporterPage>();
            }

            protected override Task<bool> SaveData()
            {
                return Task.FromResult(true);
            }

            protected override IRawSheetExporterPage CreatePage(string sheetName)
            {
                return new EmptyPage();
            }
        }

        private sealed class EmptyPage : IRawSheetExporterPage
        {
            public void SetCell(int col, int row, string data) { }
        }

        private sealed class RecordingRawSheetConverter : RawSheetConverter
        {
            private readonly Dictionary<string, List<MemoryPage>> _importPages = new();

            public Func<string, bool> ProcessSheet { get; set; } = _ => true;
            public Func<string, string> ImportSheetName { get; set; } = name => name;
            public Func<PropertyInfo, ISheet, string> ExportSheetName { get; set; } = (_, sheet) => sheet.Name;
            public Func<PropertyInfo, ISheet, string, string> PropertyName { get; set; } = (_, __, name) => name;
            public Func<PropertyInfo, ISheet, string, string> ExternalName { get; set; } = (_, __, name) => name;

            public List<string> ShouldProcessCalls { get; } = new();
            public List<string> ImportNameCalls { get; } = new();
            public List<string> ExportNameCalls { get; } = new();
            public List<string> GetPagesCalls { get; } = new();
            public List<string> CreatePageCalls { get; } = new();
            public List<string> PropertyNameInputs { get; } = new();
            public List<string> PropertyNameResults { get; } = new();
            public List<string> ExternalNameInputs { get; } = new();
            public Dictionary<string, MemoryPage> ExportPages { get; } = new();

            public RecordingRawSheetConverter() : base(null, null) { }

            public void AddImportPage(string name, params string[][] rows)
            {
                _importPages[name] = new List<MemoryPage> { new MemoryPage(rows) };
            }

            protected override bool ShouldProcessSheet(
                SheetConvertingContext context, PropertyInfo sheetProperty)
            {
                ShouldProcessCalls.Add(sheetProperty.Name);
                return ProcessSheet(sheetProperty.Name);
            }

            protected override string GetImportSheetName(PropertyInfo sheetProperty)
            {
                ImportNameCalls.Add(sheetProperty.Name);
                return ImportSheetName(sheetProperty.Name);
            }

            protected override string ToPropertyName(
                PropertyInfo sheetProperty, ISheet sheet, string externalName)
            {
                PropertyNameInputs.Add(externalName);
                string result = PropertyName(sheetProperty, sheet, externalName);
                PropertyNameResults.Add(result);
                return result;
            }

            protected override string GetExportSheetName(PropertyInfo sheetProperty, ISheet sheet)
            {
                ExportNameCalls.Add(sheetProperty.Name);
                return ExportSheetName(sheetProperty, sheet);
            }

            protected override string ToExternalName(
                PropertyInfo sheetProperty, ISheet sheet, string propertyName)
            {
                ExternalNameInputs.Add(propertyName);
                return ExternalName(sheetProperty, sheet, propertyName);
            }

            protected override Task<bool> LoadData()
            {
                return Task.FromResult(true);
            }

            protected override IEnumerable<IRawSheetImporterPage> GetPages(string sheetName)
            {
                GetPagesCalls.Add(sheetName);
                return _importPages.TryGetValue(sheetName, out var pages)
                    ? pages
                    : Array.Empty<IRawSheetImporterPage>();
            }

            protected override int GetColumnCount(
                IRawSheetImporterPage page, int row, int headerColumnCount)
            {
                return page is MemoryPage memoryPage
                    ? Math.Max(headerColumnCount, memoryPage.GetColumnCount(row))
                    : headerColumnCount;
            }

            protected override Task<bool> SaveData()
            {
                return Task.FromResult(true);
            }

            protected override IRawSheetExporterPage CreatePage(string sheetName)
            {
                CreatePageCalls.Add(sheetName);
                var page = new MemoryPage();
                ExportPages.Add(sheetName, page);
                return page;
            }
        }

        private sealed class MemoryPage : IRawSheetImporterPage, IRawSheetExporterPage
        {
            private readonly Dictionary<(int Column, int Row), string> _cells = new();

            public string SubName => null;
            public IReadOnlyDictionary<(int Column, int Row), string> Cells => _cells;
            public IEnumerable<string> Values => _cells.Values.Where(x => x != null);

            public MemoryPage() { }

            public MemoryPage(IEnumerable<string[]> rows)
            {
                int row = 0;

                foreach (var values in rows)
                {
                    for (int column = 0; column < values.Length; ++column)
                        _cells[(column, row)] = values[column];

                    row++;
                }
            }

            public string GetCell(int col, int row)
            {
                return _cells.TryGetValue((col, row), out string value) ? value : null;
            }

            public int GetColumnCount(int row)
            {
                int result = 0;

                foreach (var cell in _cells.Keys)
                {
                    if (cell.Row == row)
                        result = Math.Max(result, cell.Column + 1);
                }

                return result;
            }

            public void SetCell(int col, int row, string data)
            {
                _cells[(col, row)] = data;
            }
        }
    }
}
