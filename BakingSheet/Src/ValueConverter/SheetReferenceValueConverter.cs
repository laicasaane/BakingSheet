﻿// BakingSheet, Maxwell Keonwoo Kang <code.athei@gmail.com>, 2022

using System;
using System.Threading.Tasks;

namespace Cathei.BakingSheet.Internal
{
    public class SheetReferenceValueConverter : ISheetValueConverter
    {
        public bool CanConvert(Type type)
        {
            return typeof(ISheetReference).IsAssignableFrom(type);
        }

        public object StringToValue(Type type, string value, ISheetFormatter format)
        {
            var reference = (ISheetReference)Activator.CreateInstance(type);
            reference.Id = format.StringToValue(reference.IdType, value);
            return reference;
        }

        public string ValueToString(Type type, object value, ISheetFormatter format)
        {
            var reference = (ISheetReference)value;
            return format.ValueToString(reference.IdType, reference.Id);
        }
    }
}
