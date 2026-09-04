// BakingSheet, Maxwell Keonwoo Kang <code.athei@gmail.com>, 2022

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Cathei.BakingSheet.Raw;
using Microsoft.Extensions.Logging;

namespace Cathei.BakingSheet.Internal
{
    internal enum PropertyPathState
    {
        Invalid,
        Branch,
        Leaf,
    }

    /// <summary>
    /// Tree structure represents assignable properties in sheet rows.
    /// </summary>
    public partial class PropertyMap
    {
        private PropertyNodeObject Root { get; }
        private PropertyNodeList Arr { get; }

        private readonly SheetConvertingContext _context;

        private readonly List<object> _indexes = new();
        private readonly List<PropertyNodeIgnored> _unsupportedProperties = new();

        private HashSet<string> _warned = null;

        private int _maxDepth;

        public int MaxDepth => _maxDepth;

        private static IEnumerable<string> ParseFlattenPath(string path)
        {
            int idx = 0;
            int next = path.IndexOf(SheetTokens.Separator.Path, StringComparison.Ordinal);

            while (next != -1)
            {
                yield return path.Substring(idx, next - idx);

                idx = next + SheetTokens.Separator.Path.Length;
                next = path.IndexOf(SheetTokens.Separator.Path, idx, StringComparison.Ordinal);
            }

            yield return path.Substring(idx);
        }

        internal static Type[] GetGenericArguments(Type type, Type baseType)
        {
            if (baseType.IsInterface)
            {
                // just use the first interface available
                foreach (var impl in type.GetInterfaces())
                {
                    if (impl.IsGenericType && impl.GetGenericTypeDefinition() == baseType)
                        return impl.GetGenericArguments();
                }
            }
            else
            {
                Type current = type;

                while (current != null)
                {
                    if (current.IsGenericType && current.GetGenericTypeDefinition() == baseType)
                        return current.GetGenericArguments();
                    current = current.BaseType;
                }
            }

            throw new InvalidOperationException($"Type {type} does not implement {baseType}");
        }

        private static bool RootGetter(PropertyNode child, object obj, object key, out object value)
        {
            value = obj;
            return true;
        }

        public PropertyMap(SheetConvertingContext context, Type sheetType)
        {
            _context = context;

            var resolver = context.Container.ContractResolver;
            var rowType = GetGenericArguments(sheetType, typeof(ISheet<,>))[1];

            Root = new PropertyNodeObject(null, null, rowType, RootGetter, null, null, resolver, 0);

            _maxDepth = Root.CalculateDepth();

            if (typeof(ISheetRowArray).IsAssignableFrom(rowType))
            {
                var arrPropertyInfo = SheetTokens.GetRowArrayProperty(rowType);

                Debug.Assert(arrPropertyInfo != null);

                Arr = new PropertyNodeList(null, null, arrPropertyInfo.PropertyType,
                    PropertyNodeObject.ValueGetter, null, arrPropertyInfo, resolver, 0, true);

                _maxDepth = Math.Max(_maxDepth, Arr.CalculateDepth());
            }

            Root.CollectUnsupportedProperties(_unsupportedProperties);

            if (Arr != null)
                Arr.CollectUnsupportedProperties(_unsupportedProperties);
        }

        internal void ReportUnsupportedProperties(SheetConvertingContext context)
        {
            foreach (var node in _unsupportedProperties)
            {
                context.Logger.LogError(
                    "Property \"{PropertyPath}\" has unsupported vertical collection type \"{PropertyType}\".",
                    node.FullPath, node.ValueType);
            }
        }

        /// <summary>
        /// Set value of a specific property of a row.
        /// </summary>
        /// <param name="row">Target row.</param>
        /// <param name="vindex">Vertical index of the row.</param>
        /// <param name="path">Path to the node (Column name).</param>
        /// <param name="value">Value to assign.</param>
        /// <param name="formatter">Format provider to convert value to object.</param>
        public void SetValue(ISheetRow row, int vindex, string path, string value, ISheetFormatter formatter)
        {
            if (!TryResolveBinding(path, formatter, out var binding))
                return;

            int verticalListCount = 0;

            foreach (var owner in binding.VerticalOwners)
            {
                if (owner is PropertyNodeVerticalDictionary)
                {
                    _context.Logger.LogError("Nested vertical list is not supported");
                    return;
                }

                if (owner is PropertyNodeList)
                    verticalListCount++;
            }

            if (verticalListCount > 1)
            {
                _context.Logger.LogError("Nested vertical list is not supported");
                return;
            }

            if (verticalListCount == 0 && vindex != 0)
            {
                _context.Logger.LogError("There is multiple value for a non-vertical column");
                return;
            }

            if (!TryConvertValue(binding, value, formatter, out var converted))
                return;

            binding.Node.SetValue(row, vindex, binding.Indexes.GetEnumerator(), converted);
        }

