// BakingSheet, Maxwell Keonwoo Kang <code.athei@gmail.com>, 2022

using System;
using System.Collections;
using System.Collections.Generic;

namespace Cathei.BakingSheet.Internal
{
    internal readonly struct VerticalCollectionTarget
    {
        public PropertyNodeList Owner { get; }
        public PropertyNode Boundary { get; }
        public string MarkerPath { get; }
        public int LeftmostOwnedColumn { get; }

        internal PropertyColumnBinding Binding { get; }
        internal int Depth { get; }

        internal VerticalCollectionTarget(PropertyNodeList owner, PropertyNode boundary,
            string markerPath, int leftmostOwnedColumn,
            PropertyColumnBinding binding, int depth)
        {
            Owner = owner;
            Boundary = boundary;
            MarkerPath = markerPath;
            LeftmostOwnedColumn = leftmostOwnedColumn;
            Binding = binding;
            Depth = depth;
        }
    }

    internal sealed class VerticalCollectionLayout
    {
        private readonly IReadOnlyList<PropertyColumnBinding> _bindings;
        private readonly Dictionary<string, VerticalCollectionTarget> _targetsByPath;

        public IReadOnlyList<VerticalCollectionTarget> Targets { get; }
        internal IReadOnlyList<PropertyColumnBinding> Bindings => _bindings;

        public VerticalCollectionLayout(IReadOnlyList<PropertyColumnBinding> bindings)
        {
            _bindings = bindings;
            _targetsByPath = new Dictionary<string, VerticalCollectionTarget>(StringComparer.Ordinal);

            for (int column = 0; column < bindings.Count; ++column)
            {
                var binding = bindings[column];

                if (binding.Node == null)
                    continue;

                foreach (var owner in binding.VerticalOwners)
                {
                    if (!(owner is PropertyNodeList list) ||
                        !(list.Child is PropertyNodeList ||
                          list.Child is PropertyNodeVerticalDictionary) ||
                        !binding.TryGetAnonymousTargetPath(list, out string markerPath))
                    {
                        continue;
                    }

                    int depth = GetComponentDepth(binding, list.Child);

                    if (_targetsByPath.TryGetValue(markerPath, out var existing))
                    {
                        _targetsByPath[markerPath] = new VerticalCollectionTarget(
                            list,
                            list.Child,
                            markerPath,
                            Math.Min(existing.LeftmostOwnedColumn, column),
                            existing.Binding,
                            Math.Min(existing.Depth, depth));
                    }
                    else
                    {
                        _targetsByPath.Add(markerPath, new VerticalCollectionTarget(
                            list, list.Child, markerPath, column, binding, depth));
                    }
                }
            }

            var targets = new List<VerticalCollectionTarget>(_targetsByPath.Values);
            targets.Sort(CompareTargets);
            Targets = targets;
        }

        public bool TryGetTarget(string markerPath, out VerticalCollectionTarget target)
        {
            return _targetsByPath.TryGetValue(markerPath, out target);
        }

        public bool TryGetBinding(int physicalColumn, out PropertyColumnBinding binding)
        {
            if (physicalColumn < 0 || physicalColumn >= _bindings.Count)
            {
                binding = default;
                return false;
            }

            binding = _bindings[physicalColumn];
            return true;
        }

        public bool IsDictionaryKey(PropertyColumnBinding binding)
        {
            return binding.IsAnyDictionaryKey();
        }

        internal bool TryGetTarget(PropertyNodeList owner, PropertyColumnBinding binding,
            out VerticalCollectionTarget target)
        {
            if (binding.TryGetAnonymousTargetPath(owner, out string markerPath))
                return _targetsByPath.TryGetValue(markerPath, out target);

            target = default;
            return false;
        }

        internal IReadOnlyList<VerticalCollectionTarget> GetRequiredTargets(
            PropertyColumnBinding binding)
        {
            var result = new List<VerticalCollectionTarget>();

            foreach (var target in Targets)
            {
                if (binding.ContainsNode(target.Boundary))
                    result.Add(target);
            }

            return result;
        }

        private static int GetComponentDepth(PropertyColumnBinding binding, PropertyNode boundary)
        {
            for (int i = 0; i < binding.HeaderComponents.Count; ++i)
            {
                if (ReferenceEquals(binding.HeaderComponents[i].Node, boundary))
                    return i;
            }

            return int.MaxValue;
        }

        private static int CompareTargets(
            VerticalCollectionTarget left, VerticalCollectionTarget right)
        {
            int depth = left.Depth.CompareTo(right.Depth);

            if (depth != 0)
                return depth;

            int column = left.LeftmostOwnedColumn.CompareTo(right.LeftmostOwnedColumn);
            return column != 0
                ? column
                : StringComparer.Ordinal.Compare(left.MarkerPath, right.MarkerPath);
        }
    }

