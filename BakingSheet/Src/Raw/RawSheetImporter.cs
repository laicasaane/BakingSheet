// BakingSheet, Maxwell Keonwoo Kang <code.athei@gmail.com>, 2022

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Cathei.BakingSheet.Internal;
using Microsoft.Extensions.Logging;

namespace Cathei.BakingSheet.Raw
{
    /// <summary>
    /// Generic sheet importer for cell-based Spreadsheet sources.
    /// </summary>
    public abstract class RawSheetImporter : ISheetImporter, ISheetFormatter
    {
        private enum MarkerScanResult
        {
            None,
            Valid,
            Invalid,
        }

        protected abstract Task<bool> LoadData();
        protected abstract IEnumerable<IRawSheetImporterPage> GetPages(string sheetName);

        protected virtual int GetColumnCount(
            IRawSheetImporterPage page, int row, int headerColumnCount)
        {
            return headerColumnCount;
        }

        public TimeZoneInfo TimeZoneInfo { get; }
        public IFormatProvider FormatProvider { get; }

        private int _emptyRowAllowance;

        public int EmptyRowAllowance
        {
            get => _emptyRowAllowance;
            set => _emptyRowAllowance = value >= 0
                ? value
                : throw new ArgumentOutOfRangeException(nameof(value));
        }

        private bool _isLoaded;

        public RawSheetImporter(TimeZoneInfo timeZoneInfo, IFormatProvider formatProvider)
        {
            TimeZoneInfo = timeZoneInfo ?? TimeZoneInfo.Utc;
            FormatProvider = formatProvider ?? CultureInfo.InvariantCulture;
        }

        public virtual void Reset()
        {
            _isLoaded = false;
        }

        public async Task<bool> Import(SheetConvertingContext context)
        {
            if (!_isLoaded)
            {
                var success = await LoadData();

                if (!success)
                {
                    context.Logger.LogError("Failed to load data");
                    return false;
                }

                _isLoaded = true;
            }

            foreach (var pair in context.Container.GetSheetProperties())
            {
                using (context.Logger.BeginScope(pair.Key))
                {
                    bool transpose = pair.Value.GetCustomAttribute<TransposeAttribute>() != null;
                    var pages = GetPages(pair.Key).OrderBy(x => x.SubName).ToList();
                    var sheet = pair.Value.GetValue(context.Container) as ISheet;

                    if (sheet == null)
                    {
                        sheet = Activator.CreateInstance(pair.Value.PropertyType) as ISheet;
                        pair.Value.SetValue(context.Container, sheet);
                    }

                    if (sheet == null)
                    {
                        context.Logger.LogError("Failed to create sheet of type {SheetType}", pair.Value.PropertyType);
                        continue;
                    }

                    var propertyMap = sheet.GetPropertyMap(context);

                    if (pages.Count > 0)
                        propertyMap.ReportUnsupportedProperties(context);

                    foreach (var page in pages)
                    {
                        ImportPage(
                            transpose ? new TransposedRawSheetImporterPage(page) : page,
                            context, sheet, propertyMap);
                    }
                }
            }

            return true;
        }

