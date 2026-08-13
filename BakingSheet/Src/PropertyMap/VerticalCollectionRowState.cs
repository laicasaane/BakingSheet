// BakingSheet, Maxwell Keonwoo Kang <code.athei@gmail.com>, 2022

using System;
using System.Collections;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Cathei.BakingSheet.Internal
{
    internal sealed class VerticalCollectionRowState
    {
        private enum EntryState
        {
            None,
            Accepted,
            Rejected,
        }

        private sealed class PendingValue
        {
            public PropertyColumnBinding Binding;
            public object Value;
            public bool Failed;
        }

        private sealed class ListState
        {
            public int CurrentIndex = -1;
            public int LastDataRow = -1;
        }

        private sealed class DictionaryState
        {
            public EntryState State;
            public object CurrentKey;
        }

        private readonly VerticalCollectionLayout _layout;
        private readonly PropertyValueAddress _address = new PropertyValueAddress();
        private readonly List<PendingValue> _pending = new List<PendingValue>();
        private readonly Dictionary<object, ListState> _lists =
            new Dictionary<object, ListState>(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<object, DictionaryState> _dictionaries =
            new Dictionary<object, DictionaryState>(ReferenceEqualityComparer.Instance);
        private readonly HashSet<string> _activeTargets =
            new HashSet<string>(StringComparer.Ordinal);

        private ISheetRow _row;
        private int _dataRow;
        private bool _valid;

        internal bool IsValid => _valid;

        public VerticalCollectionRowState(VerticalCollectionLayout layout)
        {
            _layout = layout;
        }

        public void BeginRow(ISheetRow row)
        {
            _row = row;
            _dataRow = 0;
            _valid = true;
            _pending.Clear();
            _lists.Clear();
            _dictionaries.Clear();
            _activeTargets.Clear();
        }

        public bool HasRequiredContext(PropertyColumnBinding binding)
        {
            foreach (var target in _layout.GetRequiredTargets(binding))
            {
                if (!_activeTargets.Contains(target.MarkerPath))
                    return false;
            }

            return true;
        }

        public void SetValue(PropertyColumnBinding binding, object value)
        {
            _pending.Add(new PendingValue { Binding = binding, Value = value });
        }

        public void RejectValue(PropertyColumnBinding binding)
        {
            _pending.Add(new PendingValue { Binding = binding, Failed = true });
        }

        public bool BeginTarget(VerticalCollectionTarget target)
        {
            if (!_valid || _row == null ||
                !PropertyValueAccessor.EnsureNodeValue(
                    _row, target.Binding, target.Owner, _address, out var value) ||
                !(value is IList list))
            {
                _valid = false;
                return false;
            }

            var state = GetListState(list);
            state.CurrentIndex++;
            EnsureListItem(list, target.Owner, state.CurrentIndex);
            _address.SetListIndex(list, state.CurrentIndex);

            foreach (var candidate in _layout.Targets)
            {
                if (candidate.Depth > target.Depth &&
                    candidate.Binding.ContainsNode(target.Boundary))
                {
                    _activeTargets.Remove(candidate.MarkerPath);
                }
            }

            _activeTargets.Add(target.MarkerPath);
            return true;
        }

        public void EndDataRow(SheetConvertingContext context)
        {
            if (!_valid || _row == null)
            {
                _pending.Clear();
                return;
            }

            int maxDepth = 0;

            foreach (var pending in _pending)
                maxDepth = Math.Max(maxDepth, pending.Binding.VerticalOwners.Count);

            for (int depth = 0; depth < maxDepth; ++depth)
                ProcessOwners(context, depth);

            foreach (var pending in _pending)
            {
                if (pending.Failed || pending.Binding.IsAnyDictionaryKey())
                    continue;

                PropertyValueAccessor.SetValue(_row, pending.Binding, _address, pending.Value);
            }

            _pending.Clear();
            _dataRow++;
        }

        public bool EndRow(SheetConvertingContext context)
        {
            if (_pending.Count > 0)
                EndDataRow(context);

            bool valid = _valid && _row != null;
            _row = null;
            return valid;
        }

        public void DiscardRow()
        {
            _valid = false;
            _row = null;
            _pending.Clear();
            _lists.Clear();
            _dictionaries.Clear();
            _activeTargets.Clear();
        }

        private void ProcessOwners(SheetConvertingContext context, int depth)
        {
            var processed = new HashSet<object>(ReferenceEqualityComparer.Instance);

            foreach (var pending in _pending)
            {
                if (depth >= pending.Binding.VerticalOwners.Count)
                    continue;

                var owner = pending.Binding.VerticalOwners[depth];

                if (!PropertyValueAccessor.EnsureNodeValue(
                        _row, pending.Binding, owner, _address, out var collection) ||
                    collection == null || processed.Contains(collection))
                {
                    continue;
                }

                processed.Add(collection);

                if (owner is PropertyNodeList list && collection is IList values)
                {
                    var group = GetPendingGroup(owner, values);
                    bool advancesOwner = depth == 0;

                    if (!advancesOwner)
                    {
                        foreach (var item in group)
                        {
                            if (item.Binding.VerticalOwners.Count == depth + 1)
                            {
                                advancesOwner = true;
                                break;
                            }
                        }
                    }

                    SelectListItem(values, list, pending.Binding, advancesOwner);
                    continue;
                }

                if (owner is PropertyNodeVerticalDictionary dictionary && collection is IDictionary entries)
                {
                    var group = GetPendingGroup(owner, entries);
                    ProcessDictionary(context, dictionary, entries, group, depth);
                }
            }
        }

        private List<PendingValue> GetPendingGroup(PropertyNode owner, object collection)
        {
            var result = new List<PendingValue>();

            foreach (var pending in _pending)
            {
                if (!pending.Binding.ContainsOwner(owner))
                    continue;

                if (PropertyValueAccessor.TryGetNodeValue(
                        _row, pending.Binding, owner, _address, out var candidate) &&
                    ReferenceEquals(candidate, collection))
                {
                    result.Add(pending);
                }
            }

            return result;
        }

        private void SelectListItem(
            IList list, PropertyNodeList owner, PropertyColumnBinding binding,
            bool advancesOwner)
        {
            var state = GetListState(list);
            bool requiresMarker = _layout.TryGetTarget(owner, binding, out _);

            if (state.CurrentIndex < 0)
            {
                if (requiresMarker)
                {
                    _valid = false;
                    return;
                }

                state.CurrentIndex = 0;
            }
            else if (!requiresMarker && advancesOwner && state.LastDataRow != _dataRow)
            {
                state.CurrentIndex++;
            }

            if (advancesOwner)
                state.LastDataRow = _dataRow;
            EnsureListItem(list, owner, state.CurrentIndex);
            _address.SetListIndex(list, state.CurrentIndex);
        }

        private void ProcessDictionary(SheetConvertingContext context,
            PropertyNodeVerticalDictionary owner, IDictionary dictionary,
            IReadOnlyList<PendingValue> group, int depth)
        {
            var state = GetDictionaryState(dictionary);
            var keyValues = new List<PendingValue>();
            bool hasValueInput = false;
            bool hasVerticalValue = false;

            foreach (var pending in group)
            {
                if (pending.Binding.IsDictionaryKey(owner))
                {
                    keyValues.Add(pending);
                    continue;
                }

                hasValueInput = true;

                if (depth + 1 < pending.Binding.VerticalOwners.Count)
                    hasVerticalValue = true;
            }

            if (keyValues.Count > 0)
            {
                foreach (var pending in keyValues)
                {
                    if (pending.Failed)
                    {
                        RejectDictionary(state, dictionary);
                        return;
                    }
                }

                object key = CreateSubtreeValue(owner.Key);

                foreach (var pending in keyValues)
                    PropertyValueAccessor.SetSubtreeValue(ref key, owner.Key, pending.Binding, pending.Value);

                if (dictionary.Contains(key))
                {
                    context.Logger.LogError("Vertical dictionary already contains key \"{Key}\".", key);
                    RejectDictionary(state, dictionary);
                    return;
                }

                object value = CreateDictionaryValue(context, owner, key, group, hasValueInput);
                dictionary.Add(key, value);

                state.State = EntryState.Accepted;
                state.CurrentKey = key;
                _address.SetDictionaryKey(dictionary, key);
                return;
            }

            if (!hasValueInput)
                return;

            if (state.State == EntryState.Rejected)
            {
                _address.RemoveDictionary(dictionary);
                return;
            }

            if (state.State != EntryState.Accepted || !hasVerticalValue)
            {
                context.Logger.LogError("Vertical dictionary entry requires a key.");
                _address.RemoveDictionary(dictionary);
                return;
            }

            _address.SetDictionaryKey(dictionary, state.CurrentKey);
        }

        private object CreateDictionaryValue(SheetConvertingContext context,
            PropertyNodeVerticalDictionary owner, object key,
            IReadOnlyList<PendingValue> group, bool hasValueInput)
        {
            if (!hasValueInput)
            {
                if (IsGrowableCollection(owner.ElementType))
                    return Activator.CreateInstance(owner.ElementType);

                context.Logger.LogError(
                    "Vertical dictionary entry for key \"{Key}\" requires a value.", key);
                return GetDefault(owner.ElementType);
            }

            if (owner.Value.IsLeafNode)
            {
                foreach (var pending in group)
                {
                    if (!pending.Failed && !pending.Binding.IsDictionaryKey(owner))
                        return pending.Value;
                }

                return GetDefault(owner.ElementType);
            }

            return CreateSubtreeValue(owner.Value);
        }

        private ListState GetListState(IList list)
        {
            if (!_lists.TryGetValue(list, out var state))
            {
                state = new ListState();
                _lists.Add(list, state);
            }

            return state;
        }

        private DictionaryState GetDictionaryState(IDictionary dictionary)
        {
            if (!_dictionaries.TryGetValue(dictionary, out var state))
            {
                state = new DictionaryState();
                _dictionaries.Add(dictionary, state);
            }

            return state;
        }

        private void RejectDictionary(DictionaryState state, IDictionary dictionary)
        {
            state.State = EntryState.Rejected;
            state.CurrentKey = null;
            _address.RemoveDictionary(dictionary);
        }

        private static void EnsureListItem(IList list, PropertyNodeList owner, int index)
        {
            while (list.Count <= index)
                list.Add(CreateSubtreeValue(owner.Child));
        }

        private static object CreateSubtreeValue(PropertyNode node)
        {
            return node.ValueType.IsValueType
                ? Activator.CreateInstance(node.ValueType)
                : node.IsLeafNode ? null : Activator.CreateInstance(node.ValueType);
        }

        private static bool IsGrowableCollection(Type type)
        {
            return !type.IsArray &&
                   (typeof(IList).IsAssignableFrom(type) ||
                    typeof(IVerticalDictionary).IsAssignableFrom(type));
        }

        private static object GetDefault(Type type)
        {
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }
    }
}