    internal sealed class PropertyMapExportRow
    {
        private readonly IReadOnlyDictionary<int, object> _values;

        public int MarkerColumn { get; }
        public string MarkerPath { get; }
        public bool IsMarker => MarkerColumn >= 0;

        internal PropertyMapExportRow(IReadOnlyDictionary<int, object> values)
        {
            _values = values;
            MarkerColumn = -1;
        }

        internal PropertyMapExportRow(VerticalCollectionTarget marker)
        {
            _values = null;
            MarkerColumn = marker.LeftmostOwnedColumn;
            MarkerPath = marker.MarkerPath;
        }

        public bool TryGetValue(int physicalColumn, out object value)
        {
            if (_values == null)
            {
                value = null;
                return false;
            }

            return _values.TryGetValue(physicalColumn, out value);
        }
    }

    internal static class VerticalCollectionExporter
    {
        private sealed class ExportBlock
        {
            public List<Dictionary<int, object>> Rows { get; } = new List<Dictionary<int, object>>();
            public Dictionary<int, List<VerticalCollectionTarget>> Markers { get; } =
                new Dictionary<int, List<VerticalCollectionTarget>>();
            public bool HasData { get; set; }
        }

        private readonly struct OwnerGroupKey : IEquatable<OwnerGroupKey>
        {
            private readonly PropertyNode _owner;
            private readonly string _path;

            public OwnerGroupKey(PropertyNode owner, string path)
            {
                _owner = owner;
                _path = path ?? string.Empty;
            }

            public bool Equals(OwnerGroupKey other)
            {
                return ReferenceEquals(_owner, other._owner) &&
                       StringComparer.Ordinal.Equals(_path, other._path);
            }

            public override bool Equals(object obj)
            {
                return obj is OwnerGroupKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return (System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(_owner) * 397) ^
                       StringComparer.Ordinal.GetHashCode(_path);
            }
        }

        public static IEnumerable<PropertyMapExportRow> Traverse(
            ISheetRow row, VerticalCollectionLayout layout)
        {
            var columns = new List<int>(layout.Bindings.Count);

            for (int i = 0; i < layout.Bindings.Count; ++i)
                columns.Add(i);

            var block = Build(row, layout, columns, 0, new PropertyValueAddress());

            if (block.Rows.Count == 0)
                block.Rows.Add(new Dictionary<int, object>());

            var firstRow = block.Rows[0];

            foreach (int column in columns)
            {
                if (!firstRow.ContainsKey(column))
                    firstRow.Add(column, null);
            }

            if (block.Markers.ContainsKey(0))
            {
                var logicalStart = new Dictionary<int, object>();

                foreach (int column in columns)
                {
                    if (layout.Bindings[column].VerticalOwners.Count != 0)
                        continue;

                    logicalStart[column] = firstRow.TryGetValue(column, out var value)
                        ? value
                        : null;
                    firstRow.Remove(column);
                }

                yield return new PropertyMapExportRow(logicalStart);
            }

            for (int position = 0; position <= block.Rows.Count; ++position)
            {
                if (block.Markers.TryGetValue(position, out var markers))
                {
                    markers.Sort(CompareMarkers);

                    foreach (var marker in markers)
                        yield return new PropertyMapExportRow(marker);
                }

                if (position < block.Rows.Count)
                    yield return new PropertyMapExportRow(block.Rows[position]);
            }
        }

        private static ExportBlock Build(ISheetRow row, VerticalCollectionLayout layout,
            IReadOnlyList<int> columns, int depth, PropertyValueAddress address)
        {
            var direct = new List<int>();
            var groups = new Dictionary<OwnerGroupKey, List<int>>();

            foreach (int column in columns)
            {
                var binding = layout.Bindings[column];

                if (depth >= binding.VerticalOwners.Count)
                {
                    direct.Add(column);
                    continue;
                }

                var owner = binding.VerticalOwners[depth];
                var key = new OwnerGroupKey(owner, binding.GetOwnerPath(owner));

                if (!groups.TryGetValue(key, out var group))
                {
                    group = new List<int>();
                    groups.Add(key, group);
                }

                group.Add(column);
            }

            var result = BuildDirect(row, layout, direct, address);

            foreach (var pair in groups)
            {
                int sampleColumn = pair.Value[0];
                var binding = layout.Bindings[sampleColumn];
                var owner = binding.VerticalOwners[depth];
                ExportBlock groupBlock;

                if (owner is PropertyNodeList list)
                {
                    groupBlock = BuildList(row, layout, pair.Value, depth, address, list, binding);
                }
                else
                {
                    groupBlock = BuildDictionary(row, layout, pair.Value, depth, address,
                        (PropertyNodeVerticalDictionary)owner, binding);
                }

                if (result.HasData && groupBlock.Markers.ContainsKey(0))
                    AppendSequential(result, groupBlock);
                else
                    MergeParallel(result, groupBlock);
            }

            return result;
        }

