﻿using System;
using System.Collections.ObjectModel;
using System.Reflection;
using Cathei.BakingSheet.Internal;
using Microsoft.Extensions.Logging;

namespace Cathei.BakingSheet
{
    public abstract partial class Sheet<TKey, TValue> : KeyedCollection<TKey, TValue>, ISheet<TKey, TValue>
        where TValue : SheetRow<TKey>, new()
    {
        public string Name { get; set; }

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

        public TValue Find(TKey id) => this[id];

        bool ISheet.Contains(object key) => Contains((TKey)key);
        void ISheet.Add(object value) => Add((TValue)value);

        protected override TKey GetKeyForItem(TValue item)
        {
            return item.Id;
        }

        public virtual void PostLoad(SheetConvertingContext context)
        {
            using (context.Logger.BeginScope(Name))
            {
                var propertyMap = new PropertyMap(context, GetType(), Config.IsConvertable);

                propertyMap.UpdateIndex(this);

                foreach ((var node, var indexes) in propertyMap.TraverseLeaf())
                {
                    if (!typeof(ISheetReference).IsAssignableFrom(node.ValueType))
                        continue;

                    foreach (var row in Items)
                    {
                        int verticalSize = node.GetVerticalSize(row);

                        for (int i = 1; i <= verticalSize; ++i)
                        {
                            indexes[0] = i;

                            using (context.Logger.BeginScope(row.Id))
                            using (context.Logger.BeginScope(node.FullPath, indexes))
                            {
                                var obj = node.GetValue(row, indexes.GetEnumerator());

                                if (obj is ISheetReference refer)
                                {
                                    refer.Map(context);
                                    node.SetValue(row, indexes.GetEnumerator(), refer);
                                }
                            }
                        }
                    }
                }

                foreach (var row in Items)
                {
                    using (context.Logger.BeginScope(row.Id))
                    {
                        row.PostLoad(context);
                    }
                }
            }
        }

        public virtual void VerifyAssets(SheetConvertingContext context)
        {
            using (context.Logger.BeginScope(Name))
            {
                var propertyMap = new PropertyMap(context, GetType(), Config.IsConvertable);

                propertyMap.UpdateIndex(this);

                foreach ((var node, bool isArray, var indexes) in propertyMap.TraverseLeaf())
                {
                    foreach (var verifier in context.Verifiers)
                    {
                        if (!verifier.TargetType.IsAssignableFrom(node.ValueType))
                            continue;

                        foreach (var att in node.AttributesGetter(verifier.TargetAttribute))
                        {
                            foreach (var row in Items)
                            {
                                void verifyAsset()
                                {
                                    using (context.Logger.BeginScope(row.Id))
                                    using (context.Logger.BeginScope(node.FullPath, indexes))
                                    {
                                        var err = verifier.Verify(att, node.GetValue(row, indexes.GetEnumerator()));
                                        if (err != null)
                                            context.Logger.LogError("Verification: {Error}", err);
                                    }
                                }

                                if (!isArray)
                                {
                                    verifyAsset();
                                }
                                else if (row is ISheetRowArray rowArray)
                                {
                                    for (int i = 0; i < rowArray.Arr.Count; ++i)
                                    {
                                        // use 1-base for index
                                        indexes[0] = i + 1;
                                        verifyAsset();
                                    }
                                }
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
    }

    // Convenient shorthand
    public abstract class Sheet<T> : Sheet<string, T>
        where T : SheetRow<string>, new() {}
}