        internal bool TryResolveBinding(string path, ISheetFormatter formatter, out PropertyColumnBinding binding)
        {
            return TryResolveBinding(path, formatter, true, out binding);
        }

        internal bool TryResolveBinding(string path, ISheetFormatter formatter,
            bool reportInvalid, out PropertyColumnBinding binding)
        {
            PropertyNode node = null;
            var resolver = _context.Container.ContractResolver;
            var valueContext = new SheetValueConvertingContext(formatter, resolver);
            var indexes = new List<object>();
            var horizontalIndexes = new Dictionary<PropertyNode, object>();

            foreach (var subpath in ParseFlattenPath(path))
            {
                if (node == null)
                {
                    if (Root.HasSubpath(subpath))
                    {
                        node = Root.ColumnNode;
                    }
                    else if (Arr != null && Arr.ColumnNode.HasSubpath(subpath))
                    {
                        node = Arr.ColumnNode;
                    }
                    else
                    {
                        if (reportInvalid)
                            ReportInvalidColumn(path);
                        binding = default;
                        return false;
                    }
                }

                if (node.IndexType != null)
                {
                    object index = valueContext.StringToValue(node.IndexType, subpath);
                    indexes.Add(index);
                    horizontalIndexes[node] = index;
                }

                node = node.GetChild(subpath);

                if (node == null)
                {
                    if (reportInvalid)
                        ReportInvalidColumn(path);
                    binding = default;
                    return false;
                }

                if (node.IsIgnored)
                {
                    binding = default;
                    return false;
                }

                node = node.ColumnNode;
            }

            Debug.Assert(node != null);
            binding = CreateBinding(node, indexes, path, horizontalIndexes);
            return true;
        }

        internal bool TryResolveBinding(RawSheetHeaderPath path, ISheetFormatter formatter,
            Func<string, string> memberNameMapper,
            out PropertyColumnBinding binding,
            out IReadOnlyList<RawSheetHeaderComponent> resolvedComponents,
            out int invalidComponent, out bool ignored)
        {
            var state = ResolvePath(
                path, formatter, memberNameMapper,
                out var cursor, out var indexes, out var horizontalIndexes,
                out resolvedComponents, out invalidComponent, out ignored);

            if (ignored)
            {
                binding = default;
                invalidComponent = -1;
                return true;
            }

            if (state != PropertyPathState.Leaf)
            {
                binding = default;
                return false;
            }

            string semanticPath = RawSheetHeader.FormatFlat(resolvedComponents);
            binding = CreateBinding(cursor, indexes, semanticPath, horizontalIndexes);
            return true;
        }

        internal PropertyPathState ClassifyPath(RawSheetHeaderPath path, ISheetFormatter formatter,
            Func<string, string> memberNameMapper,
            out IReadOnlyList<RawSheetHeaderComponent> resolvedComponents,
            out int invalidComponent)
        {
            var state = ResolvePath(
                path, formatter, memberNameMapper,
                out _, out _, out _, out resolvedComponents, out invalidComponent, out bool ignored);

            return ignored ? PropertyPathState.Invalid : state;
        }

