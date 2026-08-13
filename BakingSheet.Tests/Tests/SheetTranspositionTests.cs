// BakingSheet, Maxwell Keonwoo Kang <code.athei@gmail.com>, 2022

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Cathei.BakingSheet.Tests
{
    public class SheetTranspositionTests
    {
        [Fact]
        public void TransposeAttributeHasApprovedPropertyOnlyContract()
        {
            var attributeType = typeof(SheetContainerBase).Assembly.GetType(
                "Cathei.BakingSheet.TransposeAttribute");

            Assert.NotNull(attributeType);
            Assert.True(attributeType.IsSealed);
            Assert.True(typeof(Attribute).IsAssignableFrom(attributeType));
            Assert.NotNull(attributeType.GetConstructor(Type.EmptyTypes));

            var usage = attributeType.GetCustomAttributes(typeof(AttributeUsageAttribute), false)
                .Cast<AttributeUsageAttribute>()
                .Single();

            Assert.Equal(AttributeTargets.Property, usage.ValidOn);
        }

        [Fact]
        public async Task ImportReadsPhysicalColumnsAsLogicalRows()
        {
            using var fileSystem = new TestFileSystem();
            var logger = new TestLogger();
            var container = new TransposedTestSheetContainer(logger);
            var converter = CreateConverter(fileSystem);

            fileSystem.SetTestData(TestPath("Tests.csv"), "Id,A\nContent,Hello");

            var result = await container.Bake(converter);

            logger.VerifyNoError();
            Assert.True(result);
            Assert.Single(container.Tests);
            Assert.Equal("Hello", container.Tests["A"].Content);
        }

        [Fact]
        public async Task ExportWritesLogicalRowsAsPhysicalColumns()
        {
            using var fileSystem = new TestFileSystem();
            var logger = new TestLogger();
            var container = new TransposedTestSheetContainer(logger)
            {
                Tests = new TestSheet
                {
                    new TestSheet.Row { Id = "A", Content = "Hello" },
                },
            };
            var converter = CreateConverter(fileSystem);

            container.PostLoad();
            var result = await container.Store(converter);

            logger.VerifyNoError();
            Assert.True(result);
            fileSystem.VerifyTestData(TestPath("Tests.csv"), "Id,A\nContent,Hello\n");
        }

        [Fact]
        public async Task ImportThenExportPreservesPhysicalLayout()
        {
            const string physicalPage = "Id,A\nContent,Hello\n";
            using var fileSystem = new TestFileSystem();
            var logger = new TestLogger();
            var container = new TransposedTestSheetContainer(logger);
            var converter = CreateConverter(fileSystem);

            fileSystem.SetTestData(TestPath("Tests.csv"), physicalPage);

            Assert.True(await container.Bake(converter));
            Assert.True(await container.Store(converter));

            logger.VerifyNoError();
            fileSystem.VerifyTestData(TestPath("Tests.csv"), physicalPage);
        }

        [Fact]
        public async Task ImportOrdersAllTransposedPartialPagesBySubName()
        {
            using var fileSystem = new TestFileSystem();
            var logger = new TestLogger();
            var container = new TransposedTestSheetContainer(logger);
            var converter = CreateConverter(fileSystem);

            fileSystem.SetTestData(TestPath("Tests.002.csv"), "Id,Second\nContent,second");
            fileSystem.SetTestData(TestPath("Tests.csv"), "Id,Zero\nContent,zero");
            fileSystem.SetTestData(TestPath("Tests.001.csv"), "Id,First\nContent,first");

            var result = await container.Bake(converter);

            logger.VerifyNoError();
            Assert.True(result);
            Assert.Equal(new[] { "Zero", "First", "Second" }, container.Tests.Select(x => x.Id));
            Assert.Equal(new[] { "zero", "first", "second" }, container.Tests.Select(x => x.Content));
        }

        [Fact]
        public async Task ImportSupportsMixedPropertyOrientationsWithDistinctRowTypes()
        {
            using var fileSystem = new TestFileSystem();
            var logger = new TestLogger();
            var container = new MixedOrientationContainer(logger);
            var converter = CreateConverter(fileSystem);

            fileSystem.SetTestData(TestPath("Normal.csv"), "Id,Content\nN,normal");
            fileSystem.SetTestData(TestPath("Transposed.csv"), "Id,T\nContent,transposed");

            var result = await container.Bake(converter);

            logger.VerifyNoError();
            Assert.True(result);
            Assert.Equal("normal", container.Normal["N"].Content);
            Assert.Equal("transposed", container.Transposed["T"].Content);
        }

        [Fact]
        public async Task ImportTransposedComplexGrammarMatchesLogicalPage()
        {
            const string logicalPage =
                "Id,Coordinates,Coordinates,$Note\n" +
                ",X,Y,\n" +
                "Row,1,2,ignored\n" +
                ",3,4,\n" +
                "$Skip,99,99,ignored";
            const string physicalPage =
                "Id,,Row,,$Skip\n" +
                "Coordinates,X,1,3,99\n" +
                "Coordinates,Y,2,4,99\n" +
                "$Note,,ignored,,ignored";
            using var normalFileSystem = new TestFileSystem();
            using var transposedFileSystem = new TestFileSystem();
            var normalLogger = new TestLogger();
            var transposedLogger = new TestLogger();
            var normal = new ComplexContainer(normalLogger);
            var transposed = new TransposedComplexContainer(transposedLogger);

            normalFileSystem.SetTestData(TestPath("Complex.csv"), logicalPage);
            transposedFileSystem.SetTestData(TestPath("Complex.csv"), physicalPage);

            Assert.True(await normal.Bake(CreateConverter(normalFileSystem)));
            Assert.True(await transposed.Bake(CreateConverter(transposedFileSystem)));

            normalLogger.VerifyNoError();
            transposedLogger.VerifyNoError();
            Assert.Equal(new[] { 1, 3 }, normal.Complex["Row"].Coordinates.Select(x => x.X));
            Assert.Equal(new[] { 2, 4 }, normal.Complex["Row"].Coordinates.Select(x => x.Y));
            Assert.Equal(
                normal.Complex["Row"].Coordinates.Select(x => x.X),
                transposed.Complex["Row"].Coordinates.Select(x => x.X));
            Assert.Equal(
                normal.Complex["Row"].Coordinates.Select(x => x.Y),
                transposed.Complex["Row"].Coordinates.Select(x => x.Y));
        }

        [Fact]
        public async Task InvalidMarkerReportsPhysicalCellAndRecoversAtNextId()
        {
            using var fileSystem = new TestFileSystem();
            var logger = new TestLogger();
            var container = new TransposedMarkerContainer(logger);
            var converter = CreateConverter(fileSystem);

            fileSystem.SetTestData(
                TestPath("Markers.csv"),
                "Id,Discard,,,Keep\n" +
                "Values,1,<#Values,2,3\n" +
                "Content,discarded,ignored,ignored,kept");

            var result = await container.Bake(converter);

            Assert.True(result);
            Assert.Null(container.Markers["Discard"]);
            Assert.Equal("kept", container.Markers["Keep"].Content);
            Assert.Equal(new[] { 3 }, container.Markers["Keep"].Values);
            logger.VerifyLog(
                LogLevel.Error,
                "Invalid vertical collection marker at cell \"C2\".",
                new[] { "Markers" });
        }

        [Fact]
        public async Task ImportStopsAtFirstEmptyPhysicalRowAndColumn()
        {
            using var fileSystem = new TestFileSystem();
            var logger = new TestLogger();
            var container = new TransposedTestSheetContainer(logger);
            var converter = CreateConverter(fileSystem);

            fileSystem.SetTestData(
                TestPath("Tests.csv"),
                "Id,Keep,,AfterEmptyColumn\n" +
                "Content,kept,,ignored\n" +
                ",,,\n" +
                "Extra,beyond,,ignored");

            var result = await container.Bake(converter);

            logger.VerifyNoError();
            Assert.True(result);
            Assert.Single(container.Tests);
            Assert.Equal("kept", container.Tests["Keep"].Content);
        }

        private static CsvSheetConverter CreateConverter(TestFileSystem fileSystem)
        {
            return new CsvSheetConverter("testdata", TimeZoneInfo.Utc, fileSystem: fileSystem);
        }

        private static string TestPath(string fileName)
        {
            return Path.Combine("testdata", fileName);
        }

        public sealed class TransposedTestSheetContainer : SheetContainerBase
        {
            public TransposedTestSheetContainer(ILogger logger) : base(logger) { }

            [Transpose]
            public TestSheet Tests { get; set; }
        }

        public sealed class NormalOrientationSheet : Sheet<NormalOrientationSheet.Row>
        {
            public sealed class Row : SheetRow
            {
                public string Content { get; set; }
            }
        }

        public sealed class TransposedOrientationSheet : Sheet<TransposedOrientationSheet.Row>
        {
            public sealed class Row : SheetRow
            {
                public string Content { get; set; }
            }
        }

        public sealed class MixedOrientationContainer : SheetContainerBase
        {
            public MixedOrientationContainer(ILogger logger) : base(logger) { }

            public NormalOrientationSheet Normal { get; set; }

            [Transpose]
            public TransposedOrientationSheet Transposed { get; set; }
        }

        public sealed class ComplexSheet : Sheet<ComplexSheet.Row>
        {
            public struct Coordinate
            {
                public int X { get; set; }
                public int Y { get; set; }
            }

            public sealed class Row : SheetRow
            {
                public VerticalList<Coordinate> Coordinates { get; set; }
            }
        }

        public sealed class ComplexContainer : SheetContainerBase
        {
            public ComplexContainer(ILogger logger) : base(logger) { }

            public ComplexSheet Complex { get; set; }
        }

        public sealed class TransposedComplexContainer : SheetContainerBase
        {
            public TransposedComplexContainer(ILogger logger) : base(logger) { }

            [Transpose]
            public ComplexSheet Complex { get; set; }
        }

        public sealed class MarkerSheet : Sheet<MarkerSheet.Row>
        {
            public sealed class Row : SheetRow
            {
                public VerticalList<int> Values { get; set; }
                public string Content { get; set; }
            }
        }

        public sealed class TransposedMarkerContainer : SheetContainerBase
        {
            public TransposedMarkerContainer(ILogger logger) : base(logger) { }

            [Transpose]
            public MarkerSheet Markers { get; set; }
        }
    }
}
