using Border.Core;
using UnityEngine;

/// <summary>
/// 플레이어 1인의 인벤토리 — 슬롯 N칸 + 손(슬롯 포인터) (조작상호작용UI.md 4장).
/// 상주 매니저가 아니라 플레이어 프리팹에 붙는 플레이어별 컴포넌트다(멀티에서 사람마다 1개).
/// 손은 별도 칸이 아니라 "지금 꺼내 든 슬롯의 인덱스"이며(4-2), -1 = 맨손이다.
/// 입력(Tab 홀드/휠/G) 연결은 .inputactions 확장 후 후속 단계에서 이 API 를 호출하는 형태로 붙이고,
/// HUD 통지 방식은 HUD(5장) 스켈레톤 단계에서 결정한다.
/// </summary>
public class PlayerInventory : MonoBehaviour
{
    /// <summary>맨손을 뜻하는 손 인덱스 값 (0-2 용어: 맨손).</summary>
    public const int BareHandIndex = -1;

    [Header("Slots")]
    [SerializeField] private int inventorySlots = 3; // 3-10 inventory_slots ⚪ — 상한이 아니라 시작값. 상수로 박지 말 것(6-10)

    [Header("Hand")]
    [SerializeField] private float handSwapSeconds = 0.3f; // 3-10 hand_swap_sec ⚪ — 손 전환 딜레이(E6)

    private InventorySlot[] slots;
    private int heldIndex = BareHandIndex;

    /// <summary>슬롯 수. 시작값은 inventory_slots 이며 🔵 상점 확장의 기준점이다(4-5).</summary>
    public int SlotCount => slots.Length;

    /// <summary>손이 가리키는 슬롯 인덱스. <see cref="BareHandIndex"/> 면 맨손이다(4-2).</summary>
    public int HeldIndex => heldIndex;

    /// <summary>
    /// 맨손 여부. 어떤 슬롯도 가리키지 않는 상태(0-2)에 더해, 휠 순환으로 **빈 칸을 가리키는 상태도
    /// 맨손 취급**이다(기획 확정 2026-07-11) — 포인터는 그 칸에 머물러 다음 휠 입력의 기준이 된다.
    /// </summary>
    public bool IsBareHanded => heldIndex == BareHandIndex || slots[heldIndex].IsEmpty;

    /// <summary>손에 든 칸의 상태. 맨손(빈 칸을 가리키는 경우 포함)이면 빈 값을 반환한다.</summary>
    public InventorySlot HeldSlot => IsBareHanded ? default : slots[heldIndex];

    /// <summary>
    /// 손 전환 딜레이(hand_swap_sec) 진행 중 여부.
    /// 전환 중에는 E·좌클릭이 무반응이고, 다른 슬롯 입력도 무시된다 — 큐잉 없음(3-8 E6, 2-1).
    /// </summary>
    public bool IsSwapping { get; private set; }

    /// <summary>슬롯 배열을 인스펙터 크기로 생성한다.</summary>
    private void Awake()
    {
        slots = new InventorySlot[inventorySlots];
        Log.D($"[PlayerInventory] Awake slots={inventorySlots}");
    }

    /// <summary>손 전환 타이머를 갱신한다.</summary>
    private void Update()
    {
        // TODO(impl): 손 전환 타이머 갱신 → handSwapSeconds 경과 시 IsSwapping 해제.
        // 매 프레임 호출되므로 진입 트레이스를 두지 않는다.
    }

    /// <summary>지정 인덱스의 슬롯 상태를 반환한다(HUD 3칸 상시 표시용, 5장).</summary>
    /// <param name="index">슬롯 인덱스(0 .. SlotCount-1).</param>
    /// <returns>해당 칸의 상태.</returns>
    public InventorySlot GetSlot(int index)
    {
        return slots[index];
    }

    /// <summary>빈 슬롯이 하나라도 있는지 반환한다. 회수 프롬프트의 활성/비활성(3-8 E8) 판정에 쓴다.</summary>
    /// <returns>빈 슬롯 존재 여부.</returns>
    public bool HasEmptySlot()
    {
        return FindEmptySlotIndex() >= 0;
    }