        private PropertyPathState ResolvePath(RawSheetHeaderPath path, ISheetFormatter formatter,
            Func<string, string> memberNameMapper,
            out PropertyNode node, out List<object> indexes,
            out Dictionary<PropertyNode, object> horizontalIndexes,
            out IReadOnlyList<RawSheetHeaderComponent> resolvedComponents,
            out int invalidComponent, out bool ignored)
        {
            var resolver = _context.Container.ContractResolver;
            var valueContext = new SheetValueConvertingContext(formatter, resolver);
            indexes = new List<object>();
            horizontalIndexes = new Dictionary<PropertyNode, object>();
            PropertyNode cursor = null;
            bool anonymousDictionaryRequired = false;
            var components = new List<RawSheetHeaderComponent>(path.Components.Count);
            ignored = false;

            for (int i = 0; i < path.Components.Count; ++i)
            {
                var component = path.Components[i];
                bool componentMapped = false;
                bool componentRecorded = false;

                if (cursor == null)
                {
                    if (component.Kind != RawSheetHeaderComponentKind.Named)
                    {
                        node = null;
                        resolvedComponents = components;
                        invalidComponent = i;
                        return PropertyPathState.Invalid;
                    }

                    string rootName = memberNameMapper(component.Text);

                    if (!RawSheetHeader.IsValidName(rootName))
                    {
                        node = null;
                        resolvedComponents = components;
                        invalidComponent = i;
                        return PropertyPathState.Invalid;
                    }

                    component = component.WithText(rootName);
                    componentMapped = true;
                    componentRecorded = true;
                    components.Add(component);

                    if (Root.HasSubpath(rootName))
                    {
                        cursor = Root;
                    }
                    else if (Arr != null && Arr.ColumnNode.HasSubpath(rootName))
                    {
                        cursor = Arr.Child;
                    }
                    else
                    {
                        node = null;
                        resolvedComponents = components;
                        invalidComponent = i;
                        return PropertyPathState.Invalid;
                    }
                }

                if (component.Kind == RawSheetHeaderComponentKind.AnonymousList)
                {
                    if (anonymousDictionaryRequired ||
                        !(cursor is PropertyNodeList list) || !list.IsVerticalList)
                    {
                        node = null;
                        resolvedComponents = components;
                        invalidComponent = i;
                        return PropertyPathState.Invalid;
                    }

                    cursor = list.Child;
                    anonymousDictionaryRequired = cursor is PropertyNodeVerticalDictionary;
                    components.Add(component);
                    continue;
                }

                if (component.Kind == RawSheetHeaderComponentKind.AnonymousDictionary)
                {
                    if (!anonymousDictionaryRequired ||
                        !(cursor is PropertyNodeVerticalDictionary))
                    {
                        node = null;
                        resolvedComponents = components;
                        invalidComponent = i;
                        return PropertyPathState.Invalid;
                    }

                    anonymousDictionaryRequired = false;
                    components.Add(component);
                    continue;
                }

                string componentName = component.Text;

                if (!componentMapped &&
                    (cursor is PropertyNodeObject || cursor is PropertyNodeVerticalDictionary))
                {
                    componentName = memberNameMapper(componentName);

                    if (!RawSheetHeader.IsValidName(componentName))
                    {
                        node = null;
                        resolvedComponents = components;
                        invalidComponent = i;
                        return PropertyPathState.Invalid;
                    }

                    component = component.WithText(componentName);
                }

                if (anonymousDictionaryRequired ||
                    !TryResolveNamedComponent(cursor, componentName, valueContext,
                        indexes, horizontalIndexes, out var child) ||
                    child == null)
                {
                    node = null;
                    resolvedComponents = components;
                    invalidComponent = i;
                    return PropertyPathState.Invalid;
                }

                if (!componentRecorded)
                    components.Add(component);

                if (child.IsIgnored)
                {
                    node = null;
                    resolvedComponents = components;
                    invalidComponent = i;
                    ignored = true;
                    return PropertyPathState.Leaf;
                }

                if (child is PropertyNodeList verticalList && verticalList.IsVerticalList)
                {
                    cursor = verticalList.Child;
                    anonymousDictionaryRequired = cursor is PropertyNodeVerticalDictionary;
                }
                else
                {
                    cursor = child;
                    anonymousDictionaryRequired = false;
                }
            }

            node = cursor;
            resolvedComponents = components;
            invalidComponent = Math.Max(0, path.Components.Count - 1);

            if (cursor == null)
                return PropertyPathState.Invalid;

            if (anonymousDictionaryRequired || !cursor.IsLeafNode)
            {
                return PropertyPathState.Branch;
            }

            invalidComponent = -1;
            return PropertyPathState.Leaf;
        }