        private void ImportPage(IRawSheetImporterPage page, SheetConvertingContext context,
            ISheet sheet, PropertyMap propertyMap)
        {
            var idColumnName = page.GetCell(0, 0);

            if (idColumnName != nameof(ISheetRow.Id))
            {
                context.Logger.LogError("First column \"{ColumnName}\" must be named \"Id\"", idColumnName);
                return;
            }

            ReadHeaderDimensions(page, out int headerRowCount, out int headerColumnCount);

            if (!RawSheetHeader.TryRead(
                    page, headerRowCount, headerColumnCount,
                    out var header, out int invalidHeaderColumn, out int invalidHeaderRow))
            {
                LogInvalidHeader(context, page, invalidHeaderColumn, invalidHeaderRow);
                return;
            }

            var bindings = new List<PropertyColumnBinding>(header.ColumnCount);
            var semanticPaths = new HashSet<string>(StringComparer.Ordinal);

            foreach (var path in header.Paths)
            {
                if (path.IsComment || path.IsSeparator)
                {
                    bindings.Add(default);
                    continue;
                }

                if (!propertyMap.TryResolveBinding(
                        path, this, out var binding, out int invalidComponent, out bool ignored))
                {
                    var component = path.Components[Math.Max(0, invalidComponent)];
                    LogInvalidHeader(context, page, component.Column, component.Row);
                    return;
                }

                if (ignored)
                {
                    bindings.Add(default);
                    continue;
                }

                if (!path.TryValidateGeometry(
                        binding.HeaderComponents,
                        out invalidHeaderColumn, out invalidHeaderRow))
                {
                    LogInvalidHeader(context, page, invalidHeaderColumn, invalidHeaderRow);
                    return;
                }

                if (!semanticPaths.Add(binding.SemanticPath))
                {
                    LogInvalidHeader(
                        context, page, path.PhysicalColumn, path.LastComponentRow);
                    return;
                }

                bindings.Add(binding);
            }

            var layout = propertyMap.CreateLayout(bindings);
            var rowState = new VerticalCollectionRowState(layout);
            ISheetRow sheetRow = null;
            string rowId = null;
            bool discardingRow = false;
            int emptyRowStreak = 0;

            for (int pageRow = header.RowCount; ; ++pageRow)
            {
                int columnCount = GetPhysicalColumnCount(
                    page, pageRow, header.ColumnCount);

                if (IsWhitespaceRow(page, pageRow, columnCount))
                {
                    emptyRowStreak++;

                    if (emptyRowStreak > EmptyRowAllowance)
                        break;

                    continue;
                }

                emptyRowStreak = 0;
                string idCellValue = page.GetCell(0, pageRow);
                bool blankId = string.IsNullOrWhiteSpace(idCellValue);

                if (!blankId && Config.StartsWithComment(idCellValue, Config.Comment))
                    continue;

                if (!blankId)
                {
                    FinalizeRow(context, sheet, rowState, sheetRow, rowId);

                    rowId = idCellValue;
                    sheetRow = Activator.CreateInstance(sheet.RowType) as ISheetRow;
                    discardingRow = false;

                    if (sheetRow != null)
                        rowState.BeginRow(sheetRow);
                }
                else if (discardingRow)
                {
                    continue;
                }

                var markerResult = ScanMarkerRow(
                    page, pageRow, columnCount, header, layout,
                    out var marker, out int markerColumn, out int invalidMarkerColumn);

                if (markerResult != MarkerScanResult.None)
                {
                    bool invalid = markerResult == MarkerScanResult.Invalid ||
                                   sheetRow == null || discardingRow;

                    if (!invalid)
                        invalid = !rowState.BeginTarget(marker);

                    if (invalid)
                    {
                        int column = invalidMarkerColumn >= 0
                            ? invalidMarkerColumn
                            : markerColumn;
                        context.Logger.LogError(
                            "Invalid vertical collection marker at cell \"{Cell}\".",
                            GetCellName(page, column, pageRow));
                        rowState.DiscardRow();
                        sheetRow = null;
                        discardingRow = true;
                    }

                    continue;
                }

                if (sheetRow == null)
                    continue;

                using (context.Logger.BeginScope(rowId))
                {
                    bool rowFailed = ImportDataRow(
                        page, context, propertyMap, rowState,
                        bindings, header, pageRow);

                    if (rowFailed)
                    {
                        rowState.DiscardRow();
                        sheetRow = null;
                        discardingRow = true;
                        continue;
                    }

                    rowState.EndDataRow(context);
                }
            }

            FinalizeRow(context, sheet, rowState, sheetRow, rowId);
        }

        private bool ImportDataRow(IRawSheetImporterPage page, SheetConvertingContext context,
            PropertyMap propertyMap, VerticalCollectionRowState rowState,
            IReadOnlyList<PropertyColumnBinding> bindings, RawSheetHeader header, int pageRow)
        {
            for (int pageColumn = 0; pageColumn < bindings.Count; ++pageColumn)
            {
                var path = header.Paths[pageColumn];

                if (path.IsComment || path.IsSeparator)
                    continue;

                string cellValue = page.GetCell(pageColumn, pageRow);

                if (string.IsNullOrEmpty(cellValue) ||
                    pageColumn > 0 && Config.StartsWithComment(cellValue, Config.DataCellComment))
                    continue;

                var binding = bindings[pageColumn];

                if (binding.Node == null)
                    continue;

                if (!rowState.HasRequiredContext(binding))
                {
                    context.Logger.LogError(
                        "Vertical collection marker required before cell \"{Cell}\".",
                        GetCellName(page, pageColumn, pageRow));
                    return true;
                }

                using (context.Logger.BeginScope(binding.Path))
                {
                    try
                    {
                        if (propertyMap.TryConvertValue(binding, cellValue, this, out var converted))
                        {
                            rowState.SetValue(binding, converted);
                        }
                        else
                        {
                            rowState.RejectValue(binding);
                        }
                    }
                    catch (Exception ex)
                    {
                        if (pageColumn == 0)
                        {
                            context.Logger.LogError(ex, "Failed to set id \"{CellValue}\"", cellValue);
                            return true;
                        }

                        context.Logger.LogError(ex, "Failed to set value \"{CellValue}\"", cellValue);
                        rowState.RejectValue(binding);
                    }
                }
            }

            return false;
        }

        private static void FinalizeRow(SheetConvertingContext context, ISheet sheet,
            VerticalCollectionRowState rowState, ISheetRow sheetRow, string rowId)
        {
            if (sheetRow == null)
                return;

            using (context.Logger.BeginScope(rowId))
            {
                if (!rowState.EndRow(context))
                    return;

                if (sheet.Contains(sheetRow.Id))
                {
                    context.Logger.LogError("Already has row with id \"{RowId}\"", sheetRow.Id);
                }
                else
                {
                    sheet.Add(sheetRow);
                }
            }
        }