    /// <summary>첫 번째 빈 슬롯의 인덱스를 찾는다. 회수 판정(HasEmptySlot)과 실제 편입(TryAdd)이 같은 규칙을 쓰도록 한 곳에 둔다.</summary>
    /// <returns>첫 빈 슬롯 인덱스. 만재면 -1.</returns>
    private int FindEmptySlotIndex()
    {
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i].IsEmpty) return i;
        }

        return -1;
    }

    /// <summary>
    /// 지정 종류·페어 번호의 아이템이 어느 슬롯에든 있는지 반환한다.
    /// 자물쇠의 "열쇠를 손에 드세요"(있는데 안 들었다) vs "키가 필요합니다"(아예 없다) 분기(3-6)에 쓴다.
    /// </summary>
    /// <param name="kind">찾을 아이템 종류.</param>
    /// <param name="pairId">페어 번호(NN). 페어 개념이 없는 종류는 0.</param>
    /// <returns>소지 여부.</returns>
    public bool Contains(ItemKind kind, int pairId)
    {
        // TODO(impl): slots 순회로 kind·pairId 일치 탐색.
        // 매 프레임(프롬프트 판정) 호출되므로 진입 트레이스를 두지 않는다.
        return default;
    }

    /// <summary>
    /// 아이템을 빈 슬롯 1칸에 넣는다 (3-5 회수 규칙 — 전 아이템 공통).
    /// 손에 자동으로 쥐어지지 않는다(4-2 회수 시 자동 장착 금지) — heldIndex 를 건드리지 않는다.
    /// </summary>
    /// <param name="item">획득할 아이템 데이터.</param>
    /// <param name="pairId">페어 번호(NN). 열쇠 외에는 0.</param>
    /// <returns>획득 성공 여부. 만재면 false(E8: 가방이 가득 찼습니다).</returns>
    public bool TryAdd(ItemDataSO item, int pairId)
    {
        Log.D($"[PlayerInventory] TryAdd {item.ItemName} pairId={pairId}");

        int index = FindEmptySlotIndex();
        if (index < 0) return false;

        slots[index].Data = item;
        slots[index].PairId = pairId;

        // heldIndex 는 건드리지 않는다 — 회수는 손에 자동으로 쥐어주지 않는다(4-2).
        return true;
    }

    /// <summary>
    /// 지정 슬롯을 손에 든다 (Tab 홀드 + 숫자). 빈 슬롯이면 맨손이 된다(4-2 전환 조작).
    /// 손 전환 딜레이를 시작하며, 이미 전환 중이면 입력을 무시한다 — 연타로 딜레이를 우회할 수 없다(2-1).
    /// </summary>
    /// <param name="index">손에 들 슬롯 인덱스(0 .. SlotCount-1).</param>
    public void EquipSlot(int index)
    {
        // TODO(impl): IsSwapping 중이면 무시. 빈 슬롯 → 맨손. heldIndex 갱신 + 전환 딜레이 시작.
        Log.D($"[PlayerInventory] EquipSlot {index}");
    }

    /// <summary>
    /// 손에 든 것을 슬롯에 되돌리고 맨손이 된다 — 집어넣기 (Tab 홀드 후 숫자 없이 뗌).
    /// 아이템은 슬롯에 그대로 남는다(0-2: 집어넣기 ≠ 버리기).
    /// </summary>
    public void StowHand()
    {
        // TODO(impl): heldIndex = BareHandIndex. 슬롯 내용은 유지.
        Log.D("[PlayerInventory] StowHand");
    }

    /// <summary>
    /// 손 슬롯을 순환한다 (마우스 휠): 맨손 → 1 → 2 → 3 → 맨손 (2장 키맵).
    /// 빈 칸을 건너뛰지 않는다 — 빈 칸에 멈추고 그동안은 맨손 취급이다(기획 확정 2026-07-11).
    /// </summary>
    /// <param name="direction">휠 방향. +1 = 정방향, -1 = 역방향.</param>
    public void CycleHand(int direction)
    {
        // TODO(impl): heldIndex 를 [BareHandIndex .. SlotCount-1] 범위에서 빈 칸 포함 순환. 전환 딜레이 적용.
        Log.D($"[PlayerInventory] CycleHand {direction}");
    }

    /// <summary>
    /// 손에 든 아이템을 월드에 떨군다 — 버리기 (G). WorldPrefab 을 스폰하고 슬롯을 비운 뒤
    /// 자동 맨손이 된다(4-2 자동 맨손 규칙). 맨손이면 아무것도 하지 않는다.
    /// </summary>
    public void DropHeld()
    {
        // TODO(impl): 맨손이면 return. HeldSlot.Data.WorldPrefab 스폰(페어 번호 승계) → 슬롯 Clear → 맨손.
        Log.D("[PlayerInventory] DropHeld");
    }

    /// <summary>
    /// 손에 든 아이템을 사용으로 소멸시킨다 — 자물쇠 개방 시 열쇠(3-6) · 투척 시 투척물(4-3).
    /// 슬롯이 비고 자동 맨손이 된다. 다음 슬롯을 자동 장착하지 않으며(4-2), 버리기와 달리 월드에 아무것도 남기지 않는다.
    /// </summary>
    public void ConsumeHeld()
    {
        // TODO(impl): 슬롯 Clear → 맨손. WorldPrefab 스폰 없음(버리기와 다르다).
        Log.D("[PlayerInventory] ConsumeHeld");
    }
}