        internal bool TryResolveMarkerPath(string path, ISheetFormatter formatter,
            Func<string, string> memberNameMapper, out string resolvedPath)
        {
            var resolver = _context.Container.ContractResolver;
            var valueContext = new SheetValueConvertingContext(formatter, resolver);
            var indexes = new List<object>();
            var horizontalIndexes = new Dictionary<PropertyNode, object>();
            var resolvedParts = new List<string>();
            PropertyNode cursor = null;
            bool anonymousDictionaryRequired = false;

            foreach (string sourcePart in ParseFlattenPath(path))
            {
                string part = sourcePart;

                if (RawSheetMarker.TryParseSelector(part, out int selector))
                {
                    if (anonymousDictionaryRequired)
                    {
                        resolvedPath = null;
                        return false;
                    }

                    for (int i = 0; i < selector; ++i)
                    {
                        if (!(cursor is PropertyNodeList list) || !list.IsVerticalList)
                        {
                            resolvedPath = null;
                            return false;
                        }

                        cursor = list.Child;
                    }

                    anonymousDictionaryRequired = cursor is PropertyNodeVerticalDictionary;
                    resolvedParts.Add(part);
                    continue;
                }

                if (part == SheetTokens.Dictionary.Selector.Anonymous)
                {
                    if (!anonymousDictionaryRequired ||
                        !(cursor is PropertyNodeVerticalDictionary))
                    {
                        resolvedPath = null;
                        return false;
                    }

                    anonymousDictionaryRequired = false;
                    resolvedParts.Add(part);
                    continue;
                }

                if (cursor == null)
                {
                    part = memberNameMapper(part);

                    if (!RawSheetHeader.IsValidName(part))
                    {
                        resolvedPath = null;
                        return false;
                    }

                    if (Root.HasSubpath(part))
                    {
                        cursor = Root;
                    }
                    else if (Arr != null && Arr.ColumnNode.HasSubpath(part))
                    {
                        cursor = Arr.Child;
                    }
                    else
                    {
                        resolvedPath = null;
                        return false;
                    }
                }
                else if (cursor is PropertyNodeObject ||
                         cursor is PropertyNodeVerticalDictionary)
                {
                    part = memberNameMapper(part);

                    if (!RawSheetHeader.IsValidName(part))
                    {
                        resolvedPath = null;
                        return false;
                    }
                }

                if (anonymousDictionaryRequired ||
                    !TryResolveNamedComponent(cursor, part, valueContext,
                        indexes, horizontalIndexes, out var child) ||
                    child == null)
                {
                    resolvedPath = null;
                    return false;
                }

                resolvedParts.Add(part);

                if (child is PropertyNodeList verticalList && verticalList.IsVerticalList)
                {
                    cursor = verticalList.Child;
                    anonymousDictionaryRequired = cursor is PropertyNodeVerticalDictionary;
                }
                else
                {
                    cursor = child;
                    anonymousDictionaryRequired = false;
                }
            }

            resolvedPath = string.Join(SheetTokens.Separator.Path, resolvedParts);
            return resolvedParts.Count > 0;
        }

        internal bool TryConvertValue(PropertyColumnBinding binding, string value,
            ISheetFormatter formatter, out object converted)
        {
            var converter = binding.Node.ValueConverter;

            if (converter == null)
            {
                _context.Logger.LogError("No converter registered for type {NodeType}", binding.Node.ValueType);
                converted = null;
                return false;
            }

            var resolver = _context.Container.ContractResolver;
            var valueContext = new SheetValueConvertingContext(formatter, resolver);
            converted = converter.StringToValue(binding.Node.ValueType, value, valueContext);
            return true;
        }

        /// <summary>
        /// Updating possible index and keys to match the rows in target sheet
        /// </summary>
        /// <param name="sheet">Target sheet</param>
        public void UpdateIndex(ISheet sheet)
        {
            foreach (var row in sheet)
            {
                Root.UpdateIndex(row);

                if (row is ISheetRowArray rowArray)
                    Arr.UpdateIndex(rowArray.Arr);
            }
        }

        /// <summary>
        /// Traverse each leaf node
        /// Calling UpdateCount first is required to get correct result
        /// index list are returned just to feed back, only valid on enumeration loop
        /// </summary>
        public IEnumerable<(PropertyNode, IReadOnlyList<object>)> TraverseLeaf()
        {
            _indexes.Clear();

            foreach (var node in Root.TraverseChildren(_indexes))
                yield return (node, _indexes);

            if (Arr != null)
            {
                foreach (var node in Arr.TraverseChildren(_indexes))
                    yield return (node, _indexes);
            }
        }

        internal VerticalCollectionLayout CreateLayout(IReadOnlyList<PropertyColumnBinding> bindings)
        {
            return new VerticalCollectionLayout(bindings);
        }