        private MarkerScanResult ScanMarkerRow(
            IRawSheetImporterPage page, int row, int columnCount,
            RawSheetHeader header, VerticalCollectionLayout layout,
            out VerticalCollectionTarget marker,
            out int markerColumn, out int invalidColumn)
        {
            marker = default;
            markerColumn = -1;
            invalidColumn = -1;
            int ordinaryColumn = -1;

            for (int column = 0; column < columnCount; ++column)
            {
                string value = page.GetCell(column, row);

                if (RawSheetMarker.IsHorizontalSpace(value))
                    continue;

                bool hasHeaderPath = column < header.Paths.Count;
                bool isCommentColumn = hasHeaderPath &&
                                       header.Paths[column].IsComment;
                bool isSeparatorColumn = hasHeaderPath &&
                                         header.Paths[column].IsSeparator;

                if (column > 0 && hasHeaderPath && !isCommentColumn && !isSeparatorColumn &&
                    Config.StartsWithComment(value, Config.DataCellComment))
                    continue;

                bool isCandidate = !isCommentColumn && RawSheetMarker.IsCandidate(value);

                if (!isCandidate)
                {
                    if (markerColumn >= 0)
                    {
                        invalidColumn = column;
                        return MarkerScanResult.Invalid;
                    }

                    if (ordinaryColumn < 0)
                        ordinaryColumn = column;

                    continue;
                }

                if (ordinaryColumn >= 0 || markerColumn >= 0 ||
                    column == 0 && !string.IsNullOrWhiteSpace(value))
                {
                    invalidColumn = column;
                    return MarkerScanResult.Invalid;
                }

                markerColumn = column;

                if (!RawSheetMarker.TryParse(value, out string markerPath) ||
                    !layout.TryGetTarget(markerPath, out marker))
                {
                    invalidColumn = column;
                    return MarkerScanResult.Invalid;
                }
            }

            return markerColumn >= 0 ? MarkerScanResult.Valid : MarkerScanResult.None;
        }

        private void ReadHeaderDimensions(
            IRawSheetImporterPage page, out int rowCount, out int columnCount)
        {
            rowCount = 1;
            columnCount = GetPhysicalColumnCount(page, 0, 1);

            while (true)
            {
                int currentWidth = GetPhysicalColumnCount(page, rowCount, columnCount);
                int scanWidth = Math.Max(columnCount, currentWidth);

                if (!string.IsNullOrEmpty(page.GetCell(0, rowCount)) ||
                    IsNullOrEmptyRow(page, rowCount, scanWidth))
                {
                    break;
                }

                columnCount = scanWidth;
                rowCount++;
            }

            int lastHeaderColumn = 0;

            for (int row = 0; row < rowCount; ++row)
            {
                int currentWidth = GetPhysicalColumnCount(page, row, columnCount);
                columnCount = Math.Max(columnCount, currentWidth);

                for (int column = 0; column < currentWidth; ++column)
                {
                    if (!string.IsNullOrEmpty(page.GetCell(column, row)))
                        lastHeaderColumn = Math.Max(lastHeaderColumn, column);
                }
            }

            columnCount = lastHeaderColumn + 1;
        }

        private int GetPhysicalColumnCount(
            IRawSheetImporterPage page, int row, int headerColumnCount)
        {
            if (page is TransposedRawSheetImporterPage transposedPage)
                return Math.Max(headerColumnCount, transposedPage.LogicalColumnCount);

            return Math.Max(headerColumnCount, GetColumnCount(page, row, headerColumnCount));
        }

        private static bool IsNullOrEmptyRow(
            IRawSheetImporterPage page, int row, int columnCount)
        {
            for (int column = 0; column < columnCount; ++column)
            {
                if (!string.IsNullOrEmpty(page.GetCell(column, row)))
                    return false;
            }

            return true;
        }

        private static bool IsWhitespaceRow(
            IRawSheetImporterPage page, int row, int columnCount)
        {
            for (int column = 0; column < columnCount; ++column)
            {
                if (!string.IsNullOrWhiteSpace(page.GetCell(column, row)))
                    return false;
            }

            return true;
        }

        private static void LogInvalidHeader(SheetConvertingContext context,
            IRawSheetImporterPage page, int column, int row)
        {
            context.Logger.LogError(
                "Invalid sheet header at cell \"{Cell}\".",
                GetCellName(page, column, row));
        }

        private static string GetCellName(int column, int row)
        {
            int number = column + 1;
            string letters = string.Empty;

            while (number > 0)
            {
                number--;
                letters = (char)('A' + number % 26) + letters;
                number /= 26;
            }

            return $"{letters}{row + 1}";
        }

        private static string GetCellName(IRawSheetImporterPage page, int column, int row)
        {
            if (page is TransposedRawSheetImporterPage transposedPage)
                transposedPage.GetSourceCoordinates(column, row, out column, out row);

            return GetCellName(column, row);
        }
    }
}
