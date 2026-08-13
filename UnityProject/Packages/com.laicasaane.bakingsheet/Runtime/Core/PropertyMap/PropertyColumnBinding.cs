// BakingSheet, Maxwell Keonwoo Kang <code.athei@gmail.com>, 2022

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Cathei.BakingSheet.Raw;

namespace Cathei.BakingSheet.Internal
{
    internal readonly struct PropertyColumnBinding
    {
        private readonly IReadOnlyDictionary<PropertyNode, object> _horizontalIndexes;

        public PropertyNode Node { get; }
        public IReadOnlyList<object> Indexes { get; }
        public string Path { get; }
        internal IReadOnlyList<RawSheetHeaderComponent> HeaderComponents { get; }
        internal string SemanticPath { get; }

        internal IReadOnlyList<PropertyNode> Nodes { get; }
        internal IReadOnlyList<PropertyNode> VerticalOwners { get; }

        internal PropertyColumnBinding(PropertyNode node, IReadOnlyList<object> indexes, string path,
            IReadOnlyList<PropertyNode> nodes, IReadOnlyDictionary<PropertyNode, object> horizontalIndexes,
            IReadOnlyList<PropertyNode> verticalOwners,
            IReadOnlyList<RawSheetHeaderComponent> headerComponents)
        {
            Node = node;
            Indexes = indexes;
            Path = path;
            Nodes = nodes;
            VerticalOwners = verticalOwners;
            _horizontalIndexes = horizontalIndexes;
            HeaderComponents = headerComponents;
            SemanticPath = RawSheetHeader.FormatFlat(headerComponents);
        }

        internal bool TryGetHorizontalIndex(PropertyNode node, out object index)
        {
            return _horizontalIndexes.TryGetValue(node, out index);
        }

        internal bool ContainsOwner(PropertyNode owner)
        {
            foreach (var candidate in VerticalOwners)
            {
                if (ReferenceEquals(candidate, owner))
                    return true;
            }

            return false;
        }

        internal bool ContainsNode(PropertyNode node)
        {
            foreach (var candidate in Nodes)
            {
                if (ReferenceEquals(candidate, node))
                    return true;
            }

            return false;
        }

        internal bool TryGetAnonymousTargetPath(PropertyNodeList owner, out string markerPath)
        {
            var boundary = owner.Child;

            for (int i = 0; i < HeaderComponents.Count; ++i)
            {
                if (!ReferenceEquals(HeaderComponents[i].Node, boundary))
                    continue;

                markerPath = RawSheetHeader.FormatMarker(HeaderComponents, i + 1);
                return true;
            }

            markerPath = null;
            return false;
        }

        internal bool IsDictionaryKey(PropertyNodeVerticalDictionary dictionary)
        {
            var child = Node;

            while (child != null && child.Parent != dictionary)
                child = child.Parent;

            return child != null && dictionary.IsKeyChild(child);
        }

        internal bool IsAnyDictionaryKey()
        {
            foreach (var owner in VerticalOwners)
            {
                if (owner is PropertyNodeVerticalDictionary dictionary && IsDictionaryKey(dictionary))
                    return true;
            }

            return false;
        }

        internal string GetOwnerPath(PropertyNode owner)
        {
            string result = null;

            for (int i = 0; i < Nodes.Count; ++i)
            {
                var node = Nodes[i];

                if (i > 0 && Nodes[i - 1] is PropertyNodeList list && list.IsVerticalList)
                    result = AppendSegment(result, "[]");

                string current = FormatPath(node.FullPath);
                string parent = i == 0 ? null : FormatPath(Nodes[i - 1].FullPath);

                if (!string.IsNullOrEmpty(current) && current != parent)
                {
                    string suffix = current;

                    if (!string.IsNullOrEmpty(parent) &&
                        current.StartsWith(parent + Config.IndexDelimiter, StringComparison.Ordinal))
                    {
                        suffix = current.Substring(parent.Length + 1);
                    }

                    if (string.IsNullOrEmpty(result))
                        result = suffix;
                    else if (!current.StartsWith(result, StringComparison.Ordinal))
                        result = AppendPath(result, suffix);
                    else
                        result = current;
                }

                if (ReferenceEquals(node, owner))
                    return result;
            }

            return null;
        }

        private string FormatPath(string path)
        {
            if (path == null)
                return null;

            var arguments = new object[Indexes.Count];

            for (int i = 0; i < arguments.Length; ++i)
                arguments[i] = Indexes[i];

            return string.Format(path, arguments);
        }

        private static string AppendSegment(string path, string segment)
        {
            return string.IsNullOrEmpty(path) ? segment : AppendPath(path, segment);
        }

        private static string AppendPath(string path, string suffix)
        {
            return $"{path}{Config.IndexDelimiter}{suffix}";
        }
    }

