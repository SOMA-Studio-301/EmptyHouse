using Border.Core;
using UnityEngine;

/// <summary>
/// 플레이어 1인의 인벤토리 — 슬롯 N칸 + 손(슬롯 포인터)
/// 플레이어 프리팹에 붙는 플레이어별 컴포넌트다(멀티에서 사람마다 1개)
/// 손은 별도 칸이 아니라 "지금 꺼내 든 슬롯의 인덱스"이며, -1 = 맨손
/// 입력은 InputReader 이벤트를 직접 구독한다 — 현재는 숫자 단독(임시)·휠. Tab 홀드·G 버리기는 후속.
/// HUD 는 밀어넣기(push) 단방향: 슬롯 변경은 RefreshSlot, 손 이동은 PointHand 를 반드시 경유한다.
/// </summary>
public class PlayerInventory : MonoBehaviour
{
    /// <summary>맨손을 뜻하는 손 인덱스 값 (0-2 용어: 맨손).</summary>
    public const int BareHandIndex = -1;

    [Header("Slots")]
    [SerializeField] private int inventorySlots = 3; // 3-10 inventory_slots ⚪ — 상한이 아니라 시작값. 상수로 박지 말 것(6-10)

    [Header("Hand")]
    [SerializeField] private float handSwapSeconds = 0.3f; // 3-10 hand_swap_sec ⚪ — 손 전환 딜레이(E6)

    [Header("Input")]
    [SerializeField] private InputReader inputReader; // 프로젝트 에셋(SO) — 자기완결 프리팹의 허용 의존

    [Header("HUD")]
    [SerializeField] private UIInventory ui; // 같은 프리팹의 Canvas-Inventory. 인벤은 UI 를 밀어넣기만 하고 역참조는 받지 않는다

    [Header("Drop")]
    [SerializeField] private LayerMask groundMask; // 드롭 위치 접지 판정 대상 레이어. 인스펙터에서 Ground 만 선택한다
    [SerializeField] private float groundProbeHeight = 2f; // 바닥 탐색 SphereCast/Raycast 시작 높이(플레이어 발밑 기준 위로)
    [SerializeField] private float dropForwardOffset = 0.8f; // 플레이어 정면으로 띄우는 거리 — 자기 콜라이더 안에 스폰되지 않도록

    private InventorySlot[] slots;
    private int heldIndex = BareHandIndex;

    // 손 전환 딜레이의 남은 시간(초). 0 보다 크면 전환 중이다(IsSwapping).
    private float swapTimer;

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

    public bool IsSwapping { get; private set; } // 손 전환 딜레이(hand_swap_sec) 진행 중 — E·좌클릭 게이트. 슬롯 입력은 차단하지 않는다(즉시 반영 + 타이머 리셋)

    /// <summary>슬롯 배열을 인스펙터 크기로 생성한다.</summary>
    private void Awake()
    {
        slots = new InventorySlot[inventorySlots];
    }

    /// <summary>입력 이벤트 구독을 시작한다. 숫자 키 → 슬롯 선택, 휠 → 손 순환.</summary>
    private void OnEnable()
    {
        inputReader.EquipSlotEvent += EquipSlot;
        inputReader.CycleHandEvent += CycleHand;
        inputReader.DropEvent += DropHeld;
    }

    /// <summary>입력 이벤트 구독을 해제한다.</summary>
    private void OnDisable()
    {
        inputReader.EquipSlotEvent -= EquipSlot;
        inputReader.CycleHandEvent -= CycleHand;
        inputReader.DropEvent -= DropHeld;
    }

    /// <summary>
    /// HUD 슬롯 뷰를 슬롯 수만큼 짓는다. Awake 에서 slots 배열이 만들어진 뒤여야 하므로 Start 다.
    /// UI 가 PlayerInventory 를 역참조해 스스로 칸 수를 읽으면 Awake/Start 순서에 의존하게 된다 — 그래서 이쪽에서 민다.
    /// </summary>
    private void Start()
    {
        ui.Build(SlotCount);
    }