        internal IReadOnlyList<PropertyColumnBinding> GetCurrentBindings()
        {
            return GetCurrentBindings(null);
        }

        internal IReadOnlyList<PropertyColumnBinding> GetCurrentBindings(
            Func<string, string> memberNameMapper)
        {
            var bindings = new List<PropertyColumnBinding>();

            foreach (var pair in TraverseLeaf())
            {
                var indexes = new List<object>(pair.Item2);
                string path = FormatPath(pair.Item1.FullPath, indexes);
                bindings.Add(CreateBinding(
                    pair.Item1, indexes, path, null, memberNameMapper));
            }

            return bindings;
        }

        internal IEnumerable<PropertyMapValue> TraverseValues(ISheetRow row)
        {
            foreach (var binding in GetCurrentBindings())
            {
                if (binding.IsAnyDictionaryKey())
                    continue;

                foreach (var value in TraverseValues(row, binding, 0, new PropertyValueAddress()))
                    yield return value;
            }
        }

        internal IEnumerable<PropertyMapExportRow> TraverseExportRows(
            ISheetRow row, VerticalCollectionLayout layout)
        {
            return VerticalCollectionExporter.Traverse(row, layout);
        }

        private IEnumerable<PropertyMapValue> TraverseValues(ISheetRow row, PropertyColumnBinding binding,
            int ownerIndex, PropertyValueAddress address)
        {
            if (ownerIndex >= binding.VerticalOwners.Count)
            {
                yield return new PropertyMapValue(row, binding, address.Clone());
                yield break;
            }

            var owner = binding.VerticalOwners[ownerIndex];

            if (!PropertyValueAccessor.TryGetNodeValue(row, binding, owner, address, out var collection))
            {
                foreach (var value in TraverseValues(row, binding, ownerIndex + 1, address))
                    yield return value;

                yield break;
            }

            if (owner is PropertyNodeList && collection is System.Collections.IList list)
            {
                if (list.Count == 0)
                {
                    foreach (var value in TraverseValues(row, binding, ownerIndex + 1, address))
                        yield return value;

                    yield break;
                }

                for (int i = 0; i < list.Count; ++i)
                {
                    address.SetListIndex(list, i);

                    foreach (var value in TraverseValues(row, binding, ownerIndex + 1, address))
                        yield return value;
                }

                address.RemoveList(list);
                yield break;
            }

            if (owner is PropertyNodeVerticalDictionary && collection is System.Collections.IDictionary dictionary)
            {
                if (dictionary.Count == 0)
                {
                    foreach (var value in TraverseValues(row, binding, ownerIndex + 1, address))
                        yield return value;

                    yield break;
                }

                foreach (System.Collections.DictionaryEntry entry in dictionary)
                {
                    address.SetDictionaryKey(dictionary, entry.Key);

                    foreach (var value in TraverseValues(row, binding, ownerIndex + 1, address))
                        yield return value;
                }

                address.RemoveDictionary(dictionary);
            }
        }

        private PropertyColumnBinding CreateBinding(PropertyNode node, IReadOnlyList<object> indexes,
            string path, IReadOnlyDictionary<PropertyNode, object> knownHorizontalIndexes,
            Func<string, string> memberNameMapper = null)
        {
            var nodes = new List<PropertyNode>();

            for (var current = node; current != null; current = current.Parent)
                nodes.Add(current);

            nodes.Reverse();

            var verticalOwners = new List<PropertyNode>();
            var horizontalIndexes = new Dictionary<PropertyNode, object>();

            if (knownHorizontalIndexes != null)
            {
                foreach (var pair in knownHorizontalIndexes)
                    horizontalIndexes.Add(pair.Key, pair.Value);
            }

            int indexPosition = 0;

            foreach (var current in nodes)
            {
                if (current is PropertyNodeVerticalDictionary ||
                    current is PropertyNodeList verticalList && verticalList.IsVerticalList)
                {
                    verticalOwners.Add(current);
                    continue;
                }

                if (knownHorizontalIndexes == null && current.IndexType != null && indexPosition < indexes.Count)
                    horizontalIndexes[current] = indexes[indexPosition++];
            }

            var headerComponents = CreateHeaderComponents(nodes, path, memberNameMapper);

            return new PropertyColumnBinding(node, new List<object>(indexes), path,
                nodes, horizontalIndexes, verticalOwners, headerComponents);
        }

