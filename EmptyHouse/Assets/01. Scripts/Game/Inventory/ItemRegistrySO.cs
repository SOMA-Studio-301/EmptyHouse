using System.Collections.Generic;
using Border.Core;
using UnityEngine;

/// <summary>
/// 아이템 데이터 ↔ 네트워크 ID 변환표.
/// <see cref="ItemDataSO"/> 는 에셋 참조라 그대로는 네트워크에 실을 수 없으므로,
/// 전 클라이언트가 공유하는 이 목록의 인덱스를 ID 로 삼는다(<see cref="PlayerHandItem"/> 이 유일한 사용자).
/// 목록 순서가 곧 ID 이므로 런타임 중 순서가 바뀔 일은 없지만, 세션 사이에 순서를 바꿔도
/// 모든 클라이언트가 같은 에셋을 로드하므로 불일치는 생기지 않는다.
/// </summary>
[CreateAssetMenu(fileName = "SO_ItemRegistry", menuName = "EmptyHouse/Item/Item Registry")]
public class ItemRegistrySO : ScriptableObject
{
    /// <summary>아무것도 들지 않은 상태(맨손)를 뜻하는 ID.</summary>
    public const int NoneId = -1;

    [SerializeField] private List<ItemDataSO> items = new List<ItemDataSO>();

    /// <summary>아이템의 네트워크 ID 를 구한다.</summary>
    /// <param name="item">조회할 아이템. null 이면 맨손이다.</param>
    /// <returns>목록 내 인덱스. 맨손이거나 목록에 없으면 <see cref="NoneId"/>.</returns>
    public int GetId(ItemDataSO item)
    {
        if (item == null) return NoneId;

        int index = items.IndexOf(item);
        if (index < 0)
        {
            Log.E($"[ItemRegistry] {item.name} 이 레지스트리에 없다 — 손에 든 모습이 남에게 보이지 않는다.");
            return NoneId;
        }

        return index;
    }

    /// <summary>네트워크 ID 에 해당하는 아이템을 구한다.</summary>
    /// <param name="id">조회할 ID.</param>
    /// <returns>해당 아이템. <see cref="NoneId"/> 이거나 범위를 벗어나면 null(맨손).</returns>
    public ItemDataSO GetItem(int id)
    {
        return id >= 0 && id < items.Count ? items[id] : null;
    }

#if UNITY_EDITOR
    /// <summary>
    /// 프로젝트의 모든 <see cref="ItemDataSO"/> 를 이름순으로 목록에 채운다.
    /// 아이템 에셋을 추가하고 레지스트리에 넣는 것을 잊는 실수를 막기 위한 편의 기능이다.
    /// </summary>
    [ContextMenu("프로젝트의 아이템 전부 수집")]
    private void CollectAllItems()
    {
        string[] guids = UnityEditor.AssetDatabase.FindAssets($"t:{nameof(ItemDataSO)}");
        System.Array.Sort(guids, (a, b) => string.CompareOrdinal(
            UnityEditor.AssetDatabase.GUIDToAssetPath(a),
            UnityEditor.AssetDatabase.GUIDToAssetPath(b)));

        items.Clear();
        foreach (string guid in guids)
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            items.Add(UnityEditor.AssetDatabase.LoadAssetAtPath<ItemDataSO>(path));
        }

        UnityEditor.EditorUtility.SetDirty(this);
        Log.D($"[ItemRegistry] {items.Count}개 아이템 수집");
    }
#endif
}
