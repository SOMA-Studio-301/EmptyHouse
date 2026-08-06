using Border.Core;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 플레이어 손전등 온/오프 상태의 네트워크 권위자.
/// 소유 클라이언트가 F 토글을 서버에 요청하고, 서버가 NetworkVariable 로 상태를 변경하면
/// 모든 클라이언트가 자기 복제 인스턴스의 손전등 오브젝트를 켜고 끈다(오브젝트 사운드 규칙).
/// 라이트 연출은 flashlightObject 내부 몫이며 이 컴포넌트는 활성/비활성만 담당한다.
/// </summary>
public sealed class PlayerFlashlight : NetworkBehaviour
{
    [Header("Input")]
    [SerializeField] private InputReader inputReader; // F 토글 입력을 중계하는 SO. 소유자만 구독한다

    [Header("Refs")]
    [SerializeField] private GameObject flashlightObject; // 오른손 소켓 아래 손전등 GO. 상태에 따라 SetActive 토글

    [Header("Audio")]
    [SerializeField] private SFXEventChannelSO sfxEventChannel; // 원샷 SFX 발행 채널
    [SerializeField] private AudioId toggleAudioId = AudioId.None; // 온/오프 공용 토글음(3D 오브젝트 사운드)

    private readonly NetworkVariable<bool> isOn = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public bool IsOn => isOn.Value; // 현재 손전등 켜짐 상태. IK 등 표현 계층이 읽는다

    /// <summary>상태 변경 구독을 걸고 늦게 스폰된 인스턴스도 현재 복제값으로 초기화한다. 소유자는 입력을 추가 구독한다.</summary>
    public override void OnNetworkSpawn()
    {
        isOn.OnValueChanged += HandleStateChanged;
        ApplyState(isOn.Value, playSfx: false);

        if (IsOwner)
        {
            inputReader.FlashlightEvent += RequestToggle;
        }
    }

    /// <summary>상태 변경 구독을 해제한다. 소유자는 입력 구독도 해제한다.</summary>
    public override void OnNetworkDespawn()
    {
        isOn.OnValueChanged -= HandleStateChanged;

        if (IsOwner)
        {
            inputReader.FlashlightEvent -= RequestToggle;
        }
    }

    /// <summary>소유자의 F 입력을 서버 토글 요청으로 중계한다.</summary>
    private void RequestToggle()
    {
        if (!IsOwner || !IsSpawned) return;
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

    /// <summary>복제된 상태 변화를 각 클라이언트에서 시청각 표현으로 반영한다.</summary>
    /// <param name="previous">이전 상태.</param>
    /// <param name="current">새 상태.</param>
    private void HandleStateChanged(bool previous, bool current)
    {
        ApplyState(current, playSfx: true);
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
