/// <summary>
/// 인벤토리 한 칸의 런타임 상태 (조작상호작용UI.md 4-1: 아이템 1개 = 1칸).
/// 데이터 컨테이너일 뿐 규칙을 갖지 않는다 — 채움/비움 규칙은 <see cref="PlayerInventory"/> 가 소유한다.
/// </summary>
public struct InventorySlot
{
    /// <summary>칸에 든 아이템. null 이면 빈 칸이다.</summary>
    public ItemDataSO Data;

    /// <summary>페어 번호(NN). <see cref="ItemKind.Key"/> 일 때만 의미가 있고 그 외에는 0 이다(0-3 넘버링 규약).</summary>
    public int PairId;

    /// <summary>빈 칸 여부.</summary>
    public bool IsEmpty => Data == null;

    /// <summary>칸을 비운다.</summary>
    public void Clear()
    {
        Data = null;
        PairId = 0;
    }
}