        private static bool TryResolveNamedComponent(PropertyNode node, string text,
            SheetValueConvertingContext valueContext, List<object> indexes,
            Dictionary<PropertyNode, object> horizontalIndexes, out PropertyNode child)
        {
            child = null;

            if (node is PropertyNodeObject objectNode)
            {
                if (!objectNode.HasSubpath(text))
                    return false;

                child = objectNode.GetChild(text);
                return true;
            }

            if (node is PropertyNodeVerticalDictionary verticalDictionary)
            {
                child = verticalDictionary.GetChild(text);
                return child != null;
            }

            if (node is PropertyNodeList list)
            {
                if (list.IsVerticalList)
                    return false;

                return TryResolveHorizontalComponent(
                    list, text, valueContext, indexes, horizontalIndexes, out child);
            }

            if (node is PropertyNodeDictionary dictionary)
            {
                return TryResolveHorizontalComponent(
                    dictionary, text, valueContext, indexes, horizontalIndexes, out child);
            }

            return false;
        }

        private static bool TryResolveHorizontalComponent(PropertyNode node, string text,
            SheetValueConvertingContext valueContext, List<object> indexes,
            Dictionary<PropertyNode, object> horizontalIndexes, out PropertyNode child)
        {
            try
            {
                object index = valueContext.StringToValue(node.IndexType, text);
                indexes.Add(index);
                horizontalIndexes[node] = index;
                child = node.GetChild(text);
                return child != null;
            }
            catch
            {
                child = null;
                return false;
            }
        }

        private static IReadOnlyList<RawSheetHeaderComponent> CreateHeaderComponents(
            IReadOnlyList<PropertyNode> nodes, string path,
            Func<string, string> memberNameMapper)
        {
            var pathParts = new List<string>();

            foreach (string part in ParseFlattenPath(path))
            {
                if (part == SheetTokens.Dictionary.Selector.Anonymous ||
                    part == SheetTokens.List.Selector.Anonymous ||
                    part.Length >= 3 &&
                    part[0] == SheetTokens.List.Selector.Start[0] &&
                    part[part.Length - 1] == SheetTokens.List.Selector.End[0])
                {
                    continue;
                }

                pathParts.Add(part);
            }
            var components = new List<RawSheetHeaderComponent>();
            int pathPosition = 0;

            for (int i = 1; i < nodes.Count; ++i)
            {
                var parent = nodes[i - 1];
                var current = nodes[i];

                if (parent is PropertyNodeList parentList && parentList.IsVerticalList)
                {
                    if (current is PropertyNodeList currentList && currentList.IsVerticalList)
                    {
                        components.Add(new RawSheetHeaderComponent(
                            RawSheetHeaderComponentKind.AnonymousList,
                            null, -1, -1, current));
                    }
                    else if (current is PropertyNodeVerticalDictionary)
                    {
                        components.Add(new RawSheetHeaderComponent(
                            RawSheetHeaderComponentKind.AnonymousDictionary,
                            null, -1, -1, current));
                    }

                    continue;
                }

                if (parent is PropertyNodeObject ||
                    parent is PropertyNodeVerticalDictionary ||
                    parent is PropertyNodeDictionary ||
                    parent is PropertyNodeList)
                {
                    string text = pathPosition < pathParts.Count
                        ? pathParts[pathPosition++]
                        : current.PropertyInfo?.Name;

                    if (memberNameMapper != null &&
                        (parent is PropertyNodeObject ||
                         parent is PropertyNodeVerticalDictionary))
                    {
                        text = memberNameMapper(text);

                        if (!RawSheetHeader.IsValidName(text))
                        {
                            throw new InvalidOperationException(
                                $"Mapped member name \"{text ?? "(null)"}\" is invalid.");
                        }
                    }

                    components.Add(new RawSheetHeaderComponent(
                        RawSheetHeaderComponentKind.Named,
                        text, -1, -1, current));
                }
            }

            return components;
        }

        internal void ReportInvalidColumn(string path)
        {
            _warned = _warned ?? new HashSet<string>();

            if (_warned.Add(path))
                _context.Logger.LogError("Column name is invalid");
        }

        private static string FormatPath(string path, IReadOnlyList<object> indexes)
        {
            var arguments = new object[indexes.Count];

            for (int i = 0; i < indexes.Count; ++i)
                arguments[i] = indexes[i];

            return string.Format(path, arguments);
        }
    }
}