    /// <summary>손 전환 타이머를 갱신한다.</summary>
    private void Update()
    {
        // 매 프레임 호출되므로 진입 트레이스를 두지 않는다.
        if (!IsSwapping) return;

        swapTimer -= Time.deltaTime;
        if (swapTimer > 0f) return;

        swapTimer = 0f;
        IsSwapping = false;
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
    /// 아이템을 빈 슬롯 1칸에 넣고 그 칸을 바로 손에 든다 (3-5 회수 규칙 — 전 아이템 공통).
    /// 자동 장착 — 4-2 "회수 시 자동 장착 금지"는 사용자 확정으로 폐기(2026-07-16). 전환 딜레이는 걸지 않는다.
    /// </summary>
    /// <param name="item">획득할 아이템 데이터.</param>
    /// <param name="pairId">페어 번호(NN). 열쇠 외에는 0.</param>
    /// <returns>획득 성공 여부. 만재면 false(E8: 가방이 가득 찼습니다).</returns>
    public bool TryAdd(ItemDataSO item, int pairId)
    {
        Log.D($"[PlayerInventory] TryAdd {item.NameKey} pairId={pairId}");

        int index = FindEmptySlotIndex();
        if (index < 0) return false;

        slots[index].Data = item;
        slots[index].PairId = pairId;
        RefreshSlot(index);

        PointHand(index); // 주운 즉시 손에 든다 — 바로 사용 가능해야 하므로 전환 딜레이 없음
        return true;
    }

    /// <summary>한 칸의 현재 상태를 HUD 에 반영한다. 슬롯 내용이 바뀐 곳에서만 호출한다(획득·버리기·소비).</summary>
    /// <param name="index">갱신할 슬롯 인덱스(0 .. SlotCount-1).</param>
    private void RefreshSlot(int index)
    {
        ui.SetSlot(index, slots[index]);
    }

    /// <summary>
    /// 손 포인터를 옮기고 HUD 하이라이트를 따라 옮긴다.
    /// heldIndex 를 고치는 모든 경로(EquipSlot·CycleHand·StowHand·자동 맨손)는 이 헬퍼를 경유해야 화면이 따라온다.
    /// </summary>
    /// <param name="index">새 손 인덱스. <see cref="BareHandIndex"/> 면 맨손(하이라이트 전부 off).</param>
    private void PointHand(int index)
    {
        heldIndex = index;
        ui.SetHeldIndex(index);
    }

    /// <summary>
    /// 지정 슬롯을 손에 든다 (숫자 키 — 임시로 Tab 홀드 없이 단독 입력, Tab 게이팅은 후속).
    /// 빈 슬롯을 선택해도 포인터는 그 칸으로 감
    /// 전환 중 입력도 즉시 반영 — 포인터가 바로 움직이고 딜레이는 최근 입력 기준으로 재시작
    /// </summary>
    /// <param name="index">손에 들 슬롯 인덱스(0 .. SlotCount-1).</param>
    private void EquipSlot(int index)
    {
        Log.D($"[PlayerInventory] EquipSlot {index}");

        // 숫자 키는 슬롯 수와 무관하게 1..5 가 들어온다. 없는 칸이면 무시한다(3칸일 때 4·5 키).
        if (index < 0 || index >= SlotCount) return;

        if (index == heldIndex) return; // 이미 든 칸을 다시 눌러 스스로에게 전환 딜레이를 걸지 않는다

        PointHand(index);
        StartSwap();
    }

    /// <summary>
    /// 손에 든 것을 슬롯에 되돌리고 맨손이 된다 — 집어넣기 (Tab 홀드 후 숫자 없이 뗌).
    /// 아이템은 슬롯에 그대로 남는다(0-2: 집어넣기 ≠ 버리기).
    /// </summary>
    private void StowHand()
    {
        Log.D("[PlayerInventory] StowHand");

        PointHand(BareHandIndex);
    }

    /// <summary>손 전환 딜레이(hand_swap_sec)를 시작한다. 이미 전환 중이면 타이머를 다시 채운다. 손 포인터가 실제로 움직인 경로에서만 호출한다.</summary>
    private void StartSwap()
    {
        swapTimer = handSwapSeconds;
        IsSwapping = swapTimer > 0f; // 딜레이를 0 으로 튜닝하면 전환 중 상태 자체가 생기지 않는다
    }

    /// <summary>
    /// 손 슬롯을 순환한다 (마우스 휠): 맨손 → 1 → 2 → 3 → 맨손 (2장 키맵).
    /// 빈 칸을 건너뛰지 않는다 — 빈 칸에 멈추고 그동안은 맨손 취급이다(기획 확정 2026-07-11).
    /// 전환 중 입력도 즉시 반영 — 연속 스크롤 시 포인터가 바로바로 따라가고 딜레이만 최근 입력 기준으로 재시작
    /// </summary>
    /// <param name="direction">휠 방향. +1 = 정방향, -1 = 역방향.</param>
    private void CycleHand(int direction)
    {
        Log.D($"[PlayerInventory] CycleHand {direction}");

        // 맨손(-1) 을 포함한 SlotCount + 1 개의 위치를 순환한다. -1 을 0 으로 밀어 모듈러를 태우고 되돌린다.
        int positionCount = SlotCount + 1;
        int position = heldIndex + 1;
        int next = (position + direction + positionCount) % positionCount; // direction 이 음수여도 음수 나머지가 나오지 않게 한 바퀴 더한다

        PointHand(next - 1);
        StartSwap();
    }

    /// <summary>
    /// 손에 든 아이템을 월드에 떨군다 — 버리기 (G). WorldPrefab 을 스폰하고 슬롯을 비운 뒤
    /// 자동 맨손이 된다. 맨손이면 아무것도 하지 않는다.
    /// </summary>
    private void DropHeld()
    {
        if (IsBareHanded) return;

        int index = heldIndex;
        ItemDataSO data = slots[index].Data;
        int pairId = slots[index].PairId;

        SpawnDroppedItem(data, pairId);

        slots[index].Clear();
        RefreshSlot(index);
        PointHand(BareHandIndex);
    }

    /// <summary>
    /// 아이템의 WorldPrefab 을 플레이어 정면 지면 위치에 스폰한다.
    /// 콜라이더 바운즈로 접지 높이를 보정해 공중에 뜨거나 바닥에 파묻히지 않게 한다.
    /// Initialize 는 <see cref="ItemPickupInteractable"/>(Tap형) 에만 있다 — <see cref="OilPickupInteractable"/>(Hold형)
    /// 는 페어 번호가 필요 없고 프리팹 자체에 itemData 가 이미 직렬화돼 있어 주입이 불필요하다.
    /// </summary>
    /// <param name="data">스폰할 아이템 데이터.</param>
    /// <param name="pairId">승계할 페어 번호(NN). 열쇠 외에는 0.</param>
    private void SpawnDroppedItem(ItemDataSO data, int pairId)
    {
        Vector3 dropXZ = transform.position + transform.forward * dropForwardOffset;
        Vector3 probeOrigin = dropXZ + Vector3.up * groundProbeHeight;

        Vector3 spawnPos = Physics.Raycast(probeOrigin, Vector3.down, out RaycastHit hit, groundProbeHeight * 2f, groundMask)
            ? hit.point
            : new Vector3(dropXZ.x, transform.position.y, dropXZ.z); // 바닥을 못 찾으면 플레이어 발밑 높이로 폴백(항상 접지 중이라 사실상 도달하지 않는다)

        GameObject instance = Instantiate(data.WorldPrefab, spawnPos, data.WorldPrefab.transform.rotation);

        // 콜라이더 중심이 피벗인 프리팹(예: Key)은 바닥 좌표에 그대로 놓으면 절반이 파묻힌다 — 바운즈 하단을 접지면에 맞춘다.
        Collider col = instance.GetComponentInChildren<Collider>();
        if (col != null)
        {
            instance.transform.position += Vector3.up * (spawnPos.y - col.bounds.min.y);
        }

        ItemPickupInteractable pickup = instance.GetComponent<ItemPickupInteractable>();
        if (pickup != null)
        {
            pickup.Initialize(data, pairId);
        }
    }

    /// <summary>
    /// 전 슬롯의 내용을 꺼내 반환하고 인벤을 완전히 비운다 — 사망 드롭 전용(D19: 손에 든 것 포함 전 아이템 드롭, 백신도 예외 없음).
    /// 스폰은 하지 않는다 — 어디에 무엇을 남길지는 호출자(<see cref="PlayerDeathDropper"/>)가 정한다.
    /// 반환 배열에는 비어 있던 칸이 포함되지 않으므로, 그대로 순회해 스폰하면 된다.
    /// </summary>
    /// <returns>비우기 직전의 슬롯 내용. 채워져 있던 칸만 담기며, 인벤이 비어 있었다면 길이 0 배열이다.</returns>
    public InventorySlot[] DrainAll()
    {
        Log.D("[PlayerInventory] DrainAll");

        // TODO(impl): 채워진 칸만 골라 결과 배열로 복사 → 각 칸 Clear + RefreshSlot → PointHand(BareHandIndex).
        //             InventorySlot 은 struct 라 복사본이 남으므로, 배열에 담은 뒤 원본을 비워도 안전하다.
        return System.Array.Empty<InventorySlot>();
    }

    /// <summary>
    /// 손에 든 아이템을 사용으로 소멸시킨다 — 자물쇠 개방 시 열쇠(3-6) · 투척 시 투척물(4-3).
    /// 슬롯이 비고 자동 맨손이 된다. 다음 슬롯을 자동 장착하지 않으며(4-2), 버리기와 달리 월드에 아무것도 남기지 않는다.
    /// 맨손이면 아무것도 하지 않는다.
    /// </summary>
    public void ConsumeHeld()
    {
        Log.D("[PlayerInventory] ConsumeHeld");

        if (IsBareHanded) return;

        // 비운 칸은 제자리에 남는다 — 뒤 칸을 당겨오는 재정렬이 아니다.
        int index = heldIndex;
        slots[index].Clear();
        RefreshSlot(index);

        PointHand(BareHandIndex);
    }
}