        private static ExportBlock BuildDirect(ISheetRow row, VerticalCollectionLayout layout,
            IReadOnlyList<int> columns, PropertyValueAddress address)
        {
            var result = new ExportBlock();

            if (columns.Count == 0)
                return result;

            var values = new Dictionary<int, object>();

            foreach (int column in columns)
            {
                var binding = layout.Bindings[column];

                if (!PropertyValueAccessor.TryGetValue(row, binding, address, out var value))
                    continue;

                values[column] = value;

                if (value != null)
                    result.HasData = true;
            }

            result.Rows.Add(values);
            return result;
        }

        private static ExportBlock BuildList(ISheetRow row, VerticalCollectionLayout layout,
            IReadOnlyList<int> columns, int depth, PropertyValueAddress address,
            PropertyNodeList owner, PropertyColumnBinding sampleBinding)
        {
            var result = new ExportBlock();

            if (!PropertyValueAccessor.TryGetNodeValue(row, sampleBinding, owner, address, out var value) ||
                !(value is IList list) || list.Count == 0)
            {
                var values = new Dictionary<int, object>();

                foreach (int column in columns)
                    values.Add(column, null);

                result.Rows.Add(values);
                return result;
            }

            bool explicitInstances = layout.TryGetTarget(owner, sampleBinding, out var target);

            for (int i = 0; i < list.Count; ++i)
            {
                if (explicitInstances)
                    AddMarker(result, result.Rows.Count, target);

                address.SetListIndex(list, i);
                var item = Build(row, layout, columns, depth + 1, address);

                if (!item.HasData && item.Markers.Count == 0 && explicitInstances)
                    continue;

                AppendSequential(result, item);
            }

            address.RemoveList(list);
            return result;
        }

        private static ExportBlock BuildDictionary(ISheetRow row, VerticalCollectionLayout layout,
            IReadOnlyList<int> columns, int depth, PropertyValueAddress address,
            PropertyNodeVerticalDictionary owner, PropertyColumnBinding sampleBinding)
        {
            var result = new ExportBlock();

            if (!PropertyValueAccessor.TryGetNodeValue(row, sampleBinding, owner, address, out var value) ||
                !(value is IDictionary dictionary) || dictionary.Count == 0)
            {
                return result;
            }

            foreach (DictionaryEntry entry in dictionary)
            {
                address.SetDictionaryKey(dictionary, entry.Key);
                var item = Build(row, layout, columns, depth + 1, address);

                if (item.Rows.Count == 0)
                    item.Rows.Add(new Dictionary<int, object>());

                item.HasData = true;
                AppendSequential(result, item);
            }

            address.RemoveDictionary(dictionary);
            return result;
        }

        private static void MergeParallel(ExportBlock target, ExportBlock source)
        {
            while (target.Rows.Count < source.Rows.Count)
                target.Rows.Add(new Dictionary<int, object>());

            for (int i = 0; i < source.Rows.Count; ++i)
            {
                foreach (var pair in source.Rows[i])
                    target.Rows[i][pair.Key] = pair.Value;
            }

            foreach (var pair in source.Markers)
            {
                if (!target.Markers.TryGetValue(pair.Key, out var markers))
                {
                    markers = new List<VerticalCollectionTarget>();
                    target.Markers.Add(pair.Key, markers);
                }

                markers.AddRange(pair.Value);
            }

            target.HasData |= source.HasData;
        }

        private static void AppendSequential(ExportBlock target, ExportBlock source)
        {
            int offset = target.Rows.Count;

            foreach (var row in source.Rows)
                target.Rows.Add(row);

            foreach (var pair in source.Markers)
            {
                foreach (var marker in pair.Value)
                    AddMarker(target, offset + pair.Key, marker);
            }

            target.HasData |= source.HasData;
        }

        private static void AddMarker(
            ExportBlock block, int position, VerticalCollectionTarget marker)
        {
            if (!block.Markers.TryGetValue(position, out var markers))
            {
                markers = new List<VerticalCollectionTarget>();
                block.Markers.Add(position, markers);
            }

            foreach (var existing in markers)
            {
                if (existing.MarkerPath == marker.MarkerPath)
                    return;
            }

            markers.Add(marker);
        }

        private static int CompareMarkers(
            VerticalCollectionTarget left, VerticalCollectionTarget right)
        {
            int depth = left.Depth.CompareTo(right.Depth);

            if (depth != 0)
                return depth;

            int column = left.LeftmostOwnedColumn.CompareTo(right.LeftmostOwnedColumn);
            return column != 0
                ? column
                : StringComparer.Ordinal.Compare(left.MarkerPath, right.MarkerPath);
        }
    }
}
