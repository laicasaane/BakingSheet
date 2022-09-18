﻿// BakingSheet, Maxwell Keonwoo Kang <code.athei@gmail.com>, 2022

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Cathei.BakingSheet.Internal;

namespace Cathei.BakingSheet
{
    public class SheetContractResolver : ISheetContractResolver
    {
        public static readonly SheetContractResolver Instance = new SheetContractResolver();

        private List<ISheetValueConverter> _converters = new List<ISheetValueConverter>();
        private Dictionary<Type, ISheetValueConverter> _attrConverters = new Dictionary<Type, ISheetValueConverter>();

        private Dictionary<Type, ISheetValueConverter> _typeToConverter = new Dictionary<Type, ISheetValueConverter>();

        public SheetContractResolver() : this(null) { }

        protected SheetContractResolver(IEnumerable<ISheetValueConverter> converters)
        {
            if (converters != null)
                _converters.AddRange(converters);

            // add default converters
            _converters.Add(new PrimitiveValueConverter());
            _converters.Add(new DateTimeValueConverter());
            _converters.Add(new TimeSpanValueConverter());
            _converters.Add(new SheetReferenceValueConverter());
            _converters.Add(new NullableValueConverter());
        }

        public virtual ISheetValueConverter GetValueConverter(Type type)
        {
            if (_typeToConverter.TryGetValue(type, out var cached))
                return cached;

            // type-level converter
            if (type.IsDefined(typeof(SheetValueConverterAttribute)))
            {
                var attr = type.GetCustomAttribute<SheetValueConverterAttribute>();
                var converter = GetConverterFromAttribute(attr);

                _typeToConverter.Add(type, converter);
                return converter;
            }

            // sheet-level converter
            foreach (var converter in _converters)
            {
                if (!converter.CanConvert(type))
                    continue;

                _typeToConverter.Add(type, converter);
                return converter;
            }

            // there is no ValueConverter provided for this type
            _typeToConverter.Add(type, null);
            return null;
        }

        public virtual ISheetValueConverter GetValueConverter(PropertyInfo propertyInfo)
        {
            if (!propertyInfo.IsDefined(typeof(SheetValueConverterAttribute)))
                return null;

            // property-level converter
            var attr = propertyInfo.GetCustomAttribute<SheetValueConverterAttribute>();
            var converter = GetConverterFromAttribute(attr);
            return converter;
        }

        private ISheetValueConverter GetConverterFromAttribute(SheetValueConverterAttribute attr)
        {
            if (_attrConverters.TryGetValue(attr.ConverterType, out var converter))
                return converter;

            converter = (ISheetValueConverter)Activator.CreateInstance(attr.ConverterType);
            _attrConverters.Add(attr.ConverterType, converter);
            return converter;
        }
    }
}
