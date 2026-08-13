// BakingSheet, Maxwell Keonwoo Kang <code.athei@gmail.com>, 2022

using System;
using System.Collections;
using System.Reflection;

namespace Cathei.BakingSheet.Internal
{
    internal static class PropertyNodeFactory
    {
        public static PropertyNode Create(
            PropertyNode parent, string fullPath, Type type,
            PropertyNode.GetterDelegate getter, PropertyNode.SetterDelegate setter, PropertyInfo propertyInfo,
            ISheetContractResolver resolver, int depth)
        {
            if (typeof(IVerticalDictionary).IsAssignableFrom(type))
            {
                return new PropertyNodeVerticalDictionary(parent, fullPath, type,
                    getter, setter, propertyInfo, resolver, depth);
            }

            if (typeof(IVerticalList).IsAssignableFrom(type))
            {
                return new PropertyNodeList(parent, fullPath, type,
                    getter, setter, propertyInfo, resolver, depth, true);
            }

            if (typeof(IList).IsAssignableFrom(type))
            {
                return new PropertyNodeList(parent, fullPath, type,
                    getter, setter, propertyInfo, resolver, depth, false);
            }

            if (typeof(IDictionary).IsAssignableFrom(type))
            {
                return new PropertyNodeDictionary(parent, fullPath, type,
                    getter, setter, propertyInfo, resolver, depth);
            }

            return new PropertyNodeObject(parent, fullPath, type,
                getter, setter, propertyInfo, resolver, depth);
        }

        public static PropertyNode CreateProperty(PropertyNode parent, string fullPath, PropertyInfo propertyInfo,
            PropertyNode.GetterDelegate getter, PropertyNode.SetterDelegate setter,
            ISheetContractResolver resolver, int depth)
        {
            if (resolver.GetValueConverter(propertyInfo) != null ||
                VerticalDictionaryHelper.IsSupported(propertyInfo.PropertyType, resolver))
            {
                return Create(parent, fullPath, propertyInfo.PropertyType,
                    getter, setter, propertyInfo, resolver, depth);
            }

            return new PropertyNodeIgnored(parent, fullPath, propertyInfo.PropertyType,
                getter, setter, propertyInfo);
        }
    }
}
