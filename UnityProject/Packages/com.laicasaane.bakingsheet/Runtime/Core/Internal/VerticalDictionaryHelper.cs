// BakingSheet, Maxwell Keonwoo Kang <code.athei@gmail.com>, 2022

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace Cathei.BakingSheet.Internal
{
    internal static class VerticalDictionaryHelper
    {
        public static bool IsSupported(Type type, ISheetContractResolver resolver)
        {
            if (!ContainsVerticalCollection(type, resolver, new HashSet<Type>()))
                return true;

            return IsSupportedValue(type, resolver, new HashSet<Type>()) &&
                   HasExportableLeaf(type, resolver, new HashSet<Type>());
        }

        private static bool IsSupportedValue(Type type, ISheetContractResolver resolver, HashSet<Type> visiting)
        {
            if (type.IsArray)
                return false;

            if (typeof(IVerticalDictionary).IsAssignableFrom(type))
            {
                var arguments = PropertyMap.GetGenericArguments(type, typeof(IDictionary<,>));
                return IsNonVertical(arguments[0], resolver, false, new HashSet<Type>()) &&
                       IsSupportedValue(arguments[1], resolver, visiting);
            }

            if (typeof(IVerticalList).IsAssignableFrom(type))
            {
                var elementType = PropertyMap.GetGenericArguments(type, typeof(IList<>))[0];
                return IsSupportedValue(elementType, resolver, visiting);
            }

            if (resolver.GetValueConverter(type) != null)
                return true;

            if (!visiting.Add(type))
                return true;

            try
            {
                if (typeof(IDictionary).IsAssignableFrom(type))
                {
                    var arguments = PropertyMap.GetGenericArguments(type, typeof(IDictionary<,>));
                    return IsNonVertical(arguments[0], resolver, true, new HashSet<Type>()) &&
                           IsSupportedValue(arguments[1], resolver, visiting);
                }

                if (typeof(IList).IsAssignableFrom(type))
                {
                    var elementType = PropertyMap.GetGenericArguments(type, typeof(IList<>))[0];
                    return IsSupportedValue(elementType, resolver, visiting);
                }

                foreach (PropertyInfo property in SheetTokens.GetEligibleProperties(type))
                {
                    if (resolver.GetValueConverter(property) != null)
                        continue;

                    if (!IsSupportedValue(property.PropertyType, resolver, visiting))
                        return false;
                }

                return true;
            }
            finally
            {
                visiting.Remove(type);
            }
        }

        private static bool IsNonVertical(Type type, ISheetContractResolver resolver, bool allowReference,
            HashSet<Type> visiting)
        {
            if (type.IsArray ||
                typeof(IVerticalDictionary).IsAssignableFrom(type) ||
                typeof(IVerticalList).IsAssignableFrom(type))
            {
                return false;
            }

            if (!allowReference && typeof(ISheetReference).IsAssignableFrom(type))
                return false;

            if (resolver.GetValueConverter(type) != null || !visiting.Add(type))
                return true;

            try
            {
                if (typeof(IDictionary).IsAssignableFrom(type))
                {
                    var arguments = PropertyMap.GetGenericArguments(type, typeof(IDictionary<,>));
                    return IsNonVertical(arguments[0], resolver, allowReference, visiting) &&
                           IsNonVertical(arguments[1], resolver, allowReference, visiting);
                }

                if (typeof(IList).IsAssignableFrom(type))
                {
                    var elementType = PropertyMap.GetGenericArguments(type, typeof(IList<>))[0];
                    return IsNonVertical(elementType, resolver, allowReference, visiting);
                }

                foreach (PropertyInfo property in SheetTokens.GetEligibleProperties(type))
                {
                    if (resolver.GetValueConverter(property) != null)
                        continue;

                    if (!IsNonVertical(property.PropertyType, resolver, allowReference, visiting))
                        return false;
                }

                return true;
            }
            finally
            {
                visiting.Remove(type);
            }
        }

        private static bool ContainsVerticalCollection(Type type, ISheetContractResolver resolver,
            HashSet<Type> visiting)
        {
            if (typeof(IVerticalDictionary).IsAssignableFrom(type) ||
                typeof(IVerticalList).IsAssignableFrom(type))
            {
                return true;
            }

            if (type.IsArray)
                return ContainsVerticalCollection(type.GetElementType(), resolver, visiting);

            if (resolver.GetValueConverter(type) != null || !visiting.Add(type))
                return false;

            try
            {
                if (typeof(IDictionary).IsAssignableFrom(type))
                {
                    var arguments = PropertyMap.GetGenericArguments(type, typeof(IDictionary<,>));
                    return ContainsVerticalCollection(arguments[0], resolver, visiting) ||
                           ContainsVerticalCollection(arguments[1], resolver, visiting);
                }

                if (typeof(IList).IsAssignableFrom(type))
                {
                    var elementType = PropertyMap.GetGenericArguments(type, typeof(IList<>))[0];
                    return ContainsVerticalCollection(elementType, resolver, visiting);
                }

                foreach (PropertyInfo property in SheetTokens.GetEligibleProperties(type))
                {
                    if (resolver.GetValueConverter(property) == null &&
                        ContainsVerticalCollection(property.PropertyType, resolver, visiting))
                    {
                        return true;
                    }
                }

                return false;
            }
            finally
            {
                visiting.Remove(type);
            }
        }

        private static bool HasExportableLeaf(Type type, ISheetContractResolver resolver, HashSet<Type> visiting)
        {
            if (type.IsArray)
                return false;

            if (typeof(IVerticalDictionary).IsAssignableFrom(type))
            {
                var arguments = PropertyMap.GetGenericArguments(type, typeof(IDictionary<,>));
                return HasExportableLeaf(arguments[0], resolver, visiting) ||
                       HasExportableLeaf(arguments[1], resolver, visiting);
            }

            if (typeof(IVerticalList).IsAssignableFrom(type))
            {
                var elementType = PropertyMap.GetGenericArguments(type, typeof(IList<>))[0];
                return HasExportableLeaf(elementType, resolver, visiting);
            }

            if (resolver.GetValueConverter(type) != null)
                return true;

            if (!visiting.Add(type))
                return false;

            try
            {
                if (typeof(IDictionary).IsAssignableFrom(type))
                {
                    var arguments = PropertyMap.GetGenericArguments(type, typeof(IDictionary<,>));
                    return HasExportableLeaf(arguments[1], resolver, visiting);
                }

                if (typeof(IList).IsAssignableFrom(type))
                {
                    var elementType = PropertyMap.GetGenericArguments(type, typeof(IList<>))[0];
                    return HasExportableLeaf(elementType, resolver, visiting);
                }

                foreach (PropertyInfo property in SheetTokens.GetEligibleProperties(type))
                {
                    if (resolver.GetValueConverter(property) != null ||
                        HasExportableLeaf(property.PropertyType, resolver, visiting))
                    {
                        return true;
                    }
                }

                return false;
            }
            finally
            {
                visiting.Remove(type);
            }
        }
    }
}