    internal sealed class PropertyValueAddress
    {
        private readonly Dictionary<object, int> _listIndexes;
        private readonly Dictionary<object, object> _dictionaryKeys;

        public PropertyValueAddress()
        {
            var comparer = ReferenceEqualityComparer.Instance;
            _listIndexes = new Dictionary<object, int>(comparer);
            _dictionaryKeys = new Dictionary<object, object>(comparer);
        }

        private PropertyValueAddress(PropertyValueAddress source)
            : this()
        {
            foreach (var pair in source._listIndexes)
                _listIndexes.Add(pair.Key, pair.Value);

            foreach (var pair in source._dictionaryKeys)
                _dictionaryKeys.Add(pair.Key, pair.Value);
        }

        public PropertyValueAddress Clone()
        {
            return new PropertyValueAddress(this);
        }

        public void SetListIndex(IList list, int index)
        {
            _listIndexes[list] = index;
        }

        public bool TryGetListIndex(IList list, out int index)
        {
            return _listIndexes.TryGetValue(list, out index);
        }

        public void RemoveList(IList list)
        {
            _listIndexes.Remove(list);
        }

        public void SetDictionaryKey(IDictionary dictionary, object key)
        {
            _dictionaryKeys[dictionary] = key;
        }

        public bool TryGetDictionaryKey(IDictionary dictionary, out object key)
        {
            return _dictionaryKeys.TryGetValue(dictionary, out key);
        }

        public void RemoveDictionary(IDictionary dictionary)
        {
            _dictionaryKeys.Remove(dictionary);
        }
    }

    internal sealed class PropertyMapValue
    {
        private readonly ISheetRow _row;
        private readonly PropertyColumnBinding _binding;
        private readonly PropertyValueAddress _address;

        public PropertyInfo PropertyInfo => _binding.Node.PropertyInfo;
        public Type ValueType => _binding.Node.ValueType;
        public string Path => _binding.Path;
        public bool IsPresent { get; }
        public object Value { get; }

        internal PropertyMapValue(ISheetRow row, PropertyColumnBinding binding, PropertyValueAddress address)
        {
            _row = row;
            _binding = binding;
            _address = address;
            IsPresent = PropertyValueAccessor.TryGetValue(row, binding, address, out var value);
            Value = value;
        }

        public void SetValue(object value)
        {
            PropertyValueAccessor.SetValue(_row, _binding, _address, value);
        }
    }

    internal static class PropertyValueAccessor
    {
        public static bool TryGetValue(ISheetRow row, PropertyColumnBinding binding,
            PropertyValueAddress address, out object value)
        {
            return TryGetNodeValue(row, binding, binding.Node, address, out value);
        }

        public static bool TryGetNodeValue(ISheetRow row, PropertyColumnBinding binding, PropertyNode target,
            PropertyValueAddress address, out object value)
        {
            value = null;
            var nodes = binding.Nodes;
            int targetIndex = IndexOf(nodes, target);

            if (targetIndex < 0 || !TryGetRoot(row, nodes[0], out value))
                return false;

            for (int i = 1; i <= targetIndex; ++i)
            {
                if (!TryReadChild(nodes[i - 1], nodes[i], value, binding, address, out value))
                    return false;
            }

            return true;
        }

        public static bool EnsureNodeValue(ISheetRow row, PropertyColumnBinding binding, PropertyNode target,
            PropertyValueAddress address, out object value)
        {
            object captured = null;
            bool result = ModifyValue(row, binding, target, address, original =>
            {
                captured = original ?? CreateValue(target);
                return captured;
            });
            value = captured;
            return result && value != null;
        }

        public static bool SetValue(ISheetRow row, PropertyColumnBinding binding,
            PropertyValueAddress address, object value)
        {
            return ModifyValue(row, binding, binding.Node, address, _ => value);
        }

        public static bool SetSubtreeValue(ref object root, PropertyNode subtree,
            PropertyColumnBinding binding, object value)
        {
            int rootIndex = IndexOf(binding.Nodes, subtree);
            int targetIndex = binding.Nodes.Count - 1;

            if (rootIndex < 0)
                return false;

            bool success = true;
            root = ModifyChild(root, rootIndex, targetIndex, binding, null, _ => value, ref success);
            return success;
        }

        public static object CreateValue(PropertyNode node)
        {
            if (node.ValueType.IsValueType)
                return Activator.CreateInstance(node.ValueType);

            if (node.IsLeafNode)
                return null;

            return Activator.CreateInstance(node.ValueType);
        }

