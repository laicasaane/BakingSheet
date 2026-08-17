// BakingSheet, Maxwell Keonwoo Kang <code.athei@gmail.com>, 2022

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Cathei.BakingSheet.Internal;
using Cathei.BakingSheet.Raw;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Cathei.BakingSheet.Tests
{
    public class ExplicitVerticalCollectionMarkerTests
    {
        [Fact]
        public void RawConvertersExposeValidatedHeaderAndEmptyRowControls()
        {
            var headerMode = typeof(RawSheetConverter).Assembly.GetType(
                "Cathei.BakingSheet.Raw.HeaderMode", throwOnError: false);

            Assert.NotNull(headerMode);
            Assert.True(headerMode.IsEnum);
            Assert.Equal(
                new[] { "Hybrid", "Split", "Flat" },
                Enum.GetNames(headerMode));
            Assert.Equal(
                new[] { 0, 1, 2 },
                Enum.GetValues(headerMode).Cast<object>().Select(Convert.ToInt32));

            var headerProperty = typeof(RawSheetConverter).GetProperty("HeaderMode");
            var emptyRowProperty = typeof(RawSheetImporter).GetProperty("EmptyRowAllowance");

            Assert.NotNull(headerProperty);
            Assert.Equal(headerMode, headerProperty.PropertyType);
            Assert.Null(typeof(RawSheetConverter).GetProperty("SplitHeader"));
            Assert.NotNull(emptyRowProperty);
            Assert.Equal(typeof(int), emptyRowProperty.PropertyType);

            var rawConstructor = typeof(RawSheetConverter)
                .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
                .Single();
            Assert.Equal(
                new[] { typeof(TimeZoneInfo), typeof(IFormatProvider) },
                rawConstructor.GetParameters().Select(x => x.ParameterType));

            var csvConstructor = typeof(CsvSheetConverter).GetConstructors().Single();
            Assert.Equal(
                new[]
                {
                    typeof(string),
                    typeof(TimeZoneInfo),
                    typeof(string),
                    typeof(IFileSystem),
                    typeof(IFormatProvider),
                },
                csvConstructor.GetParameters().Select(x => x.ParameterType));
        }

        [Fact]
        public void RawConverterControlsRejectInvalidValuesAndPreservePriorState()
        {
            using var fileSystem = new TestFileSystem();
            var converter = new CsvSheetConverter(
                "testdata", TimeZoneInfo.Utc, fileSystem: fileSystem);

            Assert.Equal(HeaderMode.Hybrid, converter.HeaderMode);
            Assert.Equal(0, converter.EmptyRowAllowance);

            converter.HeaderMode = HeaderMode.Split;
            Assert.Throws<ArgumentOutOfRangeException>(
                () => converter.HeaderMode = (HeaderMode)3);
            Assert.Equal(HeaderMode.Split, converter.HeaderMode);

            converter.EmptyRowAllowance = 1;
            Assert.Throws<ArgumentOutOfRangeException>(
                () => converter.EmptyRowAllowance = -1);
            Assert.Equal(1, converter.EmptyRowAllowance);
            Assert.Empty(fileSystem.files);
        }

        [Theory]
        [InlineData(HeaderMode.Hybrid)]
        [InlineData(HeaderMode.Split)]
        [InlineData(HeaderMode.Flat)]
        public async Task ExportUsesCanonicalHeaderGeometryAndExplicitMarkers(HeaderMode mode)
        {
            using var fileSystem = new TestFileSystem();
            var logger = new RecordingLogger();
            var container = new ExplicitOnlyContainer(logger)
            {
                ExplicitVerticalCollection = CreateRaidSheet(),
            };
            var converter = new CsvSheetConverter(
                "testdata", TimeZoneInfo.Utc, fileSystem: fileSystem)
            {
                HeaderMode = mode,
            };

            container.PostLoad();
            var result = await container.Store(converter);

            Assert.True(result);
            Assert.Empty(logger.Errors);
            fileSystem.VerifyTestData(
                Path.Combine("testdata", "ExplicitVerticalCollection.csv"),
                GetExpectedExport(mode));

            var path = Path.Combine("testdata", "ExplicitVerticalCollection.csv");
            fileSystem.files[path] = new MemoryStream(fileSystem.files[path].ToArray());
            var imported = new ExplicitOnlyContainer(logger);

            result = await imported.Bake(converter);

            Assert.True(result);
            Assert.Empty(logger.Errors);
            AssertSmallRaidGraph(imported.ExplicitVerticalCollection["RAID001"]);
        }

        public static IEnumerable<object[]> EquivalentHeaders()
        {
            yield return new object[]
            {
                "Id,Stages:[2]:Name,Stages:[2]:RewardPools:[1]:Item," +
                "Stages:[2]:RewardPools:[1]:Amount\n" + RaidData,
            };
            yield return new object[]
            {
                "Id,Stages:[2],,\n" +
                ",Name,RewardPools:[1],\n" +
                ",,Item,Amount\n" + RaidData,
            };
            yield return new object[]
            {
                "Id,Stages,,\n" +
                ",[],,\n" +
                ",[],,\n" +
                ",Name,RewardPools,\n" +
                ",,[],\n" +
                ",,Item,Amount\n" + RaidData,
            };
        }

        public static IEnumerable<object[]> LabeledListHeaders()
        {
            yield return new object[] { "Id,Stages:[stage]:[wave]:Name\n" };
            yield return new object[] { "Id,Stages:[stage]:[wave]\n,Name\n" };
            yield return new object[] { "Id,Stages\n,[stage]\n,[wave]\n,Name\n" };
        }

        public static IEnumerable<object[]> LabeledDictionaryHeaders()
        {
            yield return new object[]
            {
                "Id,WaveRewards:{rewards}:Key,WaveRewards:{rewards}:Value\n",
            };
            yield return new object[] { "Id,WaveRewards:{rewards},\n,Key,Value\n" };
            yield return new object[] { "Id,WaveRewards,\n,{rewards},\n,Key,Value\n" };
        }

        [Theory]
        [MemberData(nameof(EquivalentHeaders))]
        public async Task ImportEquivalentHeadersBuildsSameGraph(string csv)
        {
            using var fileSystem = new TestFileSystem();
            var logger = new RecordingLogger();
            var container = new TestSheetContainer(logger);
            var converter = new CsvSheetConverter(
                "testdata", TimeZoneInfo.Utc, fileSystem: fileSystem);
            fileSystem.SetTestData(
                Path.Combine("testdata", "ExplicitVerticalCollection.csv"), csv);

            var result = await container.Bake(converter);

            Assert.True(result);
            Assert.Empty(logger.Errors);
            AssertRaidGraph(container.ExplicitVerticalCollection["RAID001"]);
        }

        [Theory]
        [MemberData(nameof(LabeledListHeaders))]
        public async Task LabeledListMarkersBuildSameGraphInEveryHeaderMode(string header)
        {
            using var fileSystem = new TestFileSystem();
            var logger = new RecordingLogger();
            var container = new TestSheetContainer(logger);
            var converter = new CsvSheetConverter(
                "testdata", TimeZoneInfo.Utc, fileSystem: fileSystem);
            fileSystem.SetTestData(
                Path.Combine("testdata", "ExplicitVerticalCollection.csv"),
                header +
                "Row,\n" +
                ",<#[stage]#>\n" +
                ",<#[wave]#>\n" +
                ",Forest\n");

            var result = await container.Bake(converter);

            Assert.True(result);
            Assert.Empty(logger.Errors);
            Assert.Equal(
                "Forest",
                container.ExplicitVerticalCollection["Row"].Stages[0][0][0].Name);
        }

        [Theory]
        [MemberData(nameof(LabeledDictionaryHeaders))]
        public async Task LabeledDictionaryMarkersBuildSameGraphInEveryHeaderMode(string header)
        {
            using var fileSystem = new TestFileSystem();
            var logger = new RecordingLogger();
            var container = new TestSheetContainer(logger);
            var converter = new CsvSheetConverter(
                "testdata", TimeZoneInfo.Utc, fileSystem: fileSystem);
            fileSystem.SetTestData(
                Path.Combine("testdata", "ExplicitVerticalCollection.csv"),
                header +
                "Row,,\n" +
                ",<#{rewards}#>,\n" +
                ",Gold,100\n");

            var result = await container.Bake(converter);

            Assert.True(result);
            Assert.Empty(logger.Errors);
            Assert.Equal(100, container.ExplicitVerticalCollection["Row"].WaveRewards[0]["Gold"]);
        }

        [Fact]
        public async Task KeywordAndUnderscoreLabelsRemainExactSheetText()
        {
            using var fileSystem = new TestFileSystem();
            var logger = new RecordingLogger();
            var container = new TestSheetContainer(logger);
            var converter = new CsvSheetConverter(
                "testdata", TimeZoneInfo.Utc, fileSystem: fileSystem);
            fileSystem.SetTestData(
                Path.Combine("testdata", "ExplicitVerticalCollection.csv"),
                "Id,Stages:[class]:[_wave]:Name\n" +
                "Row,\n" +
                ",<#[class]#>\n" +
                ",<#[_wave]#>\n" +
                ",Forest\n");

            var result = await container.Bake(converter);

            Assert.True(result);
            Assert.Empty(logger.Errors);
            Assert.Equal(
                "Forest",
                container.ExplicitVerticalCollection["Row"].Stages[0][0][0].Name);
        }

        [Fact]
        public async Task LabelCannotPointToDifferentCollectionBoundaries()
        {
            using var fileSystem = new TestFileSystem();
            var logger = new RecordingLogger();
            var container = new TestSheetContainer(logger);
            var converter = new CsvSheetConverter(
                "testdata", TimeZoneInfo.Utc, fileSystem: fileSystem);
            fileSystem.SetTestData(
                Path.Combine("testdata", "ExplicitVerticalCollection.csv"),
                "Id,Stages:[stage]:[group]:RewardPools:[group]:Item\nRow,Gold\n");

            var result = await container.Bake(converter);

            Assert.True(result);
            Assert.Null(container.ExplicitVerticalCollection?["Row"]);
            Assert.Equal(
                new[] { "Invalid sheet header at cell \"B1\"." },
                logger.Errors);
        }

        [Fact]
        public async Task CollectionBoundaryCannotUseDifferentLabels()
        {
            using var fileSystem = new TestFileSystem();
            var logger = new RecordingLogger();
            var container = new TestSheetContainer(logger);
            var converter = new CsvSheetConverter(
                "testdata", TimeZoneInfo.Utc, fileSystem: fileSystem);
            fileSystem.SetTestData(
                Path.Combine("testdata", "ExplicitVerticalCollection.csv"),
                "Id,Stages:[stage]:[wave]:Name," +
                "Stages:[stage]:[other]:RewardPools:[rewards]:Item\n" +
                "Row,Forest,Gold\n");

            var result = await container.Bake(converter);

            Assert.True(result);
            Assert.Null(container.ExplicitVerticalCollection?["Row"]);
            Assert.Equal(
                new[] { "Invalid sheet header at cell \"C1\"." },
                logger.Errors);
        }

        [Fact]
        public async Task AnonymousDictionaryMarkersCreateInstancesAndKeysCreateEntries()
        {
            using var fileSystem = new TestFileSystem();
            var logger = new RecordingLogger();
            var container = new TestSheetContainer(logger);
            var converter = new CsvSheetConverter(
                "testdata", TimeZoneInfo.Utc, fileSystem: fileSystem);
            fileSystem.SetTestData(
                Path.Combine("testdata", "ExplicitVerticalCollection.csv"),
                "Id,WaveRewards:{}:Key,WaveRewards:{}:Value\n" +
                "Row,,\n" +
                ",<#WaveRewards:{}#> $$ First dictionary,\n" +
                ",A,1\n" +
                ",B,2\n" +
                ",<#WaveRewards:{}#> $$ Second dictionary,\n" +
                ",C,3\n");

            var result = await container.Bake(converter);

            Assert.True(result);
            Assert.Empty(logger.Errors);
            var dictionaries = container.ExplicitVerticalCollection["Row"].WaveRewards;
            Assert.Equal(2, dictionaries.Count);
            Assert.Equal(1, dictionaries[0]["A"]);
            Assert.Equal(2, dictionaries[0]["B"]);
            Assert.Equal(3, dictionaries[1]["C"]);
        }

        [Theory]
        [InlineData("[1]", "<#Values:Value:[1]#>")]
        [InlineData("[group]", "<#[group]#>")]
        public async Task NestedListsInDictionaryImportWithAnonymousAndLabeledMarkers(
            string selector, string marker)
        {
            using var fileSystem = new TestFileSystem();
            var logger = new RecordingLogger();
            var container = new NestedListsOnlyContainer(logger);
            var converter = new CsvSheetConverter(
                "testdata", TimeZoneInfo.Utc, fileSystem: fileSystem);
            fileSystem.SetTestData(
                Path.Combine("testdata", "NestedLists.csv"),
                $"Id,Values:Key,Values:Value:{selector}\n" +
                "Row,10,\n" +
                $",,{marker}\n" +
                ",,Alpha\n" +
                ",,Beta\n" +
                $",,{marker}\n" +
                ",,Gamma\n" +
                ",20,\n" +
                $",,{marker}\n" +
                ",,Delta\n");

            var result = await container.Bake(converter);

            Assert.True(result);
            Assert.Empty(logger.Errors);
            var values = container.NestedLists["Row"].Values;
            Assert.Equal(2, values.Count);
            Assert.Equal(2, values[10].Count);
            Assert.Equal(new[] { "Alpha", "Beta" }, values[10][0]);
            Assert.Equal(new[] { "Gamma" }, values[10][1]);
            Assert.Single(values[20]);
            Assert.Equal(new[] { "Delta" }, values[20][0]);
        }

        [Theory]
        [InlineData("Stages:[0]:Name")]
        [InlineData("Stages:[01]:Name")]
        [InlineData("Stages:[3]:Name")]
        [InlineData("Stages:[]:Name")]
        [InlineData("Stages:{}:Name")]
        [InlineData("Stages:[999999999999999999999999]:Name")]
        [InlineData("Stages:[1label]:Name")]
        [InlineData("Stages:[bad-name]:Name")]
        [InlineData("Stages:[bad name]:Name")]
        [InlineData("Stages:[é]:Name")]
        [InlineData("Stages:[+2]:Name")]
        [InlineData("Stages:[1_0]:Name")]
        [InlineData("Stages:[ 2 ]:Name")]
        [InlineData("Stages:[2147483648]:Name")]
        public async Task InvalidFlatHeadersRejectPageAtExactCell(string header)
        {
            using var fileSystem = new TestFileSystem();
            var logger = new RecordingLogger();
            var container = new TestSheetContainer(logger);
            var converter = new CsvSheetConverter(
                "testdata", TimeZoneInfo.Utc, fileSystem: fileSystem);
            fileSystem.SetTestData(
                Path.Combine("testdata", "ExplicitVerticalCollection.csv"),
                $"Id,{header}\nRow,Forest\n");

            var result = await container.Bake(converter);

            Assert.True(result);
            Assert.Null(container.ExplicitVerticalCollection?["Row"]);
            Assert.Equal(
                new[] { "Invalid sheet header at cell \"B1\"." },
                logger.Errors);
        }

        [Fact]
        public async Task DuplicateSemanticHeaderRejectsSecondColumn()
        {
            using var fileSystem = new TestFileSystem();
            var logger = new RecordingLogger();
            var container = new TestSheetContainer(logger);
            var converter = new CsvSheetConverter(
                "testdata", TimeZoneInfo.Utc, fileSystem: fileSystem);
            fileSystem.SetTestData(
                Path.Combine("testdata", "ExplicitVerticalCollection.csv"),
                "Id,Stages:[2]:Name,Stages:[2]:Name\nRow,Forest,Castle\n");

            var result = await container.Bake(converter);

            Assert.True(result);
            Assert.Null(container.ExplicitVerticalCollection?["Row"]);
            Assert.Equal(
                new[] { "Invalid sheet header at cell \"C1\"." },
                logger.Errors);
        }

        [Fact]
        public async Task InvalidHeaderRejectsOnlyItsPhysicalPage()
        {
            using var fileSystem = new TestFileSystem();
            var logger = new RecordingLogger();
            var container = new TestSheetContainer(logger);
            var converter = new CsvSheetConverter(
                "testdata", TimeZoneInfo.Utc, fileSystem: fileSystem);
            fileSystem.SetTestData(
                Path.Combine("testdata", "ExplicitVerticalCollection.csv"),
                "Id,Stages:[0]:Name\nRejected,Forest\n");
            fileSystem.SetTestData(
                Path.Combine("testdata", "ExplicitVerticalCollection.001.csv"),
                "Id,Stages:[2]:Name\nAccepted,\n" +
                ",<#Stages:[1]#>\n" +
                ",<#Stages:[2]#>\n" +
                ",Forest\n");
            fileSystem.SetTestData(
                Path.Combine("testdata", "Tests.csv"),
                "Id,Content\nOther,kept\n");

            var result = await container.Bake(converter);

            Assert.True(result);
            Assert.Null(container.ExplicitVerticalCollection["Rejected"]);
            Assert.Equal(
                "Forest",
                container.ExplicitVerticalCollection["Accepted"].Stages[0][0][0].Name);
            Assert.Equal("kept", container.Tests["Other"].Content);
            Assert.Equal(
                new[] { "Invalid sheet header at cell \"B1\"." },
                logger.Errors);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public async Task MarkerCanAppearInOwnedUnrelatedOrSeparatorColumn(int markerColumn)
        {
            using var fileSystem = new TestFileSystem();
            var logger = new RecordingLogger();
            var container = new TestSheetContainer(logger);
            var converter = new CsvSheetConverter(
                "testdata", TimeZoneInfo.Utc, fileSystem: fileSystem);
            string MarkerRow(string marker)
            {
                var cells = new[] { string.Empty, string.Empty, string.Empty, string.Empty };
                cells[markerColumn] = marker;
                return string.Join(",", cells);
            }

            fileSystem.SetTestData(
                Path.Combine("testdata", "ExplicitVerticalCollection.csv"),
                "Id,Stages:[2]:Name,Content,\n" +
                "Row,,,\n" +
                MarkerRow("<#Stages:[1]#>") + "\n" +
                MarkerRow("<#Stages:[2]#>") + "\n" +
                ",Forest,,ignored\n");

            var result = await container.Bake(converter);

            Assert.True(result);
            Assert.Empty(logger.Errors);
            Assert.Equal(
                "Forest",
                container.ExplicitVerticalCollection["Row"].Stages[0][0][0].Name);
        }

        [Fact]
        public async Task MarkerAllowsOuterHorizontalSpaceAndNotesButCommentsRemainPositional()
        {
            using var fileSystem = new TestFileSystem();
            var logger = new RecordingLogger();
            var container = new TestSheetContainer(logger);
            var converter = new CsvSheetConverter(
                "testdata", TimeZoneInfo.Utc, fileSystem: fileSystem);
            fileSystem.SetTestData(
                Path.Combine("testdata", "ExplicitVerticalCollection.csv"),
                "Id,Stages:[2]:Name,$Comment\n" +
                "$Skipped,<#Stages:[0]#>,ignored\n" +
                "Row,,\n" +
                ", \t<#Stages:[1]#> \t$$ Act <#label#> \t,\n" +
                ",\u00A0<#Stages:[2]#>\u00A0,\n" +
                ",Forest,<#Stages:[0]#>\n");

            var result = await container.Bake(converter);

            Assert.True(result);
            Assert.Empty(logger.Errors);
            Assert.Null(container.ExplicitVerticalCollection["$Skipped"]);
            Assert.Equal(
                "Forest",
                container.ExplicitVerticalCollection["Row"].Stages[0][0][0].Name);
        }

        [Fact]
        public async Task DataCellCommentSiblingDoesNotInvalidateMarkerRow()
        {
            using var fileSystem = new TestFileSystem();
            var logger = new RecordingLogger();
            var container = new TestSheetContainer(logger);
            var converter = new CsvSheetConverter(
                "testdata", TimeZoneInfo.Utc, fileSystem: fileSystem);
            fileSystem.SetTestData(
                Path.Combine("testdata", "ExplicitVerticalCollection.csv"),
                "Id,Stages:[2]:Name,Content\n" +
                "Row,,\n" +
                ",<#Stages:[1]#>, \t$$ Act\n" +
                ",<#Stages:[2]#>,\n" +
                ",Forest,\n");

            var result = await container.Bake(converter);

            Assert.True(result);
            Assert.Empty(logger.Errors);
            Assert.Equal(
                "Forest",
                container.ExplicitVerticalCollection["Row"].Stages[0][0][0].Name);
        }

        [Fact]
        public async Task MissingFirstMarkerDiscardsLogicalRowAndRecoversAtNextId()
        {
            using var fileSystem = new TestFileSystem();
            var logger = new RecordingLogger();
            var container = new TestSheetContainer(logger);
            var converter = new CsvSheetConverter(
                "testdata", TimeZoneInfo.Utc, fileSystem: fileSystem);
            fileSystem.SetTestData(
                Path.Combine("testdata", "ExplicitVerticalCollection.csv"),
                "Id,Stages:[2]:Name,Content\n" +
                "Discard,Forest,bad\n" +
                ",Ignored,ignored\n" +
                "Keep,,kept\n" +
                ",<#Stages:[1]#>,\n" +
                ",<#Stages:[2]#>,\n" +
                ",Forest,\n");

            var result = await container.Bake(converter);

            Assert.True(result);
            Assert.Null(container.ExplicitVerticalCollection["Discard"]);
            var kept = container.ExplicitVerticalCollection["Keep"];
            Assert.Equal("kept", kept.Content);
            Assert.Equal("Forest", kept.Stages[0][0][0].Name);
            Assert.Equal(
                new[] { "Vertical collection marker required before cell \"B2\"." },
                logger.Errors);
        }

        [Theory]
        [InlineData(",ordinary,<#Stages:[0]#>", "C5")]
        [InlineData(",<#Stages:[0]#>,ordinary", "B5")]
        [InlineData(",<#Stages:[2]#>,unexpected", "C5")]
        [InlineData(",<#Stages:[2]#>,<#Stages:[2]#>", "C5")]
        [InlineData(",<#Stages#>,", "B5")]
        [InlineData(",<#Stages:[+2]#>,", "B5")]
        [InlineData(",<#Stages:[02]#>,", "B5")]
        [InlineData(",<#Stages:[]#>,", "B5")]
        [InlineData(",<#Stages:{}#>,", "B5")]
        [InlineData(",<#WaveRewards:{}:Key#>,", "B5")]
        [InlineData(",<#Stages :[2]#>,", "B5")]
        [InlineData(",<#Stages:[\v2]#>,", "B5")]
        [InlineData(",<#Stages:[ 2 ]#>,", "B5")]
        [InlineData(",<#Stages:[\u00A02\u00A0]#>,", "B5")]
        [InlineData(",<#Stages:[1_0]#>,", "B5")]
        [InlineData(",<#Stages:[2147483648]#>,", "B5")]
        [InlineData(",<#Stages:[2]#> suffix,", "B5")]
        [InlineData(",<#Unknown:[1]#>,", "B5")]
        public async Task InvalidMarkerReportsFirstOffendingCellAndRecovers(
            string invalidRow, string expectedCell)
        {
            using var fileSystem = new TestFileSystem();
            var logger = new RecordingLogger();
            var container = new TestSheetContainer(logger);
            var converter = new CsvSheetConverter(
                "testdata", TimeZoneInfo.Utc, fileSystem: fileSystem);
            fileSystem.SetTestData(
                Path.Combine("testdata", "ExplicitVerticalCollection.csv"),
                "Id,Stages:[2]:Name,Content\n" +
                "Discard,,\n" +
                ",<#Stages:[1]#>,\n" +
                ",<#Stages:[2]#>,\n" +
                invalidRow + "\n" +
                "Keep,,kept\n" +
                ",<#Stages:[1]#>,\n" +
                ",<#Stages:[2]#>,\n" +
                ",Forest,\n");

            var result = await container.Bake(converter);

            Assert.True(result);
            Assert.Null(container.ExplicitVerticalCollection["Discard"]);
            Assert.Equal(
                "Forest",
                container.ExplicitVerticalCollection["Keep"].Stages[0][0][0].Name);
            Assert.Equal(
                new[] { $"Invalid vertical collection marker at cell \"{expectedCell}\"." },
                logger.Errors);
        }

        [Theory]
        [InlineData("<#[Wave]#>")]
        [InlineData("<#{wave}#>")]
        [InlineData("<#Stages#>")]
        [InlineData("<#Stages:[wave]#>")]
        public async Task LabelMarkersAreCaseSensitiveKindSpecificAndDirect(string invalidMarker)
        {
            using var fileSystem = new TestFileSystem();
            var logger = new RecordingLogger();
            var container = new TestSheetContainer(logger);
            var converter = new CsvSheetConverter(
                "testdata", TimeZoneInfo.Utc, fileSystem: fileSystem);
            fileSystem.SetTestData(
                Path.Combine("testdata", "ExplicitVerticalCollection.csv"),
                "Id,Stages:[stage]:[wave]:Name\n" +
                "Discard,\n" +
                ",<#[stage]#>\n" +
                $",{invalidMarker}\n" +
                "Keep,\n" +
                ",<#[stage]#>\n" +
                ",<#[wave]#>\n" +
                ",Forest\n");

            var result = await container.Bake(converter);

            Assert.True(result);
            Assert.Null(container.ExplicitVerticalCollection["Discard"]);
            Assert.Equal(
                "Forest",
                container.ExplicitVerticalCollection["Keep"].Stages[0][0][0].Name);
            Assert.Equal(
                new[] { "Invalid vertical collection marker at cell \"B4\"." },
                logger.Errors);
        }

        [Theory]
        [InlineData(0, false)]
        [InlineData(1, true)]
        public async Task EmptyRowAllowanceUsesConsecutiveCompleteWhitespaceRows(
            int allowance, bool importsSecondRow)
        {
            using var fileSystem = new TestFileSystem();
            var logger = new RecordingLogger();
            var container = new TestSheetContainer(logger);
            var converter = new CsvSheetConverter(
                "testdata", TimeZoneInfo.Utc, fileSystem: fileSystem)
            {
                EmptyRowAllowance = allowance,
            };
            fileSystem.SetTestData(
                Path.Combine("testdata", "Tests.csv"),
                "Id,Content\n" +
                "One,first\n" +
                " \t, \t\n" +
                "Two,second\n" +
                ",\n" +
                ",\n" +
                "Three,third\n");

            var result = await container.Bake(converter);

            Assert.True(result);
            Assert.Empty(logger.Errors);
            Assert.Equal("first", container.Tests["One"].Content);
            Assert.Equal(importsSecondRow, container.Tests["Two"] != null);
            Assert.Null(container.Tests["Three"]);
        }

        private const string RaidData =
            "RAID001,,,\n" +
            ",<#Stages:[1]#> $$ Act A,,\n" +
            ",<#Stages:[2]#> $$ Chapter A1,,\n" +
            ",Forest,,\n" +
            ",,<#Stages:[2]:RewardPools:[1]#> $$ Rewards: Forest Pool 1,\n" +
            ",,Gold,100\n" +
            ",,Health Potion,2\n" +
            ",,<#Stages:[2]:RewardPools:[1]#> $$ Rewards: Forest Pool 2,\n" +
            ",,Gem,5\n" +
            ",Castle,,\n" +
            ",,<#Stages:[2]:RewardPools:[1]#> $$ Rewards: Castle Pool 1,\n" +
            ",,Coin,200\n" +
            ",,Health Potion,4\n" +
            ",<#Stages:[2]#> $$ Chapter A2,,\n" +
            ",Desert,,\n" +
            ",,<#Stages:[2]:RewardPools:[1]#> $$ Rewards: Desert Pool 1,\n" +
            ",,Sand,10\n" +
            ",,Scorpion Tail,3\n" +
            ",<#Stages:[1]#> $$ Act B,,\n" +
            ",<#Stages:[2]#> $$ Chapter B1,,\n" +
            ",Volcano,,\n" +
            ",,<#Stages:[2]:RewardPools:[1]#> $$ Rewards: Volcano Pool 1,\n" +
            ",,Obsidian,7\n" +
            ",,Fire Core,1\n" +
            ",<#Stages:[2]#> $$ Chapter B2,,\n" +
            ",Ruins,,\n" +
            ",,<#Stages:[2]:RewardPools:[1]#> $$ Rewards: Ruins Pool 1,\n" +
            ",,Ancient Coin,20\n" +
            ",,Relic Fragment,2\n";

        private static TestExplicitVerticalCollectionSheet CreateRaidSheet()
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
                },
            };
        }

        private static void AssertRaidGraph(TestExplicitVerticalCollectionSheet.Row row)
        {
            Assert.NotNull(row);
            Assert.Equal(2, row.Stages.Count);
            Assert.Equal(2, row.Stages[0].Count);
            Assert.Equal(2, row.Stages[1].Count);
            Assert.Equal(new[] { "Forest", "Castle" }, row.Stages[0][0].Select(x => x.Name));
            Assert.Equal(new[] { "Desert" }, row.Stages[0][1].Select(x => x.Name));
            Assert.Equal(new[] { "Volcano" }, row.Stages[1][0].Select(x => x.Name));
            Assert.Equal(new[] { "Ruins" }, row.Stages[1][1].Select(x => x.Name));

            var forest = row.Stages[0][0][0];
            Assert.Equal(2, forest.RewardPools.Count);
            Assert.Equal(new[] { "Gold", "Health Potion" }, forest.RewardPools[0].Select(x => x.Item));
            Assert.Equal(new[] { 100, 2 }, forest.RewardPools[0].Select(x => x.Amount));
            Assert.Equal(new[] { "Gem" }, forest.RewardPools[1].Select(x => x.Item));
            Assert.Equal(new[] { 5 }, forest.RewardPools[1].Select(x => x.Amount));

            Assert.Equal(
                new[] { "Coin", "Health Potion" },
                row.Stages[0][0][1].RewardPools[0].Select(x => x.Item));
            Assert.Equal(
                new[] { "Sand", "Scorpion Tail" },
                row.Stages[0][1][0].RewardPools[0].Select(x => x.Item));
            Assert.Equal(
                new[] { "Obsidian", "Fire Core" },
                row.Stages[1][0][0].RewardPools[0].Select(x => x.Item));
            Assert.Equal(
                new[] { "Ancient Coin", "Relic Fragment" },
                row.Stages[1][1][0].RewardPools[0].Select(x => x.Item));
        }

        private static void AssertSmallRaidGraph(TestExplicitVerticalCollectionSheet.Row row)
        {
            Assert.NotNull(row);
            Assert.Single(row.Stages);
            Assert.Single(row.Stages[0]);
            Assert.Single(row.Stages[0][0]);
            var stage = row.Stages[0][0][0];
            Assert.Equal("Forest", stage.Name);
            Assert.Single(stage.RewardPools);
            Assert.Single(stage.RewardPools[0]);
            Assert.Equal("Gold", stage.RewardPools[0][0].Item);
            Assert.Equal(100, stage.RewardPools[0][0].Amount);
        }

        private static string GetExpectedExport(HeaderMode mode)
        {
            string header;

            switch (mode)
            {
                case HeaderMode.Hybrid:
                    header =
                        "Id,Stages:[2],,,WaveRewards:{},,Content\n" +
                        ",Name,RewardPools:[1],,Key,Value\n" +
                        ",,Item,Amount\n";
                    break;
                case HeaderMode.Split:
                    header =
                        "Id,Stages,,,WaveRewards,,Content\n" +
                        ",[],,,{}\n" +
                        ",[],,,Key,Value\n" +
                        ",Name,RewardPools\n" +
                        ",,[]\n" +
                        ",,Item,Amount\n";
                    break;
                case HeaderMode.Flat:
                    header =
                        "Id,Stages:[2]:Name,Stages:[2]:RewardPools:[1]:Item," +
                        "Stages:[2]:RewardPools:[1]:Amount,WaveRewards:{}:Key," +
                        "WaveRewards:{}:Value,Content\n";
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode));
            }

            return header +
                   "RAID001,,,,,,\n" +
                   ",<#Stages:[1]#>,,,,,\n" +
                   ",<#Stages:[2]#>,,,,,\n" +
                   ",Forest\n" +
                   ",,<#Stages:[2]:RewardPools:[1]#>,,,,\n" +
                   ",,Gold,100\n";
        }

        private sealed class RecordingLogger : ILogger
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

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
                Exception exception, Func<TState, Exception, string> formatter)
            {
                string message = formatter(state, exception);

                if (logLevel >= LogLevel.Error &&
                    !message.Contains("Failed to find sheet"))
                {
                    Errors.Add(message);
                }
            }
        }

        private sealed class ExplicitOnlyContainer : SheetContainerBase
        {
            public TestExplicitVerticalCollectionSheet ExplicitVerticalCollection { get; set; }

            public ExplicitOnlyContainer(ILogger logger) : base(logger)
            {
            }
        }

        private sealed class NestedListsSheet : Sheet<NestedListsSheet.Row>
        {
            public sealed class Row : SheetRow
            {
                public VerticalDictionary<int, VerticalList<VerticalList<string>>> Values { get; set; }
            }
        }

        private sealed class NestedListsOnlyContainer : SheetContainerBase
        {
            public NestedListsSheet NestedLists { get; set; }

            public NestedListsOnlyContainer(ILogger logger) : base(logger)
            {
            }
        }
    }
}
