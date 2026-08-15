// BakingSheet, Maxwell Keonwoo Kang <code.athei@gmail.com>, 2022

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Cathei.BakingSheet.Internal;
using Microsoft.Extensions.Logging;

namespace Cathei.BakingSheet.Raw
{
    public enum HeaderMode
    {
        Hybrid = 0,
        Split = 1,
        Flat = 2,
    }

    /// <summary>
    /// Generic sheet converter for cell-based Spreadsheet sources.
    /// </summary>
    public abstract class RawSheetConverter : RawSheetImporter, ISheetConverter
    {
        private HeaderMode _headerMode;

        public HeaderMode HeaderMode
        {
            get => _headerMode;
            set
            {
                switch (value)
                {
                    case HeaderMode.Hybrid:
                    case HeaderMode.Split:
                    case HeaderMode.Flat:
                        _headerMode = value;
                        return;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(value));
                }
            }
        }

        protected abstract Task<bool> SaveData();
        protected abstract IRawSheetExporterPage CreatePage(string sheetName);

        protected virtual string GetExportSheetName(
            PropertyInfo sheetProperty, ISheet sheet)
        {
            return sheet.Name;
        }

        protected virtual string ToExternalName(
            PropertyInfo sheetProperty, ISheet sheet, string propertyName)
        {
            return propertyName;
        }

        protected RawSheetConverter(TimeZoneInfo timeZoneInfo, IFormatProvider formatProvider)
            : base(timeZoneInfo, formatProvider)
        {
        }

        public async Task<bool> Export(SheetConvertingContext context)
        {
            foreach (var pair in context.Container.GetSheetProperties())
            {
                using (context.Logger.BeginScope(pair.Key))
                {
                    if (!ShouldProcessSheet(context, pair.Value))
                        continue;

                    var sheet = pair.Value.GetValue(context.Container) as ISheet;
                    if (sheet == null)
                        continue;

                    string sheetName = GetExportSheetName(pair.Value, sheet);

                    if (string.IsNullOrEmpty(sheetName))
                        throw new InvalidOperationException("Export sheet name must not be empty.");

                    var page = CreatePage(sheetName);
                    ExportPage(
                        pair.Value.GetCustomAttribute<TransposeAttribute>() == null
                            ? page
                            : new TransposedRawSheetExporterPage(page),
                        context, pair.Value, sheet);
                }
            }

            var success = await SaveData();

            if (!success)
            {
                context.Logger.LogError("Failed to save data");
                return false;
            }

            return true;
        }


        private void ExportPage(IRawSheetExporterPage page, SheetConvertingContext context,
            PropertyInfo sheetProperty, ISheet sheet)
        {
            var propertyMap = sheet.GetPropertyMap(context);
            var resolver = context.Container.ContractResolver;

            propertyMap.ReportUnsupportedProperties(context);

            propertyMap.UpdateIndex(sheet);

            var bindings = propertyMap.GetCurrentBindings(
                name => ToExternalName(sheetProperty, sheet, name));
            var semanticPaths = new HashSet<string>(StringComparer.Ordinal);

            foreach (var binding in bindings)
            {
                if (!semanticPaths.Add(binding.SemanticPath))
                {
                    throw new InvalidOperationException(
                        $"Mapped semantic path \"{binding.SemanticPath}\" is duplicated.");
                }
            }

            var layout = propertyMap.CreateLayout(bindings);

            var valueContext = new SheetValueConvertingContext(this, resolver);
            var previousHeaderValues = new List<string>();
            int headerRowCount = 0;

            for (int pageColumn = 0; pageColumn < bindings.Count; ++pageColumn)
            {
                var binding = bindings[pageColumn];
                IReadOnlyList<string> rows;

                switch (HeaderMode)
                {
                    case HeaderMode.Hybrid:
                        rows = RawSheetHeader.FormatHybrid(binding.HeaderComponents);
                        break;
                    case HeaderMode.Split:
                        rows = RawSheetHeader.FormatSplit(binding.HeaderComponents);
                        break;
                    case HeaderMode.Flat:
                        rows = new[] { RawSheetHeader.FormatFlat(binding.HeaderComponents) };
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(HeaderMode));
                }

                headerRowCount = Math.Max(headerRowCount, rows.Count);

                for (int row = 0; row < rows.Count; ++row)
                {
                    while (previousHeaderValues.Count <= row)
                        previousHeaderValues.Add(null);

                    if (previousHeaderValues[row] == rows[row])
                        continue;

                    previousHeaderValues[row] = rows[row];
                    page.SetCell(pageColumn, row, rows[row]);
                }
            }

            int pageRow = headerRowCount;

            foreach (ISheetRow sheetRow in sheet)
            {
                foreach (var exportRow in propertyMap.TraverseExportRows(sheetRow, layout))
                {
                    if (exportRow.IsMarker)
                    {
                        for (int column = 0; column < bindings.Count; ++column)
                            page.SetCell(column, pageRow, null);

                        page.SetCell(
                            exportRow.MarkerColumn,
                            pageRow,
                            $"{SheetTokens.Collection.Marker.Start}" +
                            $"{exportRow.MarkerPath}{SheetTokens.Collection.Marker.End}");
                        pageRow++;
                        continue;
                    }

                    int pageColumn = 0;

                    foreach (var binding in bindings)
                    {
                        if (exportRow.TryGetValue(pageColumn, out var value))
                        {
                            string valueString = value == null
                                ? null
                                : binding.Node.ValueConverter.ValueToString(
                                    binding.Node.ValueType, value, valueContext);

                            page.SetCell(pageColumn, pageRow, valueString);
                        }

                        pageColumn++;
                    }

                    pageRow++;
                }
            }
        }
    }
}
