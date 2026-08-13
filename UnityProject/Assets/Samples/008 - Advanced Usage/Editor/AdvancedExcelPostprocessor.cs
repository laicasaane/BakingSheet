using System;
using System.Linq;
using System.Threading.Tasks;
using Cathei.BakingSheet.Unity;
using UnityEditor;
using UnityEngine;

namespace Cathei.BakingSheet.AdvancedExamples
{
    public sealed class AdvancedExcelPostprocessor : AssetPostprocessor
    {
        private const string WorkbookAssetPath =
            "Assets/Samples/008 - Advanced Usage/Excel/AdvancedUsage.xlsx";

        private const string WorkbookDirectory =
            "Assets/Samples/008 - Advanced Usage/Excel";

        private const string ScriptableObjectDirectory =
            "Assets/Samples/008 - Advanced Usage/ScriptableObject";

        private static async void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (!importedAssets.Any(path =>
                    string.Equals(path, WorkbookAssetPath, StringComparison.Ordinal)))
            {
                return;
            }

            try
            {
                await ImportWorkbook();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private static async Task ImportWorkbook()
        {
            var sheetContainer = new AdvancedSheetContainer();
            var excelConverter = new ExcelSheetConverter(WorkbookDirectory, TimeZoneInfo.Utc)
            {
                EmptyRowAllowance = 1,
            };

            if (!await sheetContainer.Bake(excelConverter))
            {
                Debug.LogError("Advanced Usage Excel import failed.");
                return;
            }

            var scriptableObjectExporter =
                new ScriptableObjectSheetExporter(ScriptableObjectDirectory);

            if (!await sheetContainer.Store(scriptableObjectExporter))
            {
                Debug.LogError("Advanced Usage ScriptableObject export failed.");
                return;
            }

            AssetDatabase.Refresh();

            Debug.Log(
                "Advanced Usage Excel workbook imported.",
                scriptableObjectExporter.Result
            );
        }
    }
}
