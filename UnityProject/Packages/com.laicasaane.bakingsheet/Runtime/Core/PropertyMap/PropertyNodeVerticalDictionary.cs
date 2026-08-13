// BakingSheet, Maxwell Keonwoo Kang <code.athei@gmail.com>, 2022

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace Cathei.BakingSheet.Internal
{
    internal sealed class PropertyNodeVerticalDictionary : PropertyNode
    {
        public override Type IndexType => null;
        public override bool IsVertical => true;

        internal Type KeyType { get; }
        internal Type ElementType { get; }
        internal PropertyNode Key { get; }
        internal PropertyNode Value { get; }

        public PropertyNodeVerticalDictionary(PropertyNode parent, string fullPath, Type valueType,
            GetterDelegate getter, SetterDelegate setter, PropertyInfo propertyInfo,
            ISheetContractResolver resolver, int depth)
            : base(parent, fullPath, valueType, getter, setter, propertyInfo)
        {
            var arguments = PropertyMap.GetGenericArguments(valueType, typeof(IDictionary<,>));
            KeyType = arguments[0];
            ElementType = arguments[1];

            Key = PropertyNodeFactory.Create(this, AppendPath("Key"), KeyType,
                null, null, propertyInfo, resolver, depth);
            Value = PropertyNodeFactory.Create(this, AppendPath("Value"), ElementType,
                null, null, propertyInfo, resolver, depth);
        }

        public override PropertyNode GetChild(string subpath)
        {
            if (subpath == "Key")
                return Key;

            if (subpath == "Value")
                return Value;

            return null;
        }

        public override bool HasSubpath(string subpath)
        {
            return subpath == "Key" || subpath == "Value";
        }

        public override void UpdateIndex(object obj)
        {
            if (!(obj is IDictionary dictionary))
                return;

            foreach (DictionaryEntry entry in dictionary)
            {
                Key.UpdateIndex(entry.Key);
                Value.UpdateIndex(entry.Value);
            }
        }

        public override int CalculateDepth()
        {
            return Math.Max(Key.CalculateDepth(), Value.CalculateDepth());
        }

        public override IEnumerable<PropertyNode> TraverseChildren(List<object> indexes)
        {
            foreach (var node in Key.TraverseChildren(indexes))
                yield return node;

            foreach (var node in Value.TraverseChildren(indexes))
                yield return node;
        }

        internal bool IsKeyChild(PropertyNode child)
        {
            while (child != null && child.Parent != this)
                child = child.Parent;

            return ReferenceEquals(child, Key);
        }

        internal override void CollectUnsupportedProperties(List<PropertyNodeIgnored> nodes)
        {
            Key.CollectUnsupportedProperties(nodes);
            Value.CollectUnsupportedProperties(nodes);
        }

        private string AppendPath(string subpath)
        {
            return $"{FullPath}{Config.IndexDelimiter}{subpath}";
        }
    }
}
