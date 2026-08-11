using System;
using Border.Core;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 손에 든 아이템의 네트워크 권위자 겸 표시 담당.
/// 손 포인터의 진실 소스인 <see cref="PlayerInventory"/> 는 소유자 전용 로컬 컴포넌트라 남의 화면에는
/// 아무것도 도달하지 않는다 — 그래서 소유자가 "지금 손에 든 아이템 ID"를 NetworkVariable 로 올리고,
/// 전 클라이언트가 각자 자기 복제 인스턴스의 손 소켓에 표시 모델을 붙인다(손전등과 같은 규칙).
/// 표시 모델은 NetworkObject 가 아니라 각자 로컬 Instantiate 다 — 상태가 없는 순수 표현물이므로 복제할 것이 없다.
///
/// 몇 손을 쓰는지(<see cref="ItemHandUsage"/>)도 이 복제값에서 파생되므로, 양손 점유로 손전등을 밀어내는
/// 판정(<see cref="PlayerFlashlight"/>)과 팔 IK(<see cref="PlayerHandArmIK"/>)가 전 클라이언트에서 같은 답을 본다.
/// </summary>
public sealed class PlayerHandItem : NetworkBehaviour
{
    [Header("Refs")]
    [SerializeField] private PlayerInventory inventory; // 같은 프리팹의 인벤토리. 소유자만 읽는다(원격 인스턴스에서는 비활성)
    [SerializeField] private ItemRegistrySO itemRegistry; // 프로젝트 에셋(SO) — 자기완결 프리팹의 허용 의존
    [SerializeField] private Transform itemSocket; // 왼손 뼈 아래 소켓. 한 손·양손 아이템 모두 이 아래에 붙는다

    private readonly NetworkVariable<int> heldItemId = new NetworkVariable<int>(
        ItemRegistrySO.NoneId,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    private GameObject visual; // 지금 소켓에 붙어 있는 표시 모델. 맨손이거나 HeldPrefab 이 없으면 null
    private ItemDataSO pushedItem; // 소유자가 마지막으로 올린 아이템. 레지스트리 누락 시 매 프레임 재시도·로그 도배를 막는다

    /// <summary>지금 손에 든 아이템. 맨손이면 null. 복제값에서 파생되므로 전 클라이언트가 같은 값을 본다.</summary>
    public ItemDataSO HeldItem { get; private set; }

    /// <summary>손에 무언가 들고 있는지 여부.</summary>
    public bool IsHolding => HeldItem != null;

    /// <summary>양손을 쓰는 아이템을 들고 있는지 여부. 손전등을 밀어내는 유일한 조건이다.</summary>
    public bool IsTwoHanded => HeldItem != null && HeldItem.HandUsage == ItemHandUsage.TwoHand;

    /// <summary>든 아이템이 바뀔 때마다 발생한다. 손전등이 실제 켜짐 상태를 다시 계산하는 신호다.</summary>
    public event Action Changed;

    /// <summary>복제 구독을 걸고 늦게 스폰된 인스턴스도 현재 복제값으로 초기화한다.</summary>
    public override void OnNetworkSpawn()
    {
        heldItemId.OnValueChanged += HandleHeldItemChanged;
        ApplyHeldItem(heldItemId.Value);
    }

    /// <summary>복제 구독을 해제하고 표시 모델을 치운다.</summary>
    public override void OnNetworkDespawn()
    {
        heldItemId.OnValueChanged -= HandleHeldItemChanged;

        HeldItem = null;
        pushedItem = null; // 재사용(풀링) 스폰에서 옛 값과 비교해 첫 갱신을 놓치지 않게 한다
        RebuildVisual();
    }

    /// <summary>
    /// 소유자의 손 포인터를 복제값에 반영한다.
    /// 인벤토리는 이벤트를 내보내지 않으므로(HUD 도 밀어넣기 단방향) 값 비교로 변화를 잡는다.
    /// </summary>
    private void Update()
    {
        // 매 프레임 호출되므로 진입 트레이스를 두지 않는다.
        if (!IsOwner || !IsSpawned) return;

        ItemDataSO current = inventory.HeldSlot.Data;
        if (current == pushedItem) return;

        pushedItem = current;
        heldItemId.Value = itemRegistry.GetId(current);
    }

    /// <summary>복제된 아이템 ID 변화를 각 클라이언트의 표현으로 반영한다.</summary>
    /// <param name="previous">이전 ID.</param>
    /// <param name="current">새 ID.</param>
    private void HandleHeldItemChanged(int previous, int current)
    {
        ApplyHeldItem(current);
    }

    /// <summary>아이템 ID 를 해석해 표시 모델을 갈아 끼우고 구독자에게 알린다.</summary>
    /// <param name="id">적용할 아이템 ID. <see cref="ItemRegistrySO.NoneId"/> 면 맨손이다.</param>
    private void ApplyHeldItem(int id)
    {
        HeldItem = itemRegistry.GetItem(id);
        Log.D($"[PlayerHandItem] Player {NetworkObjectId} hand={(HeldItem != null ? HeldItem.name : "bare")}");

        RebuildVisual();
        Changed?.Invoke();
    }

    /// <summary>
    /// 소켓의 표시 모델을 현재 아이템에 맞춘다.
    /// 손 기준 그립 오프셋은 <see cref="ItemDataSO.HeldPrefab"/> 루트의 Transform 을 그대로 쓴다 —
    /// 아이템마다 다른 쥐는 자세를 프리팹 쪽에서 잡게 해 소켓을 아이템 수만큼 만들지 않는다.
    /// </summary>
    private void RebuildVisual()
    {
        if (visual != null)
        {
            Destroy(visual);
            visual = null;
        }

        if (HeldItem == null || HeldItem.HeldPrefab == null) return;

        Transform grip = HeldItem.HeldPrefab.transform;
        visual = Instantiate(HeldItem.HeldPrefab, itemSocket);
        visual.transform.SetLocalPositionAndRotation(grip.localPosition, grip.localRotation);
        visual.transform.localScale = grip.localScale;
    }
}
