// BakingSheet, Maxwell Keonwoo Kang <code.athei@gmail.com>, 2022

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Cathei.BakingSheet.Tests
{
    public class VerticalDictionaryTests
    {
        [Fact]
        public void ConstructorsPreserveDictionarySemantics()
        {
            var empty = new VerticalDictionary<string, int>();
            var capacity = new VerticalDictionary<string, int>(4);
            var comparer = new VerticalDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var capacityComparer = new VerticalDictionary<string, int>(4, StringComparer.OrdinalIgnoreCase);
            var source = new Dictionary<string, int> { ["Alpha"] = 1 };
            var dictionary = new VerticalDictionary<string, int>(source);
            var dictionaryComparer = new VerticalDictionary<string, int>(source, StringComparer.OrdinalIgnoreCase);

            empty["First"] = 1;
            capacity.Add("Second", 2);
            comparer.Add("Mixed", 3);
            capacityComparer.Add("Other", 4);

            Assert.Equal(1, empty["First"]);
            Assert.Equal(2, capacity["Second"]);
            Assert.Equal(3, comparer["MIXED"]);
            Assert.Equal(4, capacityComparer["OTHER"]);
            Assert.Equal(1, dictionary["Alpha"]);
            Assert.Equal(1, dictionaryComparer["ALPHA"]);
            Assert.Throws<ArgumentException>(() => comparer.Add("mixed", 5));
        }

        [Fact]
        public async Task ImportPrimitiveAndCompositeEntries()
        {
            using var fileSystem = new TestFileSystem();
            var logger = new TestLogger();
            var container = new TestSheetContainer(logger);
            var converter = new CsvSheetConverter("testdata", TimeZoneInfo.Utc, fileSystem: fileSystem);

            fileSystem.SetTestData(
                Path.Combine("testdata", "VerticalDictionary.csv"),
                "Id,Primitive:Value,Primitive:Key,Composite:Key:Name,Composite:Key:Level," +
                "Composite:Value:Amount,Composite:Value:Label\n" +
                "Row,10,Alpha,Sword,2,30,Rare\n" +
                ",20,Beta,Shield,3,40,Common\n");

            var result = await container.Bake(converter);

            logger.VerifyNoError();
            Assert.True(result);
            Assert.Equal(10, container.VerticalDictionary["Row"].Primitive["Alpha"]);
            Assert.Equal(20, container.VerticalDictionary["Row"].Primitive["Beta"]);
            Assert.Equal(
                30,
                container.VerticalDictionary["Row"].Composite[
                    new TestVerticalDictionarySheet.CompositeKey { Name = "Sword", Level = 2 }].Amount);
            Assert.Equal(
                "Common",
                container.VerticalDictionary["Row"].Composite[
                    new TestVerticalDictionarySheet.CompositeKey { Name = "Shield", Level = 3 }].Label);
        }

        [Fact]
        public async Task ImportRecursiveContinuation()
        {
            using var fileSystem = new TestFileSystem();
            var logger = new TestLogger();
            var container = new TestSheetContainer(logger);
            var converter = new CsvSheetConverter("testdata", TimeZoneInfo.Utc, fileSystem: fileSystem);

            fileSystem.SetTestData(
                Path.Combine("testdata", "VerticalDictionary.csv"),
                "Id,NestedNumeric:Key,NestedNumeric:Value:Key,NestedNumeric:Value:Value\n" +
                "Row,1,10,100\n" +
                ",,,101\n" +
                ",,20,200\n" +
                ",bad,30,300\n" +
                ",,,301\n" +
                ",1,40,400\n" +
                ",,,401\n" +
                ",2,bad,500\n" +
                ",,,501\n" +
                ",,50,600\n" +
                ",,,601\n" +
                ",3,60,700\n" +
                ",,60,800\n" +
                ",,,801\n");

            var result = await container.Bake(converter);

            Assert.True(result);
            var nested = container.VerticalDictionary["Row"].NestedNumeric;
            Assert.Equal(new[] { 100, 101 }, nested[1][10]);
            Assert.Equal(new[] { 200 }, nested[1][20]);
            Assert.Equal(new[] { 600, 601 }, nested[2][50]);
            Assert.Single(nested[2]);
            Assert.Equal(new[] { 700 }, nested[3][60]);
            logger.VerifyLogCount(
                Microsoft.Extensions.Logging.LogLevel.Error,
                "Vertical dictionary already contains key \"1\".",
                1);
            logger.VerifyLogCount(
                Microsoft.Extensions.Logging.LogLevel.Error,
                "Vertical dictionary already contains key \"60\".",
                1);
            logger.VerifyLogCount(
                Microsoft.Extensions.Logging.LogLevel.Error,
                "Vertical dictionary entry requires a key.",
                0);
        }

        [Fact]
        public async Task ImportAcceptsReorderedColumns()
        {
            using var fileSystem = new TestFileSystem();
            var logger = new TestLogger();
            var container = new TestSheetContainer(logger);
            var converter = new CsvSheetConverter("testdata", TimeZoneInfo.Utc, fileSystem: fileSystem);

            fileSystem.SetTestData(
                Path.Combine("testdata", "VerticalDictionary.csv"),
                "Id,Composite:Value:Label,Composite:Value:Amount,Composite:Key:Level,Composite:Key:Name\n" +
                "Row,Rare,25,4,Sword\n");

            var result = await container.Bake(converter);

            logger.VerifyNoError();
            Assert.True(result);
            var value = container.VerticalDictionary["Row"].Composite[
                new TestVerticalDictionarySheet.CompositeKey { Name = "Sword", Level = 4 }];
            Assert.Equal(25, value.Amount);
            Assert.Equal("Rare", value.Label);
        }

        [Fact]
        public async Task ImportBlankValuesByCompleteGroup()
        {
            using var fileSystem = new TestFileSystem();
            var logger = new TestLogger();
            var container = new TestSheetContainer(logger);
            var converter = new CsvSheetConverter("testdata", TimeZoneInfo.Utc, fileSystem: fileSystem);

            fileSystem.SetTestData(
                Path.Combine("testdata", "VerticalDictionary.csv"),
                "Id,Primitive:Key,Primitive:Value,Composite:Key:Name,Composite:Value:Amount," +
                "Composite:Value:Label\n" +
                "Row,BlankScalar,,BlankComposite,,\n" +
                ",,,Partial,5,\n");

            var result = await container.Bake(converter);

            Assert.True(result);
            var row = container.VerticalDictionary["Row"];
            Assert.Equal(0, row.Primitive["BlankScalar"]);
            Assert.Equal(0, row.Composite[
                new TestVerticalDictionarySheet.CompositeKey { Name = "BlankComposite" }].Amount);
            Assert.Null(row.Composite[
                new TestVerticalDictionarySheet.CompositeKey { Name = "BlankComposite" }].Label);
            Assert.Equal(5, row.Composite[
                new TestVerticalDictionarySheet.CompositeKey { Name = "Partial" }].Amount);
            logger.VerifyLogCount(
                Microsoft.Extensions.Logging.LogLevel.Error,
                "Vertical dictionary entry for key \"BlankScalar\" requires a value.",
                1);
            logger.VerifyLogCount(
                Microsoft.Extensions.Logging.LogLevel.Error,
                "Vertical dictionary entry for key \"BlankComposite:0\" requires a value.",
                1);
        }

        [Fact]
        public async Task ImportRejectsMissingFailedAndDuplicateKeys()
        {
            using var fileSystem = new TestFileSystem();
            var logger = new TestLogger();
            var container = new TestSheetContainer(logger);
            var converter = new CsvSheetConverter("testdata", TimeZoneInfo.Utc, fileSystem: fileSystem);

            fileSystem.SetTestData(
                Path.Combine("testdata", "VerticalDictionary.csv"),
                "Id,Numeric:Key,Numeric:Value,CaseInsensitive:Key,CaseInsensitive:Value," +
                "Primitive:Key,Primitive:Value\n" +
                "Row,1,10,Alpha,1,A,5\n" +
                ",bad,20,alpha,2,,6\n" +
                ",,30,,3,B,7\n");

            var result = await container.Bake(converter);

            Assert.True(result);
            var row = container.VerticalDictionary["Row"];
            Assert.Single(row.Numeric);
            Assert.Equal(10, row.Numeric[1]);
            Assert.Single(row.CaseInsensitive);
            Assert.Equal(1, row.CaseInsensitive["ALPHA"]);
            Assert.Equal(5, row.Primitive["A"]);
            Assert.Equal(7, row.Primitive["B"]);
            logger.VerifyLogCount(
                Microsoft.Extensions.Logging.LogLevel.Error,
                "Vertical dictionary already contains key \"alpha\".",
                1);
            logger.VerifyLogCount(
                Microsoft.Extensions.Logging.LogLevel.Error,
                "Vertical dictionary entry requires a key.",
                1);
        }

        [Fact]
        public async Task ImportAndExportFlatAndSplitHeaders()
        {
            using var fileSystem = new TestFileSystem();
            var logger = new TestLogger();
            var container = new TestSheetContainer(logger)
            {
                VerticalDictionaryExport = new TestVerticalDictionaryExportSheet
                {
                    new TestVerticalDictionaryExportSheet.Row
                    {
                        Id = "Row",
                        Rewards = new VerticalDictionary<string, VerticalDictionary<string, int>>
                        {
                            ["Outer"] = new VerticalDictionary<string, int>
                            {
                                ["One"] = 1,
                                ["Two"] = 2,
                            },
                        },
                        Other = new VerticalList<int> { 7, 8, 9 },
                    },
                },
            };
            var converter = new CsvSheetConverter("testdata", TimeZoneInfo.Utc, fileSystem: fileSystem);
            converter.HeaderMode = Raw.HeaderMode.Flat;

            container.PostLoad();
            var result = await container.Store(converter);

            logger.VerifyNoError();
            Assert.True(result);
            fileSystem.VerifyTestData(
                Path.Combine("testdata", "VerticalDictionaryExport.csv"),
                "Id,Rewards:Key,Rewards:Value:Key,Rewards:Value:Value,Other\n" +
                "Row,Outer,One,1,7\n" +
                ",,Two,2,8\n" +
                ",,,,9\n");

            converter.HeaderMode = Raw.HeaderMode.Split;
            result = await container.Store(converter);

            Assert.True(result);
            fileSystem.VerifyTestData(
                Path.Combine("testdata", "VerticalDictionaryExport.csv"),
                "Id,Rewards,,,Other\n" +
                ",Key,Value\n" +
                ",,Key,Value\n" +
                "Row,Outer,One,1,7\n" +
                ",,Two,2,8\n" +
                ",,,,9\n");
        }

        [Fact]
        public async Task UnsupportedShapesReportOncePerConversion()
        {
            using var fileSystem = new TestFileSystem();
            var logger = new TestLogger();
            var container = new TestSheetContainer(logger);
            var converter = new CsvSheetConverter("testdata", TimeZoneInfo.Utc, fileSystem: fileSystem);

            fileSystem.SetTestData(
                Path.Combine("testdata", "UnsupportedVerticalDictionary.csv"),
                "Id,VerticalKey:Anything,ReferenceKey:Anything,NoExportableLeaf:Anything,Valid:Key,Valid:Value\n" +
                "Row,1,2,3,A,10\n");

            var result = await container.Bake(converter);

            Assert.True(result);
            Assert.Equal(10, container.UnsupportedVerticalDictionary["Row"].Valid["A"]);
            VerifyUnsupportedLog<VerticalDictionary<VerticalList<int>, int>>(
                logger, "VerticalKey", 1);
            VerifyUnsupportedLog<VerticalDictionary<TestSheet.Reference, int>>(
                logger, "ReferenceKey", 1);
            VerifyUnsupportedLog<VerticalList<TestUnsupportedVerticalDictionarySheet.NoExportableLeaf>>(
                logger, "NoExportableLeaf", 1);

            result = await container.Store(converter);

            Assert.True(result);
            VerifyUnsupportedLog<VerticalDictionary<VerticalList<int>, int>>(
                logger, "VerticalKey", 2);
            VerifyUnsupportedLog<VerticalDictionary<TestSheet.Reference, int>>(
                logger, "ReferenceKey", 2);
            VerifyUnsupportedLog<VerticalList<TestUnsupportedVerticalDictionarySheet.NoExportableLeaf>>(
                logger, "NoExportableLeaf", 2);
        }

        [Fact]
        public void ReferencesAndVerificationVisitValuesOnly()
        {
            var logger = new TestLogger();
            var container = new TestSheetContainer(logger)
            {
                Tests = new TestSheet
                {
                    new TestSheet.Row { Id = "Target" },
                },
                VerticalDictionaryReferences = new TestVerticalDictionaryReferenceSheet
                {
                    new TestVerticalDictionaryReferenceSheet.Row
                    {
                        Id = "Row",
                        References = new VerticalDictionary<string, TestSheet.Reference>
                        {
                            ["ReferenceKey"] = new TestSheet.Reference("Target"),
                        },
                        Text = new VerticalDictionary<string, string>
                        {
                            ["KeyMustNotBeVerified"] = "ValueMustBeVerified",
                        },
                    },
                },
            };
            var verifier = new DictionaryValueVerifier();

            container.PostLoad();
            container.Verify(verifier);

            logger.VerifyNoError();
            Assert.Equal(
                container.Tests["Target"],
                container.VerticalDictionaryReferences["Row"].References["ReferenceKey"].Ref);
            Assert.Equal(new[] { "ValueMustBeVerified" }, verifier.Values);
        }

        [Fact]
        public async Task CsvRoundTripPreservesSupportedValues()
        {
            using var fileSystem = new TestFileSystem();
            var logger = new TestLogger();
            var source = new TestSheetContainer(logger)
            {
                VerticalDictionary = new TestVerticalDictionarySheet
                {
                    new TestVerticalDictionarySheet.Row
                    {
                        Id = "Row",
                        Primitive = new VerticalDictionary<string, int> { ["A"] = 1 },
                        Lists = new VerticalDictionary<string, VerticalList<int>>
                        {
                            ["B"] = new VerticalList<int> { 2, 3 },
                        },
                        Nested = new VerticalDictionary<string, VerticalDictionary<string, int>>
                        {
                            ["C"] = new VerticalDictionary<string, int> { ["D"] = 4 },
                        },
                        NestedLists = new VerticalDictionary<string, VerticalDictionary<string, VerticalList<int>>>
                        {
                            ["E"] = new VerticalDictionary<string, VerticalList<int>>
                            {
                                ["F"] = new VerticalList<int> { 5, 6 },
                            },
                        },
                    },
                },
            };
            var exporter = new CsvSheetConverter("testdata", TimeZoneInfo.Utc, fileSystem: fileSystem);

            source.PostLoad();
            var result = await source.Store(exporter);

            Assert.True(result);

            var path = Path.Combine("testdata", "VerticalDictionary.csv");
            fileSystem.files[path] = new MemoryStream(fileSystem.files[path].ToArray());

            var imported = new TestSheetContainer(logger);
            var importer = new CsvSheetConverter("testdata", TimeZoneInfo.Utc, fileSystem: fileSystem);
            result = await imported.Bake(importer);

            logger.VerifyNoError();
            Assert.True(result);
            var row = imported.VerticalDictionary["Row"];
            Assert.Equal(1, row.Primitive["A"]);
            Assert.Equal(new[] { 2, 3 }, row.Lists["B"]);
            Assert.Equal(4, row.Nested["C"]["D"]);
            Assert.Equal(new[] { 5, 6 }, row.NestedLists["E"]["F"]);
        }

        [Fact]
        public async Task CsvRoundTripPreservesDeepMixedNestingAndMinimalMarkers()
        {
            using var fileSystem = new TestFileSystem();
            var logger = new TestLogger();
            var source = new TestSheetContainer(logger)
            {
                VerticalCollectionNesting = new TestVerticalCollectionNestingSheet
                {
                    new TestVerticalCollectionNestingSheet.Row
                    {
                        Id = "Row",
                        Roots = new VerticalList<TestVerticalCollectionNestingSheet.Root>
                        {
                            new TestVerticalCollectionNestingSheet.Root
                            {
                                ListBranches = new List<TestVerticalCollectionNestingSheet.ListBranch>
                                {
                                    new TestVerticalCollectionNestingSheet.ListBranch
                                    {
                                        ListValues = new VerticalList<int> { 1 },
                                        DictionaryValues = new VerticalDictionary<string, int>
                                        {
                                            ["ListToDictionary"] = 2,
                                        },
                                    },
                                },
                                DictionaryBranches = new Dictionary<string, TestVerticalCollectionNestingSheet.DictionaryBranch>
                                {
                                    ["Alpha"] = new TestVerticalCollectionNestingSheet.DictionaryBranch
                                    {
                                        Entries = new VerticalDictionary<string, TestVerticalCollectionNestingSheet.DictionaryValue>
                                        {
                                            ["Outer"] = new TestVerticalCollectionNestingSheet.DictionaryValue
                                            {
                                                ListValues = new VerticalList<int> { 3 },
                                                DictionaryValues = new VerticalDictionary<string, int>
                                                {
                                                    ["Inner"] = 4,
                                                },
                                            },
                                        },
                                    },
                                },
                            },
                            new TestVerticalCollectionNestingSheet.Root
                            {
                                ListBranches = new List<TestVerticalCollectionNestingSheet.ListBranch>
                                {
                                    new TestVerticalCollectionNestingSheet.ListBranch
                                    {
                                        ListValues = new VerticalList<int> { 5 },
                                        DictionaryValues = new VerticalDictionary<string, int>
                                        {
                                            ["ListToDictionary2"] = 6,
                                        },
                                    },
                                },
                            },
                        },
                        EmptyItems = new VerticalList<VerticalList<int>>
                        {
                            new VerticalList<int> { 8 },
                            new VerticalList<int>(),
                        },
                        Legacy = new VerticalList<int> { 9, 10 },
                    },
                },
            };
            var exporter = new CsvSheetConverter("testdata", TimeZoneInfo.Utc, fileSystem: fileSystem);

            source.PostLoad();
            var result = await source.Store(exporter);

            logger.VerifyNoError();
            Assert.True(result);
            var path = Path.Combine("testdata", "VerticalCollectionNesting.csv");
            fileSystem.VerifyTestData(
                path,
                "Id,Roots,,,,,,,EmptyItems:[1],Legacy\n" +
                ",ListBranches,,,DictionaryBranches\n" +
                ",1,,,Alpha\n" +
                ",ListValues,DictionaryValues,,Entries\n" +
                ",,Key,Value,Key,Value\n" +
                ",,,,,ListValues,DictionaryValues\n" +
                ",,,,,,Key,Value\n" +
                "Row,1,ListToDictionary,2,Outer,3,Inner,4,,9\n" +
                ",5,ListToDictionary2,6,,,,,,10\n" +
                ",,,,,,,,<#EmptyItems:[1]#>,\n" +
                ",,,,,,,,8\n" +
                ",,,,,,,,<#EmptyItems:[1]#>,\n");

            fileSystem.files[path] = new MemoryStream(fileSystem.files[path].ToArray());

            var imported = new TestSheetContainer(logger);
            var importer = new CsvSheetConverter("testdata", TimeZoneInfo.Utc, fileSystem: fileSystem);
            result = await imported.Bake(importer);

            logger.VerifyNoError();
            Assert.True(result);
            var row = imported.VerticalCollectionNesting["Row"];
            Assert.Equal(2, row.Roots.Count);
            Assert.Equal(1, row.Roots[0].ListBranches[0].ListValues[0]);
            Assert.Equal(2, row.Roots[0].ListBranches[0].DictionaryValues["ListToDictionary"]);
            var dictionaryValue = row.Roots[0].DictionaryBranches["Alpha"].Entries["Outer"];
            Assert.Equal(3, dictionaryValue.ListValues[0]);
            Assert.Equal(4, dictionaryValue.DictionaryValues["Inner"]);
            Assert.Equal(5, row.Roots[1].ListBranches[0].ListValues[0]);
            Assert.Equal(6, row.Roots[1].ListBranches[0].DictionaryValues["ListToDictionary2"]);
            Assert.Equal(new[] { 8 }, row.EmptyItems[0]);
            Assert.Empty(row.EmptyItems[1]);
            Assert.Equal(new[] { 9, 10 }, row.Legacy);
        }

        private static void VerifyUnsupportedLog<T>(TestLogger logger, string path, int count)
        {
            logger.VerifyLogCount(
                Microsoft.Extensions.Logging.LogLevel.Error,
                $"Property \"{path}\" has unsupported vertical collection type \"{typeof(T)}\".",
                count);
        }

        private sealed class DictionaryValueVerifier : SheetVerifier<string>
        {
            public List<string> Values { get; } = new List<string>();

            public override string Verify(PropertyInfo propertyInfo, string value)
            {
                if (propertyInfo?.Name == nameof(TestVerticalDictionaryReferenceSheet.Row.Text))
                    Values.Add(value);

                return null;
            }
        }

        private sealed class MarkerTestLogger : ILogger
        {
            public List<string> Errors { get; } = new List<string>();

            public IDisposable BeginScope<TState>(TState state)
            {
                return null;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception,
                Func<TState, Exception, string> formatter)
            {
                if (logLevel >= LogLevel.Error)
                    Errors.Add(formatter(state, exception));
            }
        }
    }
}
