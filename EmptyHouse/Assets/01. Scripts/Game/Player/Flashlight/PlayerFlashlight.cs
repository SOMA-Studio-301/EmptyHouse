using Border.Core;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 플레이어 손전등 온/오프 상태의 네트워크 권위자.
/// 소유 클라이언트가 F 토글을 서버에 요청하고, 서버가 NetworkVariable 로 상태를 변경하면
/// 모든 클라이언트가 자기 복제 인스턴스의 손전등 오브젝트를 켜고 끈다(오브젝트 사운드 규칙).
/// 라이트 연출은 flashlightObject 내부 몫이며 이 컴포넌트는 활성/비활성만 담당한다.
///
/// 손 충돌 규칙(양손 아이템): 복제되는 것은 "켜려는 의도"(<see cref="isOn"/>)뿐이고, 실제 켜짐은
/// 의도에서 양손 점유(<see cref="PlayerHandItem.IsTwoHanded"/>)를 뺀 값이다(<see cref="IsOn"/>).
/// 의도를 지운 채 끄는 게 아니라 가려 두기만 하므로, 양손 아이템을 내려놓으면 켜져 있던 손전등은
/// 저절로 돌아오고 꺼져 있던 손전등은 계속 꺼진 채로 남는다 — 별도의 복구 플래그가 필요 없다.
/// </summary>
public sealed class PlayerFlashlight : NetworkBehaviour
{
    [Header("Input")]
    [SerializeField] private InputReader inputReader; // F 토글 입력을 중계하는 SO. 소유자만 구독한다

    [Header("Refs")]
    [SerializeField] private GameObject flashlightObject; // 오른손 소켓 아래 손전등 GO. 상태에 따라 SetActive 토글
    [SerializeField] private PlayerHandItem handItem; // 같은 프리팹의 손 아이템 권위자. 양손 점유 여부를 읽는다

    [Header("Audio")]
    [SerializeField] private SFXEventChannelSO sfxEventChannel; // 원샷 SFX 발행 채널
    [SerializeField] private AudioId toggleAudioId = AudioId.None; // 온/오프 공용 토글음(3D 오브젝트 사운드)

    private readonly NetworkVariable<bool> isOn = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // 마지막으로 표현에 반영한 실제 켜짐 상태. 든 아이템이 한 손끼리 바뀌는 등
    // 실제 상태가 그대로인 변화에서 토글음이 새지 않게 하는 기준이다.
    private bool appliedOn;

    /// <summary>양손 아이템에 손을 빼앗겨 손전등을 들 수 없는 상태인지 여부.</summary>
    public bool IsBlocked => handItem.IsTwoHanded;

    /// <summary>실제 켜짐 상태(의도 ∧ 차단 없음). IK 등 표현 계층이 읽는다</summary>
    public bool IsOn => isOn.Value && !IsBlocked;

    /// <summary>상태 변경 구독을 걸고 늦게 스폰된 인스턴스도 현재 복제값으로 초기화한다. 소유자는 입력을 추가 구독한다.</summary>
    public override void OnNetworkSpawn()
    {
        isOn.OnValueChanged += HandleStateChanged;
        handItem.Changed += RefreshState;

        // 구독을 건 뒤 현재값을 직접 읽는다 — handItem 이 먼저 스폰돼 Changed 를 놓쳤어도 여기서 따라잡고,
        // 나중에 스폰되면 그때의 Changed 가 다시 맞춰 준다. 어느 순서로 스폰돼도 결과가 같다.
        appliedOn = IsOn;
        ApplyState(appliedOn, playSfx: false);

        if (IsOwner)
        {
            inputReader.FlashlightEvent += RequestToggle;
        }
    }

    /// <summary>상태 변경 구독을 해제한다. 소유자는 입력 구독도 해제한다.</summary>
    public override void OnNetworkDespawn()
    {
        isOn.OnValueChanged -= HandleStateChanged;
        handItem.Changed -= RefreshState;

        if (IsOwner)
        {
            inputReader.FlashlightEvent -= RequestToggle;
        }
    }

    /// <summary>
    /// 소유자의 F 입력을 서버 토글 요청으로 중계한다.
    /// 양손 아이템을 든 동안은 입력 자체를 삼킨다 — 의도를 뒤집어 두면 아이템을 내려놓는 순간
    /// 예상과 반대로 켜지거나 꺼져 버리기 때문이다(기획 확정: 양손 중 F 는 완전 무시).
    /// </summary>
    private void RequestToggle()
    {
        if (!IsOwner || !IsSpawned) return;
        if (IsBlocked) return;

        ToggleRpc();
    }

    /// <summary>서버에서만 손전등 상태를 반전한다.</summary>
    [Rpc(SendTo.Server)]
    private void ToggleRpc()
    {
        if (!IsServer || !IsSpawned) return;

        isOn.Value = !isOn.Value;
        Log.D($"[PlayerFlashlight] Player {NetworkObjectId} flashlight={isOn.Value}");
    }

    /// <summary>복제된 의도 변화를 각 클라이언트에서 시청각 표현으로 반영한다.</summary>
    /// <param name="previous">이전 의도.</param>
    /// <param name="current">새 의도.</param>
    private void HandleStateChanged(bool previous, bool current)
    {
        RefreshState();
    }

    /// <summary>
    /// 의도든 차단이든 무엇이 바뀌었든 실제 켜짐 상태를 다시 계산해 반영한다.
    /// 결과가 직전과 같으면 아무것도 하지 않는다 — 한 손 아이템끼리 바꿔 들 때 토글음이 나지 않게.
    /// </summary>
    private void RefreshState()
    {
        bool value = IsOn;
        if (value == appliedOn) return;

        appliedOn = value;
        ApplyState(value, playSfx: true);
    }

    /// <summary>손전등 GO 활성 상태를 맞추고, 상태 전환 시점이면 토글음을 재생한다.</summary>
    /// <param name="value">적용할 켜짐 상태.</param>
    /// <param name="playSfx">토글음 재생 여부. 스폰 초기화 시엔 false(늦게 합류한 클라이언트가 클릭음을 듣지 않게).</param>
    private void ApplyState(bool value, bool playSfx)
    {
        flashlightObject.SetActive(value);

        if (playSfx)
        {
            sfxEventChannel.RaisePlayEvent(toggleAudioId, flashlightObject.transform.position);
        }
    }
}
