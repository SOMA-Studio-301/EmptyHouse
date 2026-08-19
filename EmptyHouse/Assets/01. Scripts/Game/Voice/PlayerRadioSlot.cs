using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 일반 인벤토리와 독립된 플레이어별 무전기 전용 슬롯.
/// 한 칸만 존재하며, 무전기 픽업 성공 시 비어 있음에서 채워짐으로 전환된다.
/// HUD 는 Canvas-Inventory 에 미리 배치된 <see cref="UIInventorySlot"/> 뷰를 재사용한다 — 인벤 칸과 같은 look 이 공짜로 따라온다.
/// 비오너 숨김은 PlayerController 가 인벤토리 Canvas 를 통째로 꺼서 해결하므로(d731bf6) 여기서는 신경 쓰지 않는다.
/// </summary>
public sealed class PlayerRadioSlot : NetworkBehaviour
{
    [Header("HUD")]
    [SerializeField] private UIInventorySlot slotView; // Canvas-Inventory 에 미리 배치된 슬롯 뷰. 채움 = 아이콘 표시, 손에 듦 = 선택 하이라이트
    [SerializeField] private ItemDataSO radioItemData; // SO_Item_Radio — 아이콘 표시용. PlayerInventory 의 동명 필드와 같은 에셋을 가리킨다

    private readonly NetworkVariable<bool> isFilled = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    public bool IsFilled => isFilled.Value;
    public event Action Changed;

    private bool isHeldInHand; // 손 포인터가 이 칸을 가리키는가. PlayerInventory.PointHand 가 밀어넣는다(밀어넣기 단방향)

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        isFilled.OnValueChanged += OnFilledChanged;

        if (!IsOwner) return;

        RefreshView();
    }

    public override void OnNetworkDespawn()
    {
        isFilled.OnValueChanged -= OnFilledChanged;
        base.OnNetworkDespawn();
    }

    /// <summary>전용 칸이 비어 있을 때만 무전기를 장착한다. 알림·뷰 갱신은 값 쓰기가 발화시키는 OnValueChanged 가 담당한다(Clear 와 동일 규칙 — 명시 Invoke 를 겹치면 2회 발화).</summary>
    public bool TryFill()
    {
        if (!IsOwner || IsFilled) return false;

        isFilled.Value = true;
        return true;
    }

    /// <summary>
    /// 전용 칸을 비운다 — G 드랍·사망 드롭 경로. 오너가 아니거나 이미 비었으면 무시한다.
    /// 알림은 값 쓰기가 동기 발화시키는 OnValueChanged 경유 1회만 나간다 — TryFill 처럼 명시 Invoke 를 겹치면
    /// 이벤트가 2회 발화해 토글·카운트형 구독자가 오동작한다.
    /// </summary>
    public void Clear()
    {
        if (!IsOwner || !IsFilled) return;

        isFilled.Value = false;
    }

    /// <summary>손 하이라이트를 켜고 끈다. <see cref="PlayerInventory"/> 의 손 포인터 이동(PointHand)만 호출한다.</summary>
    /// <param name="held">이 칸을 손에 들고 있는지 여부.</param>
    public void SetHeld(bool held)
    {
        if (isHeldInHand == held) return; // 멱등 — PointHand 는 모든 손 이동 경로에서 호출된다

        isHeldInHand = held;
        RefreshView();
    }

    private void OnFilledChanged(bool previousValue, bool newValue)
    {
        if (IsOwner)
        {
            RefreshView();
        }

        Changed?.Invoke();
    }

    /// <summary>현재 상태(빈/보유/손에 듦)를 슬롯 뷰에 반영한다 — 인벤 칸과 같은 문법(아이콘 표시 + 선택 하이라이트).</summary>
    private void RefreshView()
    {
        if (IsFilled)
        {
            slotView.SetItem(radioItemData);
        }
        else
        {
            slotView.SetEmpty();
        }

        slotView.SetHighlight(IsFilled && isHeldInHand);
    }
}
