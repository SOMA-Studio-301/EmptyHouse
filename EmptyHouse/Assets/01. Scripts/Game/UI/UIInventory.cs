using Border.Core;
using UnityEngine;

/// <summary>
/// HUD 인벤토리 패널 (조작상호작용UI.md 5장 — 인벤 칸 상시 노출).
/// 플레이어 프리팹의 Canvas-Inventory 에 붙는다. 칸 수를 스스로 정하지 않고
/// <see cref="PlayerInventory"/> 가 Start 에서 <see cref="Build"/> 로 지시한다 —
/// 의존이 게임플레이 → UI 한 방향으로만 흐르고, 슬롯 수의 진실 원천이 PlayerInventory 하나로 유지된다.
/// 슬롯 수는 🔵 상점에서 늘어나므로 3 을 상수로 박지 않는다(4-5, 6-10).
/// </summary>
public class UIInventory : MonoBehaviour
{
    [Header("Slots")]
    [SerializeField] private UIInventorySlot slotPrefab;
    [SerializeField] private Transform slotParent;

    private UIInventorySlot[] slotViews;

    /// <summary>
    /// 슬롯 뷰를 slotCount 개 만들어 slotParent 밑에 배치한다. 전부 빈 칸·하이라이트 off 로 시작한다.
    /// 슬롯 확장(4-5)으로 다시 호출될 수 있으므로 기존 뷰를 먼저 파기한다.
    /// </summary>
    /// <param name="slotCount">만들 칸 수. PlayerInventory.SlotCount 를 그대로 받는다.</param>
    public void Build(int slotCount)
    {
        ClearSlots();

        slotViews = new UIInventorySlot[slotCount];

        for (int i = 0; i < slotCount; i++)
        {
            UIInventorySlot view = Instantiate(slotPrefab, slotParent);
            view.name = $"Slot_{i + 1}";
            view.SetNumber(i + 1); // 라벨은 1-based — 인덱스가 아니라 Tab+숫자 키의 번호다
            view.SetEmpty();
            view.SetHighlight(false);

            slotViews[i] = view;
        }
    }

    /// <summary>
    /// 한 칸의 내용을 갱신한다. 인벤이 바뀐 시점에만 <see cref="PlayerInventory"/> 가 호출한다 —
    /// 매 프레임 폴링하지 않는다.
    /// </summary>
    /// <param name="index">갱신할 칸 인덱스(0 .. SlotCount-1).</param>
    /// <param name="slot">그 칸의 현재 상태. 빈 칸이면 아이콘을 지운다.</param>
    public void SetSlot(int index, InventorySlot slot)
    {
        UIInventorySlot view = slotViews[index];

        if (slot.IsEmpty)
        {
            view.SetEmpty();
            return;
        }

        view.SetItem(slot.Data);
    }

    /// <summary>
    /// 손 하이라이트를 지정 칸으로 옮긴다. 하이라이트는 손 포인터를 그대로 따른다(빈 칸 이여도)
    /// <see cref="PlayerInventory"/> 가 호출한다.
    /// </summary>
    /// <param name="heldIndex">손이 가리키는 칸 인덱스. <see cref="PlayerInventory.BareHandIndex"/>(-1)면 전부 끈다.</param>
    public void SetHeldIndex(int heldIndex)
    {
        for (int i = 0; i < slotViews.Length; i++)
        {
            slotViews[i].SetHighlight(i == heldIndex);
        }
    }

    /// <summary>
    /// slotParent 밑을 전부 비운다. 재생성 시 이전 칸이 남아 중복 표시되는 것을 막고,
    /// 에디터에서 미리 배치해 둔 더미 슬롯도 첫 Build 에서 함께 제거한다.
    /// </summary>
    private void ClearSlots()
    {
        for (int i = slotParent.childCount - 1; i >= 0; i--)
        {
            Destroy(slotParent.GetChild(i).gameObject);
        }

        slotViews = null;
    }
}
