// BakingSheet, Maxwell Keonwoo Kang <code.athei@gmail.com>, 2022

using System.Collections.Generic;

namespace Cathei.BakingSheet
{
    public interface IVerticalDictionary { }

    /// <summary>
    /// Usage is same as generic Dictionary.
    /// When converting from/to sheet, entries are vertical.
    /// </summary>
    /// <typeparam name="TKey">The type of keys in the dictionary.</typeparam>
    /// <typeparam name="TValue">The type of values in the dictionary.</typeparam>
    public class VerticalDictionary<TKey, TValue> : Dictionary<TKey, TValue>, IVerticalDictionary
    {
        public VerticalDictionary() { }
        public VerticalDictionary(int capacity) : base(capacity) { }
        public VerticalDictionary(IEqualityComparer<TKey> comparer) : base(comparer) { }
        public VerticalDictionary(int capacity, IEqualityComparer<TKey> comparer) : base(capacity, comparer) { }
        public VerticalDictionary(IDictionary<TKey, TValue> dictionary) : base(dictionary) { }
        public VerticalDictionary(IDictionary<TKey, TValue> dictionary, IEqualityComparer<TKey> comparer)
            : base(dictionary, comparer) { }
    }
}
