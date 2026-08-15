// BakingSheet, Maxwell Keonwoo Kang <code.athei@gmail.com>, 2022

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;

namespace Cathei.BakingSheet.Internal
{
    public class PropertyNodeObject : PropertyNode
    {
        private Dictionary<string, PropertyNode> _children;

        public override Type IndexType => null;

        protected override bool IsLeaf => _children == null;

        public PropertyNodeObject(PropertyNode parent, string fullPath, Type valueType,
            GetterDelegate getter, SetterDelegate setter, PropertyInfo propertyInfo,
            ISheetContractResolver resolver, int depth)
            : base(parent, fullPath, valueType, getter, setter, propertyInfo)
        {
            GenerateChildren(resolver, depth);
        }

        public override PropertyNode GetChild(string subpath) => _children?[subpath];

        public bool RemoveChild(string subpath) => _children?.Remove(subpath) ?? false;

        public override bool HasSubpath(string subpath) => _children?.ContainsKey(subpath) ?? false;

        private string AppendPath(string subpath)
        {
            if (FullPath == null)
                return subpath;

            return $"{FullPath}{SheetTokens.Separator.Path}{subpath}";
        }

        public override void UpdateIndex(object obj)
        {
            if (IsLeaf)
                return;

            foreach (var child in _children.Values)
            {
                child.Getter(child, obj, null, out var elem);
                if (elem != null)
                    child.UpdateIndex(elem);
            }
        }

        internal override void CollectUnsupportedProperties(List<PropertyNodeIgnored> nodes)
        {
            if (_children == null)
                return;

            foreach (var child in _children.Values)
                child.CollectUnsupportedProperties(nodes);
        }

        public override int CalculateDepth()
        {
            if (IsLeaf)
                return 0;

            int depth = 0;

            foreach (var child in _children.Values)
                depth = Math.Max(depth, child.CalculateDepth());

            return depth;
        }

        public override IEnumerable<PropertyNode> TraverseChildren(List<object> indexes)
        {
            if (IsLeaf)
            {
                yield return this;
                yield break;
            }

            // Id column should come first
            if (_children.TryGetValue(SheetTokens.Header.Id, out var idChild))
            {
                foreach (var node in idChild.TraverseChildren(indexes))
                    yield return node;
            }

            foreach (var child in _children.Values)
            {
                if (child == idChild)
                    continue;

                foreach (var node in child.TraverseChildren(indexes))
                    yield return node;
            }
        }

        internal static bool ValueGetter(PropertyNode child, object obj, object key, out object value)
        {
            Debug.Assert(child.PropertyInfo != null);
            value = child.PropertyInfo.GetValue(obj);
            return true;
        }

        private static void ValueSetter(PropertyNode child, object obj, object key, object value)
        {
            Debug.Assert(child.PropertyInfo != null);
            child.PropertyInfo.SetValue(obj, value);
        }

        private void GenerateChildren(ISheetContractResolver resolver, int depth)
        {
            ValueConverter = resolver.GetValueConverter(PropertyInfo) ??
                             resolver.GetValueConverter(ValueType);

            // leaf node (convertable)
            if (ValueConverter != null)
                return;

            _children = new Dictionary<string, PropertyNode>();

            bool isRoot = Parent == null;

            foreach (PropertyInfo propertyInfo in SheetTokens.GetEligibleProperties(ValueType))
            {
                // "Arr" is reserved for SheetRowArray
                if (isRoot && propertyInfo.Name == nameof(ISheetRowArray.Arr))
                    continue;

                var childPath = AppendPath(propertyInfo.Name);
                var child = PropertyNodeFactory.CreateProperty(this, childPath, propertyInfo,
                    ValueGetter, ValueSetter, resolver, depth);

                _children.Add(propertyInfo.Name, child);
            }
        }
    }
}
