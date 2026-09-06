// BakingSheet, Maxwell Keonwoo Kang <code.athei@gmail.com>, 2022

namespace Cathei.BakingSheet.Raw
{
    internal readonly struct RawSheetLogContext
    {
        public string SheetProperty { get; }
        public string SheetName { get; }
        public string PageName { get; }

        public RawSheetLogContext(string sheetProperty, string sheetName, string pageName)
        {
            SheetProperty = sheetProperty;
            SheetName = sheetName;
            PageName = string.IsNullOrEmpty(pageName) ? "(default)" : pageName;
        }
    }
}
