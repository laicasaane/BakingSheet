// BakingSheet, Maxwell Keonwoo Kang <code.athei@gmail.com>, 2022

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Cathei.BakingSheet.Internal;

namespace Cathei.BakingSheet.Raw
{
    internal enum RawSheetHeaderComponentKind
    {
        Named,
        AnonymousList,
        AnonymousDictionary,
    }

    internal readonly struct RawSheetHeaderComponent
    {
        public RawSheetHeaderComponentKind Kind { get; }
        public string Text { get; }
        public int Column { get; }
        public int Row { get; }
        public PropertyNode Node { get; }

        public string LabelSelector
        {
            get
            {
                if (Text == null)
                    return null;

                switch (Kind)
                {
                    case RawSheetHeaderComponentKind.AnonymousList:
                        return $"{SheetTokens.List.Selector.Start}{Text}" +
                               $"{SheetTokens.List.Selector.End}";
                    case RawSheetHeaderComponentKind.AnonymousDictionary:
                        return $"{SheetTokens.Dictionary.Selector.Start}{Text}" +
                               $"{SheetTokens.Dictionary.Selector.End}";
                    default:
                        return null;
                }
            }
        }

        public string SplitText
        {
            get
            {
                switch (Kind)
                {
                    case RawSheetHeaderComponentKind.Named:
                        return Text;
                    case RawSheetHeaderComponentKind.AnonymousList:
                        return SheetTokens.List.Selector.Anonymous;
                    case RawSheetHeaderComponentKind.AnonymousDictionary:
                        return SheetTokens.Dictionary.Selector.Anonymous;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        public RawSheetHeaderComponent(RawSheetHeaderComponentKind kind, string text,
            int column, int row, PropertyNode node = null)
        {
            Kind = kind;
            Text = text;
            Column = column;
            Row = row;
            Node = node;
        }

        public RawSheetHeaderComponent WithNode(PropertyNode node)
        {
            return new RawSheetHeaderComponent(Kind, Text, Column, Row, node);
        }

        public RawSheetHeaderComponent WithText(string text)
        {
            return new RawSheetHeaderComponent(Kind, text, Column, Row, Node);
        }
    }

    internal readonly struct RawSheetHeaderGroup
    {
        public int ComponentStart { get; }
        public int ComponentCount { get; }
        public int Column { get; }
        public int Row { get; }

        public RawSheetHeaderGroup(int componentStart, int componentCount, int column, int row)
        {
            ComponentStart = componentStart;
            ComponentCount = componentCount;
            Column = column;
            Row = row;
        }
    }

    internal sealed class RawSheetHeaderPath
    {
        public int PhysicalColumn { get; }
        public int LastComponentRow { get; }
        public bool IsComment { get; }
        public bool IsSeparator { get; }
        public bool IsFlat { get; }
        public IReadOnlyList<RawSheetHeaderComponent> Components { get; }
        public IReadOnlyList<RawSheetHeaderGroup> Groups { get; }

        public RawSheetHeaderPath(int physicalColumn, int lastComponentRow,
            bool isComment, bool isSeparator, bool isFlat,
            IReadOnlyList<RawSheetHeaderComponent> components,
            IReadOnlyList<RawSheetHeaderGroup> groups)
        {
            PhysicalColumn = physicalColumn;
            LastComponentRow = lastComponentRow;
            IsComment = isComment;
            IsSeparator = isSeparator;
            IsFlat = isFlat;
            Components = components;
            Groups = groups;
        }

        public bool TryValidateGeometry(
            IReadOnlyList<RawSheetHeaderComponent> resolved,
            IReadOnlyList<RawSheetHeaderComponent> canonical,
            out int invalidColumn, out int invalidRow)
        {
            invalidColumn = PhysicalColumn;
            invalidRow = LastComponentRow;

            if (canonical.Count != resolved.Count)
                return false;

            for (int i = 0; i < canonical.Count; ++i)
            {
                if (canonical[i].Kind != resolved[i].Kind ||
                    canonical[i].Kind == RawSheetHeaderComponentKind.Named &&
                    !StringComparer.Ordinal.Equals(canonical[i].Text, resolved[i].Text))
                {
                    invalidColumn = Components[i].Column;
                    invalidRow = Components[i].Row;
                    return false;
                }
            }

            if (IsFlat)
                return true;

            foreach (var group in Groups)
            {
                if (group.ComponentCount <= 1)
                    continue;

                int expected = 1;
                int position = group.ComponentStart + 1;

                while (position < canonical.Count &&
                       canonical[position].Kind != RawSheetHeaderComponentKind.Named)
                {
                    expected++;
                    position++;
                }

                if (group.ComponentCount != expected)
                {
                    invalidColumn = group.Column;
                    invalidRow = group.Row;
                    return false;
                }
            }

            return true;
        }
    }

    internal sealed class RawSheetHeader
    {
        private sealed class CarriedCell
        {
            public string Value;
            public int Column;
            public int Row;
        }

        public int RowCount { get; }
        public int ColumnCount { get; }
        public IReadOnlyList<RawSheetHeaderPath> Paths { get; }

        private RawSheetHeader(int rowCount, int columnCount,
            IReadOnlyList<RawSheetHeaderPath> paths)
        {
            RowCount = rowCount;
            ColumnCount = columnCount;
            Paths = paths;
        }

        public static bool TryRead(IRawSheetImporterPage page, int rowCount, int columnCount,
            out RawSheetHeader header, out int invalidColumn, out int invalidRow)
        {
            var carried = new CarriedCell[rowCount];
            var paths = new List<RawSheetHeaderPath>(columnCount);

            for (int column = 0; column < columnCount; ++column)
            {
                int lastExplicitRow = -1;

                for (int row = 0; row < rowCount; ++row)
                {
                    string value = page.GetCell(column, row);

                    if (string.IsNullOrEmpty(value))
                        continue;

                    carried[row] = new CarriedCell
                    {
                        Value = value,
                        Column = column,
                        Row = row,
                    };
                    lastExplicitRow = row;
                }

                if (lastExplicitRow < 0)
                {
                    paths.Add(new RawSheetHeaderPath(
                        column, 0, false, true, rowCount == 1,
                        Array.Empty<RawSheetHeaderComponent>(),
                        Array.Empty<RawSheetHeaderGroup>()));
                    continue;
                }

                var cells = new List<CarriedCell>(lastExplicitRow + 1);

                for (int row = 0; row <= lastExplicitRow; ++row)
                {
                    if (carried[row] == null)
                    {
                        header = null;
                        invalidColumn = column;
                        invalidRow = row;
                        return false;
                    }

                    cells.Add(carried[row]);
                }

                string completePath = string.Join(
                    SheetTokens.Separator.Path, cells.Select(x => x.Value));
                bool isComment = SheetTokens.StartsWithComment(
                    completePath, SheetTokens.Comment.Primary);

                if (isComment)
                {
                    paths.Add(new RawSheetHeaderPath(
                        column, lastExplicitRow, true, false, rowCount == 1,
                        Array.Empty<RawSheetHeaderComponent>(),
                        Array.Empty<RawSheetHeaderGroup>()));
                    continue;
                }

                var components = new List<RawSheetHeaderComponent>();
                var groups = new List<RawSheetHeaderGroup>();
                bool flat = rowCount == 1;

                foreach (var cell in cells)
                {
                    int start = components.Count;

                    if (!TryParseCell(cell.Value, flat, cell.Column, cell.Row, components))
                    {
                        header = null;
                        invalidColumn = cell.Column;
                        invalidRow = cell.Row;
                        return false;
                    }

                    groups.Add(new RawSheetHeaderGroup(
                        start, components.Count - start, cell.Column, cell.Row));
                }

                paths.Add(new RawSheetHeaderPath(
                    column, lastExplicitRow, false, false, rowCount == 1, components, groups));
            }

            header = new RawSheetHeader(rowCount, columnCount, paths);
            invalidColumn = -1;
            invalidRow = -1;
            return true;
        }

        public static string FormatFlat(IReadOnlyList<RawSheetHeaderComponent> components)
        {
            return string.Join(SheetTokens.Separator.Path, CompressAnonymousLists(components));
        }

        public static IReadOnlyList<string> FormatSplit(
            IReadOnlyList<RawSheetHeaderComponent> components)
        {
            return components.Select(x => x.SplitText).ToList();
        }

        public static IReadOnlyList<string> FormatHybrid(
            IReadOnlyList<RawSheetHeaderComponent> components)
        {
            var result = new List<string>();

            for (int i = 0; i < components.Count;)
            {
                var current = components[i];

                if (current.Kind != RawSheetHeaderComponentKind.Named)
                {
                    result.Add(current.SplitText);
                    i++;
                    continue;
                }

                string value = current.Text;
                int listCount = 0;
                i++;

                while (i < components.Count &&
                       components[i].Kind != RawSheetHeaderComponentKind.Named)
                {
                    if (components[i].Kind == RawSheetHeaderComponentKind.AnonymousList)
                    {
                        listCount++;
                    }
                    else
                    {
                        if (listCount > 0)
                        {
                            value += $"{SheetTokens.Separator.Path}" +
                                     $"{SheetTokens.List.Selector.Start}{listCount}" +
                                     $"{SheetTokens.List.Selector.End}";
                            listCount = 0;
                        }

                        value += SheetTokens.Separator.Path +
                                 SheetTokens.Dictionary.Selector.Anonymous;
                    }

                    i++;
                }

                if (listCount > 0)
                    value += $"{SheetTokens.Separator.Path}" +
                             $"{SheetTokens.List.Selector.Start}{listCount}" +
                             $"{SheetTokens.List.Selector.End}";

                result.Add(value);
            }

            return result;
        }

        public static string FormatMarker(
            IReadOnlyList<RawSheetHeaderComponent> components, int componentCount)
        {
            return string.Join(
                SheetTokens.Separator.Path,
                CompressAnonymousLists(components.Take(componentCount).ToList()));
        }

        public static bool IsValidName(string name)
        {
            return !string.IsNullOrEmpty(name) &&
                   !SheetTokens.ContainsReservedPathNameCharacter(name) &&
                   !name.Any(char.IsWhiteSpace);
        }

        private static IEnumerable<string> CompressAnonymousLists(
            IReadOnlyList<RawSheetHeaderComponent> components)
        {
            for (int i = 0; i < components.Count;)
            {
                if (components[i].Kind != RawSheetHeaderComponentKind.AnonymousList)
                {
                    yield return components[i].SplitText;
                    i++;
                    continue;
                }

                int count = 0;

                while (i < components.Count &&
                       components[i].Kind == RawSheetHeaderComponentKind.AnonymousList)
                {
                    count++;
                    i++;
                }

                yield return $"{SheetTokens.List.Selector.Start}{count}" +
                             $"{SheetTokens.List.Selector.End}";
            }
        }

        private static bool TryParseCell(string value, bool flat, int column, int row,
            List<RawSheetHeaderComponent> components)
        {
            string[] parts = SheetTokens.SplitPath(value);

            if (!flat && parts.Length > 1)
            {
                if (!TryParseNamed(parts[0], column, row, out var named))
                    return false;

                components.Add(named);

                for (int i = 1; i < parts.Length; ++i)
                {
                    if (!TryParseNestedSelector(parts[i], column, row, components))
                        return false;
                }

                return true;
            }

            foreach (string part in parts)
            {
                if (part == SheetTokens.List.Selector.Anonymous)
                {
                    if (flat)
                        return false;

                    components.Add(new RawSheetHeaderComponent(
                        RawSheetHeaderComponentKind.AnonymousList, null, column, row));
                    continue;
                }

                if (part == SheetTokens.Dictionary.Selector.Anonymous)
                {
                    components.Add(new RawSheetHeaderComponent(
                        RawSheetHeaderComponentKind.AnonymousDictionary, null, column, row));
                    continue;
                }

                if (TryParseListCount(part, out int count))
                {
                    if (!flat || count <= 0)
                        return false;

                    for (int i = 0; i < count; ++i)
                    {
                        components.Add(new RawSheetHeaderComponent(
                            RawSheetHeaderComponentKind.AnonymousList, null, column, row));
                    }

                    continue;
                }

                if (TryParseListLabel(part, out string listLabel))
                {
                    components.Add(new RawSheetHeaderComponent(
                        RawSheetHeaderComponentKind.AnonymousList,
                        listLabel, column, row));
                    continue;
                }

                if (TryParseDictionaryLabel(part, out string dictionaryLabel))
                {
                    components.Add(new RawSheetHeaderComponent(
                        RawSheetHeaderComponentKind.AnonymousDictionary,
                        dictionaryLabel, column, row));
                    continue;
                }

                if (!TryParseNamed(part, column, row, out var named))
                    return false;

                components.Add(named);
            }

            return components.Count > 0;
        }

        private static bool TryParseNestedSelector(string part, int column, int row,
            List<RawSheetHeaderComponent> components)
        {
            if (part == SheetTokens.Dictionary.Selector.Anonymous)
            {
                components.Add(new RawSheetHeaderComponent(
                    RawSheetHeaderComponentKind.AnonymousDictionary, null, column, row));
                return true;
            }

            if (TryParseListCount(part, out int count))
            {
                for (int i = 0; i < count; ++i)
                {
                    components.Add(new RawSheetHeaderComponent(
                        RawSheetHeaderComponentKind.AnonymousList, null, column, row));
                }

                return true;
            }

            if (TryParseListLabel(part, out string listLabel))
            {
                components.Add(new RawSheetHeaderComponent(
                    RawSheetHeaderComponentKind.AnonymousList,
                    listLabel, column, row));
                return true;
            }

            if (TryParseDictionaryLabel(part, out string dictionaryLabel))
            {
                components.Add(new RawSheetHeaderComponent(
                    RawSheetHeaderComponentKind.AnonymousDictionary,
                    dictionaryLabel, column, row));
                return true;
            }

            return false;
        }

        private static bool TryParseNamed(string part, int column, int row,
            out RawSheetHeaderComponent component)
        {
            if (string.IsNullOrEmpty(part) ||
                SheetTokens.ContainsReservedPathNameCharacter(part))
            {
                component = default;
                return false;
            }

            component = new RawSheetHeaderComponent(
                RawSheetHeaderComponentKind.Named, part, column, row);
            return true;
        }

        internal static bool TryParseListCount(string part, out int count)
        {
            count = 0;

            if (part == null || part.Length < 3 ||
                part[0] != SheetTokens.List.Selector.Start[0] ||
                part[part.Length - 1] != SheetTokens.List.Selector.End[0] ||
                part[1] < '1' || part[1] > '9')
            {
                return false;
            }

            for (int i = 2; i < part.Length - 1; ++i)
            {
                if (part[i] < '0' || part[i] > '9')
                    return false;
            }

            return int.TryParse(
                part.Substring(
                    SheetTokens.List.Selector.Start.Length,
                    part.Length - SheetTokens.List.Selector.Start.Length -
                    SheetTokens.List.Selector.End.Length),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out count);
        }

        internal static bool TryParseListLabel(string part, out string label)
        {
            return TryParseLabel(
                part,
                SheetTokens.List.Selector.Start[0],
                SheetTokens.List.Selector.End[0],
                out label);
        }

        internal static bool TryParseDictionaryLabel(string part, out string label)
        {
            return TryParseLabel(
                part,
                SheetTokens.Dictionary.Selector.Start[0],
                SheetTokens.Dictionary.Selector.End[0],
                out label);
        }

        private static bool TryParseLabel(
            string part, char startCharacter, char endCharacter, out string label)
        {
            label = null;

            if (part == null || part.Length < 3 ||
                part[0] != startCharacter || part[part.Length - 1] != endCharacter)
            {
                return false;
            }

            char first = part[1];

            if (!IsAsciiLetter(first) && first != '_')
                return false;

            for (int i = 2; i < part.Length - 1; ++i)
            {
                char character = part[i];

                if (!IsAsciiLetter(character) &&
                    (character < '0' || character > '9') &&
                    character != '_')
                {
                    return false;
                }
            }

            label = part.Substring(1, part.Length - 2);
            return true;
        }

        private static bool IsAsciiLetter(char character)
        {
            return character >= 'A' && character <= 'Z' ||
                   character >= 'a' && character <= 'z';
        }
    }

    internal static class RawSheetMarker
    {
        public static bool IsCandidate(string value)
        {
            return !string.IsNullOrEmpty(value) &&
                   (value.IndexOf(
                        SheetTokens.Collection.Marker.Start, StringComparison.Ordinal) >= 0 ||
                    value.IndexOf(
                        SheetTokens.Collection.Marker.End, StringComparison.Ordinal) >= 0);
        }

        public static bool TryParse(
            string value, out string markerPath, out bool isLabelSelector)
        {
            markerPath = null;
            isLabelSelector = false;

            if (value == null)
                return false;

            int start = 0;

            while (start < value.Length && IsHorizontalSpace(value[start]))
                start++;

            if (start + SheetTokens.Collection.Marker.Start.Length +
                SheetTokens.Collection.Marker.End.Length > value.Length ||
                string.CompareOrdinal(
                    value, start, SheetTokens.Collection.Marker.Start, 0,
                    SheetTokens.Collection.Marker.Start.Length) != 0)
            {
                return false;
            }

            int markerPathStart = start + SheetTokens.Collection.Marker.Start.Length;
            int close = value.IndexOf(
                SheetTokens.Collection.Marker.End, markerPathStart, StringComparison.Ordinal);

            if (close < 0)
                return false;

            string parsedPath = value.Substring(markerPathStart, close - markerPathStart);
            string suffix = value.Substring(close + SheetTokens.Collection.Marker.End.Length);

            if (!TryParseSuffix(suffix))
                return false;

            if (RawSheetHeader.TryParseListLabel(parsedPath, out _) ||
                RawSheetHeader.TryParseDictionaryLabel(parsedPath, out _))
            {
                markerPath = parsedPath;
                isLabelSelector = true;
                return true;
            }

            return TryParsePath(parsedPath, out markerPath);
        }

        public static bool IsHorizontalSpace(string value)
        {
            if (string.IsNullOrEmpty(value))
                return true;

            foreach (char character in value)
            {
                if (!IsHorizontalSpace(character))
                    return false;
            }

            return true;
        }

        private static bool TryParseSuffix(string suffix)
        {
            if (suffix.Length == 0 || IsHorizontalSpace(suffix))
                return true;

            int position = 0;

            while (position < suffix.Length && IsHorizontalSpace(suffix[position]))
                position++;

            return position > 0 &&
                   position + SheetTokens.Comment.Cell.Length <= suffix.Length &&
                   string.CompareOrdinal(
                       suffix, position, SheetTokens.Comment.Cell, 0,
                       SheetTokens.Comment.Cell.Length) == 0;
        }

        private static bool TryParsePath(string path, out string canonicalPath)
        {
            canonicalPath = null;
            string[] parts = SheetTokens.SplitPath(path);

            if (parts.Length == 0)
                return false;

            var normalized = new string[parts.Length];

            for (int i = 0; i < parts.Length; ++i)
            {
                string part = parts[i];

                if (part == SheetTokens.Dictionary.Selector.Anonymous)
                {
                    normalized[i] = part;
                    continue;
                }

                if (TryParseSelector(part, out int selector))
                {
                    normalized[i] = $"{SheetTokens.List.Selector.Start}{selector}" +
                                    $"{SheetTokens.List.Selector.End}";
                    continue;
                }

                if (string.IsNullOrEmpty(part) ||
                    part == SheetTokens.List.Selector.Anonymous ||
                    SheetTokens.ContainsReservedPathNameCharacter(part) ||
                    part.Any(char.IsWhiteSpace))
                {
                    return false;
                }

                normalized[i] = part;
            }

            string last = normalized[normalized.Length - 1];

            if (last != SheetTokens.Dictionary.Selector.Anonymous &&
                last[0] != SheetTokens.List.Selector.Start[0])
                return false;

            canonicalPath = string.Join(SheetTokens.Separator.Path, normalized);
            return true;
        }

        internal static bool TryParseSelector(string value, out int selector)
        {
            return RawSheetHeader.TryParseListCount(value, out selector);
        }

        private static bool IsHorizontalSpace(char character)
        {
            return character == '\t' ||
                   char.GetUnicodeCategory(character) == UnicodeCategory.SpaceSeparator;
        }
    }
}
