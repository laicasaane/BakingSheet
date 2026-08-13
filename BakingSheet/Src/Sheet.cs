// BakingSheet, Maxwell Keonwoo Kang <code.athei@gmail.com>, 2022

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Cathei.BakingSheet.Internal;
using Microsoft.Extensions.Logging;

namespace Cathei.BakingSheet
{
    /// <summary>
    /// Represents a single page of Sheet.
    /// </summary>
    /// <typeparam name="TKey">Type of Id column.</typeparam>
    /// <typeparam name="TValue">Type of Row.</typeparam>
    public abstract partial class Sheet<TKey, TValue> : KeyedCollection<TKey, TValue>, ISheet<TKey, TValue>
        where TValue : SheetRow<TKey>, new()
    {
        [Preserve] public string Name { get; set; }
        [Preserve] public string HashCode { get; set; }

        private PropertyMap _propertyMap;

        public Type RowType => typeof(TValue);

        public new TValue this[TKey id]
        {
            get
            {
                if (id == null || !Contains(id))
                    return default(TValue);
                return base[id];
            }
        }

        public ICollection<TKey> Keys => Dictionary.Keys;
        public TValue Find(TKey id) => this[id];

        bool ISheet.Contains(object key) => Contains((TKey)key);
        void ISheet.Add(object value) => Add((TValue)value);

        public new Enumerator GetEnumerator() => new Enumerator(this);
        IEnumerator<ISheetRow> ISheet.GetEnumerator() => GetEnumerator();

        protected override TKey GetKeyForItem(TValue item)
        {
            return item.Id;
        }

        private PropertyMap GetPropertyMap(SheetConvertingContext context)
        {
            if (_propertyMap != null)
                return _propertyMap;

            _propertyMap = new PropertyMap(context, GetType());
            return _propertyMap;
        }

        PropertyMap ISheet.GetPropertyMap(SheetConvertingContext context) => GetPropertyMap(context);

        void ISheet.MapReferences(SheetConvertingContext context, Dictionary<Type, ISheet> rowTypeToSheet)
        {
            using (context.Logger.BeginScope(Name))
            {
                var propertyMap = GetPropertyMap(context);

                propertyMap.UpdateIndex(this);

                foreach (var row in Items)
                {
                    foreach (var value in propertyMap.TraverseValues(row))
                    {
                        if (!typeof(ISheetReference).IsAssignableFrom(value.ValueType))
                            continue;

                        var referenceRowType = value.ValueType.GenericTypeArguments[1];

                        if (!rowTypeToSheet.TryGetValue(referenceRowType, out var sheet))
                        {
                            context.Logger.LogError(
                                "Failed to find sheet for {ReferenceType} reference", referenceRowType);
                            continue;
                        }

                        using (context.Logger.BeginScope(row.Id))
                        using (context.Logger.BeginScope(value.Path))
                        {
                            if (!value.IsPresent || !(value.Value is ISheetReference refer))
                                continue;

                            refer.Map(context, sheet);
                            value.SetValue(value.Value);
                        }
                    }
                }
            }
        }

        public virtual void PostLoad(SheetConvertingContext context)
        {
            using (context.Logger.BeginScope(Name))
            {
                int index = -1;

                foreach (var row in Items)
                {
                    using (context.Logger.BeginScope(row.Id))
                    {
                        row.Index = ++index;
                        row.PostLoad(context);
                    }
                }
            }
        }

        public virtual void VerifyAssets(SheetConvertingContext context)
        {
            using (context.Logger.BeginScope(Name))
            {
                var propertyMap = GetPropertyMap(context);

                propertyMap.UpdateIndex(this);

                foreach (var row in Items)
                {
                    foreach (var value in propertyMap.TraverseValues(row))
                    {
                        foreach (var verifier in context.Verifiers)
                        {
                            if (!verifier.CanVerify(value.PropertyInfo, value.ValueType))
                                continue;

                            using (context.Logger.BeginScope(row.Id))
                            using (context.Logger.BeginScope(value.Path))
                            {
                                var err = verifier.Verify(value.PropertyInfo, value.Value);

                                if (err != null)
                                    context.Logger.LogError("Verification: {Error}", err);
                            }
                        }
                    }
                }

                foreach (var row in Items)
                {
                    using (context.Logger.BeginScope(row.Id))
                    {
                        row.VerifyAssets(context);
                    }
                }
            }
        }

        /// <summary>
        /// Struct enumerator for Sheet.
        /// </summary>
        public struct Enumerator : IEnumerator<TValue>
        {
            private readonly Sheet<TKey, TValue> _sheet;
            private int _index;

            public Enumerator(Sheet<TKey, TValue> sheet)
            {
                _sheet = sheet;
                _index = -1;
            }

            public bool MoveNext() => ++_index < _sheet.Count;
            public readonly TValue Current => _sheet[_index];

            readonly object IEnumerator.Current => Current;
            void IEnumerator.Reset() => _index = -1;

            public readonly void Dispose() { }
        }
    }

    /// <summary>
    /// Represents a single page of Sheet, with string Id.
    /// For other type of Id, use generic version.
    /// </summary>
    /// <typeparam name="T">Type of Row.</typeparam>
    public abstract class Sheet<T> : Sheet<string, T>, ISheet<T>
        where T : SheetRow<string>, new() {}
}
