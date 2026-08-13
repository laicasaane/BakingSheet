// BakingSheet, Maxwell Keonwoo Kang <code.athei@gmail.com>, 2022

using System;
using System.Collections.Generic;
using System.Reflection;

namespace Cathei.BakingSheet.Internal
{
    internal sealed class PropertyNodeIgnored : PropertyNode
    {
        public override Type IndexType => null;
        public override bool IsIgnored => true;

        public PropertyNodeIgnored(PropertyNode parent, string fullPath, Type valueType,
            GetterDelegate getter, SetterDelegate setter, PropertyInfo propertyInfo)
            : base(parent, fullPath, valueType, getter, setter, propertyInfo) { }

        public override PropertyNode GetChild(string subpath) => this;
        public override bool HasSubpath(string subpath) => true;
        public override void UpdateIndex(object obj) { }
        public override int CalculateDepth() => 0;

        public override IEnumerable<PropertyNode> TraverseChildren(List<object> indexes)
        {
            yield break;
        }

        internal override void CollectUnsupportedProperties(List<PropertyNodeIgnored> nodes)
        {
            nodes.Add(this);
        }
    }
}
