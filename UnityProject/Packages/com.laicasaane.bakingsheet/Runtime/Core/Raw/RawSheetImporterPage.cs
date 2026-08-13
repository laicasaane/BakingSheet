// BakingSheet, Maxwell Keonwoo Kang <code.athei@gmail.com>, 2022

namespace Cathei.BakingSheet.Raw
{
    /// <summary>
    /// Single page of a Spreadsheet workbook for importing.
    /// </summary>
    public interface IRawSheetImporterPage
    {
        /// <summary>
        /// Sub-name of current page, for ordering.
        /// </summary>
        string SubName { get; }

        /// <summary>
        /// Get string evaluation of a cell.
        /// Returns null if cell does not exist or out of bounds.
        /// </summary>
        string GetCell(int col, int row);
    }

    internal sealed class TransposedRawSheetImporterPage : IRawSheetImporterPage
    {
        private readonly IRawSheetImporterPage _source;

        public string SubName => _source.SubName;
        public int LogicalColumnCount { get; }

        public TransposedRawSheetImporterPage(IRawSheetImporterPage source)
        {
            _source = source;

            int physicalRow = 0;
            while (!source.IsEmptyRow(physicalRow))
                physicalRow++;

            LogicalColumnCount = physicalRow;
        }

        public string GetCell(int col, int row)
        {
            return _source.GetCell(row, col);
        }

        public void GetSourceCoordinates(int col, int row, out int sourceColumn, out int sourceRow)
        {
            sourceColumn = row;
            sourceRow = col;
        }
    }

    public static class RawSheetImporterPageExtensions
    {
        public static bool IsEmptyCell(this IRawSheetImporterPage page, int col, int row)
        {
            return string.IsNullOrEmpty(page.GetCell(col, row));
        }

        /// <summary>
        /// If the row has no value in all valid column it count as empty row.
        /// </summary>
        public static bool IsEmptyRow(this IRawSheetImporterPage page, int row)
        {
            for (int col = 0; IsValidColumn(page, col, row); ++col)
            {
                if (!page.IsEmptyCell(col, row))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// If the column has any value until current row, it count as valid column.
        /// </summary>
        private static bool IsValidColumn(IRawSheetImporterPage page, int col, int row)
        {
            for (int prevRow = 0; prevRow <= row; ++prevRow)
            {
                if (!page.IsEmptyCell(col, prevRow))
                    return true;
            }

            return false;
        }

        // public static void Import(this IRawSheetImporterPage page,
        //     RawSheetImporter importer, SheetConvertingContext context, ISheet sheet)
        // {
        //     var current = page;
        //
        //     while (current != null)
        //     {
        //         ImportPage(current, importer, context, sheet);
        //         current = current.Next;
        //     }
        // }
    }
}