        private static bool ModifyValue(ISheetRow row, PropertyColumnBinding binding, PropertyNode target,
            PropertyValueAddress address, Func<object, object> modifier)
        {
            var nodes = binding.Nodes;
            int targetIndex = IndexOf(nodes, target);

            if (targetIndex < 0 || !TryGetRoot(row, nodes[0], out var root))
                return false;

            bool success = true;
            object changed = ModifyChild(root, 0, targetIndex, binding, address, modifier, ref success);

            if (!success)
                return false;

            if (nodes[0].PropertyInfo != null && nodes[0].Setter != null)
                nodes[0].Setter(nodes[0], row, null, changed);

            return true;
        }

        private static object ModifyChild(object current, int currentIndex, int targetIndex,
            PropertyColumnBinding binding, PropertyValueAddress address,
            Func<object, object> modifier, ref bool success)
        {
            if (currentIndex == targetIndex)
                return modifier(current);

            var parent = binding.Nodes[currentIndex];
            var child = binding.Nodes[currentIndex + 1];

            if (!TryReadChild(parent, child, current, binding, address, out var childValue))
                childValue = null;

            if (childValue == null && currentIndex + 1 < targetIndex)
            {
                childValue = CreateValue(child);

                if (childValue == null)
                {
                    success = false;
                    return current;
                }
            }

            childValue = ModifyChild(childValue, currentIndex + 1, targetIndex,
                binding, address, modifier, ref success);

            if (!success || !TryWriteChild(parent, child, current, binding, address, childValue))
            {
                success = false;
                return current;
            }

            return current;
        }

        private static bool TryGetRoot(ISheetRow row, PropertyNode root, out object value)
        {
            if (root.PropertyInfo == null)
            {
                value = row;
                return true;
            }

            value = null;
            return root.Getter != null && root.Getter(root, row, null, out value);
        }

        private static bool TryReadChild(PropertyNode parent, PropertyNode child, object parentValue,
            PropertyColumnBinding binding, PropertyValueAddress address, out object value)
        {
            if (parentValue == null)
            {
                value = null;
                return false;
            }

            if (parent is PropertyNodeVerticalDictionary dictionaryNode)
            {
                if (!(parentValue is IDictionary dictionary) || address == null ||
                    !address.TryGetDictionaryKey(dictionary, out var key))
                {
                    value = null;
                    return false;
                }

                if (dictionaryNode.IsKeyChild(child))
                {
                    value = key;
                    return true;
                }

                if (!dictionary.Contains(key))
                {
                    value = null;
                    return false;
                }

                value = dictionary[key];
                return true;
            }

            object childIndex = GetChildIndex(parent, parentValue, binding, address, out bool valid);

            if (!valid || child.Getter == null)
            {
                value = null;
                return false;
            }

            return child.Getter(child, parentValue, childIndex, out value);
        }

        private static bool TryWriteChild(PropertyNode parent, PropertyNode child, object parentValue,
            PropertyColumnBinding binding, PropertyValueAddress address, object value)
        {
            if (parent is PropertyNodeVerticalDictionary dictionaryNode)
            {
                if (dictionaryNode.IsKeyChild(child) || !(parentValue is IDictionary dictionary) ||
                    address == null || !address.TryGetDictionaryKey(dictionary, out var key))
                {
                    return false;
                }

                dictionary[key] = value;
                return true;
            }

            object childIndex = GetChildIndex(parent, parentValue, binding, address, out bool valid);

            if (!valid || child.Setter == null)
                return false;

            child.Setter(child, parentValue, childIndex, value);
            return true;
        }

        private static object GetChildIndex(PropertyNode parent, object parentValue,
            PropertyColumnBinding binding, PropertyValueAddress address, out bool valid)
        {
            valid = true;

            if (parent is PropertyNodeList list)
            {
                if (!list.IsVerticalList)
                {
                    valid = binding.TryGetHorizontalIndex(parent, out var horizontalIndex);
                    return horizontalIndex;
                }

                if (!(parentValue is IList values) || address == null ||
                    !address.TryGetListIndex(values, out int listIndex))
                {
                    valid = false;
                    return null;
                }

                return listIndex + 1;
            }

            if (parent is PropertyNodeDictionary)
            {
                valid = binding.TryGetHorizontalIndex(parent, out var dictionaryIndex);
                return dictionaryIndex;
            }

            return null;
        }

        private static int IndexOf(IReadOnlyList<PropertyNode> nodes, PropertyNode node)
        {
            for (int i = 0; i < nodes.Count; ++i)
            {
                if (ReferenceEquals(nodes[i], node))
                    return i;
            }

            return -1;
        }
    }

    internal sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        public static ReferenceEqualityComparer Instance { get; } = new ReferenceEqualityComparer();

        private ReferenceEqualityComparer() { }

        public new bool Equals(object x, object y)
        {
            return ReferenceEquals(x, y);
        }

        public int GetHashCode(object obj)
        {
            return RuntimeHelpers.GetHashCode(obj);
        }
    }
}
