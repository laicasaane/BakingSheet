// BakingSheet, Maxwell Keonwoo Kang <code.athei@gmail.com>, 2022

using System.Collections.Generic;
using System.Runtime.InteropServices;
using Cathei.BakingSheet.Unity;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Cathei.BakingSheet.Editor
{
    public class SheetReferenceDropdown : AdvancedDropdown
    {
        private readonly SerializedProperty _property;
        private readonly string _label;
        private readonly string _targetTypeInfo;

#if UNITY_6000_3_OR_NEWER
        private readonly Dictionary<int, EntityId> _indexToEntityId = new Dictionary<int, EntityId>();
#endif

        // private List<UnityEngine.Object> _selections = new List<UnityEngine.Object>();

        public SheetReferenceDropdown(
            SerializedProperty property, string label, string targetTypeInfo, AdvancedDropdownState state) : base(state)
        {
            _property = property;
            _label = label;
            _targetTypeInfo = targetTypeInfo;

            var minSize = base.minimumSize;
            minSize.y = 300f;
            base.minimumSize = minSize;
        }

        protected override AdvancedDropdownItem BuildRoot()
        {
            var root = new AdvancedDropdownItem(_label);

            var nullItem = new AdvancedDropdownItem("(None)");
            nullItem.id = 0;
            root.AddChild(nullItem);

            var assetGuids = AssetDatabase.FindAssets($"t:{nameof(SheetScriptableObject)}");

#if UNITY_6000_3_OR_NEWER
            _indexToEntityId.Clear();
#endif

            foreach (var assetGuid in assetGuids)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(assetGuid);
                var sheetSO = AssetDatabase.LoadAssetAtPath<SheetScriptableObject>(assetPath);

                if (sheetSO.typeInfo != _targetTypeInfo)
                    continue;

                root.AddSeparator();

                foreach (var rowSO in sheetSO.Rows)
                {
#if UNITY_6000_3_OR_NEWER
                    var union = new EntityIdUnion(rowSO.GetEntityId());

                    if (_indexToEntityId.TryAdd(union.index, union.entityId) == false)
                    {
                        Debug.LogWarning($"Duplicate EntityId index '{union.index}' for '{rowSO.name}'. Skipping from dropdown.");
                        continue;
                    }

                    var dropdownItem = new AdvancedDropdownItem(rowSO.name)
                    {
                        id = union.index,
                        icon = AssetPreview.GetMiniThumbnail(rowSO)
                    };
#else
                    var dropdownItem = new AdvancedDropdownItem(rowSO.name)
                    {
                        id = rowSO.GetInstanceID(),
                        icon = AssetPreview.GetMiniThumbnail(rowSO)
                    };
#endif

                    root.AddChild(dropdownItem);
                }
            }

            return root;
        }

        protected override void ItemSelected(AdvancedDropdownItem item)
        {
            base.ItemSelected(item);

#if UNITY_6000_3_OR_NEWER
            if (_indexToEntityId.TryGetValue(item.id, out var entityId))
            {
                _property.entityIdValue = entityId;
            }
            else
            {
                Debug.LogWarning($"Cannot find EntityId by index '{item.id}'.");
                return;
            }
#else
            _property.objectReferenceInstanceIDValue = item.id;
#endif

            _property.serializedObject.ApplyModifiedProperties();
        }

#if UNITY_6000_3_OR_NEWER
        [StructLayout(LayoutKind.Explicit)]
        private struct EntityIdUnion
        {
            [FieldOffset(0)] public EntityId entityId;
            [FieldOffset(0)] public int index;
            [FieldOffset(4)] public int version;

            public EntityIdUnion(EntityId entityId) : this()
            {
                this.entityId = entityId;
            }
        }
#endif
    }
}