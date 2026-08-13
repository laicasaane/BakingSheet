// BakingSheet, Maxwell Keonwoo Kang <code.athei@gmail.com>, 2022

using System;
using Xunit;

namespace Cathei.BakingSheet.Tests
{
    public class PropertyMapTests
    {
        [Fact]
        public void SetValuePreservesImmediateScalarAssignment()
        {
            using var fileSystem = new TestFileSystem();
            var logger = new TestLogger();
            var container = new TestSheetContainer(logger);
            ISheet sheet = new TestSheet();
            var row = new TestSheet.Row();
            var context = new SheetConvertingContext
            {
                Container = container,
                Logger = logger,
            };
            var formatter = new CsvSheetConverter("testdata", TimeZoneInfo.Utc, fileSystem: fileSystem);

            sheet.GetPropertyMap(context).SetValue(
                row, 0, nameof(TestSheet.Row.Content), "preserved", formatter);

            Assert.Equal("preserved", row.Content);
        }
    }
}
