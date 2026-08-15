// BakingSheet, Maxwell Keonwoo Kang <code.athei@gmail.com>, 2022

using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace Cathei.BakingSheet.Tests
{
    public class SheetTokensTests
    {
        [Fact]
        public void PublicConstantsHaveExpectedValues()
        {
            Assert.Equal(
                new[]
                {
                    "$", "$$", ":", ".", "[", "]", "[]",
                    "{", "}", "{}", "<#", "#>", "Id", "Key", "Value",
                },
                new[]
                {
                    SheetTokens.Comment.Primary,
                    SheetTokens.Comment.Cell,
                    SheetTokens.Separator.Path,
                    SheetTokens.Separator.SheetName,
                    SheetTokens.List.Selector.Start,
                    SheetTokens.List.Selector.End,
                    SheetTokens.List.Selector.Anonymous,
                    SheetTokens.Dictionary.Selector.Start,
                    SheetTokens.Dictionary.Selector.End,
                    SheetTokens.Dictionary.Selector.Anonymous,
                    SheetTokens.Collection.Marker.Start,
                    SheetTokens.Collection.Marker.End,
                    SheetTokens.Header.Id,
                    SheetTokens.Dictionary.Header.Key,
                    SheetTokens.Dictionary.Header.Value,
                });
        }

        [Fact]
        public void CompositesMatchTheirParts()
        {
            Assert.Equal(
                SheetTokens.List.Selector.Start + SheetTokens.List.Selector.End,
                SheetTokens.List.Selector.Anonymous);
            Assert.Equal(
                SheetTokens.Dictionary.Selector.Start + SheetTokens.Dictionary.Selector.End,
                SheetTokens.Dictionary.Selector.Anonymous);
        }

        [Fact]
        public void CachedCollectionsExposeExpectedValues()
        {
            Assert.Equal(new[] { ":" }, SheetTokens.PathSeparators.ToArray());
            Assert.Equal(
                new[] { ':', '[', ']', '{', '}' },
                SheetTokens.ReservedPathNameCharacters.ToArray());
            Assert.Equal(new[] { "Parent", "Child" }, SheetTokens.SplitPath("Parent:Child"));
            Assert.True(SheetTokens.ContainsReservedPathNameCharacter("Parent:Child"));
            Assert.False(SheetTokens.ContainsReservedPathNameCharacter("Parent"));
        }

        [Fact]
        public void PublicHelpersPreserveExistingBehavior()
        {
            Assert.True(SheetTokens.StartsWithComment("  $ note", SheetTokens.Comment.Primary));
            Assert.False(SheetTokens.StartsWithComment("value", SheetTokens.Comment.Primary));
            Assert.Equal(("Sheet", "Sub"), SheetTokens.ParseSheetName("Sheet.Sub"));
            Assert.Equal(("Sheet", (string)null), SheetTokens.ParseSheetName("Sheet"));
            Assert.Contains(
                SheetTokens.GetEligibleProperties(typeof(TestSheet.Row)),
                property => property.Name == nameof(TestSheet.Row.Content));
            Assert.Equal(
                nameof(ISheetRowArray.Arr),
                SheetTokens.GetRowArrayProperty(typeof(TestArraySheet.Row)).Name);
        }

        [Fact]
        public async Task ConsumerCanComposeSupportedRawSyntax()
        {
            var (result, container, logger) = await ImportComposedSyntax();

            Assert.True(result);
            logger.VerifyNoError();
            var row = container.ExplicitVerticalCollection["Row"];
            Assert.Null(row.Content);
            Assert.Equal(1, row.WaveRewards[0]["A"]);
        }

        private static async Task<(bool result, TestSheetContainer container, TestLogger logger)>
            ImportComposedSyntax()
        {
            using var fileSystem = new TestFileSystem();
            var logger = new TestLogger();
            var container = new TestSheetContainer(logger);
            var converter = new CsvSheetConverter(
                "testdata", TimeZoneInfo.Utc, fileSystem: fileSystem);
            string dictionaryPath = string.Join(
                SheetTokens.Separator.Path,
                "WaveRewards",
                SheetTokens.Dictionary.Selector.Anonymous);
            string keyPath = string.Join(
                SheetTokens.Separator.Path,
                dictionaryPath,
                SheetTokens.Dictionary.Header.Key);
            string valuePath = string.Join(
                SheetTokens.Separator.Path,
                dictionaryPath,
                SheetTokens.Dictionary.Header.Value);
            string marker = SheetTokens.Collection.Marker.Start + dictionaryPath +
                            SheetTokens.Collection.Marker.End;
            fileSystem.SetTestData(
                Path.Combine("testdata", "ExplicitVerticalCollection.csv"),
                $"{SheetTokens.Header.Id},Content,{keyPath},{valuePath}\n" +
                $"{SheetTokens.Comment.Primary} ignored,,,\n" +
                $"Row,{SheetTokens.Comment.Cell} ignored,,\n" +
                $",,{marker} {SheetTokens.Comment.Cell} First dictionary,\n" +
                ",,A,1\n");

            bool result = await container.Bake(converter);
            return (result, container, logger);
        }
    }
}
