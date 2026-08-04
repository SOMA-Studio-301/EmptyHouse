using Border.Core;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 플레이어 은신(벽장) 상태의 네트워크 권위자. <see cref="PlayerDisguise"/> 를 본떴으나 게이지가 없다.
/// 상태 전환의 트리거는 벽장(<see cref="WardrobeInteractable"/>)이며, 서버만 플래그를 바꾼다 —
/// 플레이어 본인이 아니라 벽장이 진입/탈출을 확정하므로 여기엔 입력 구독이 없다.
/// 좀비 인지 제외는 이 플래그를 읽는 좀비 AI 의 몫이다(조작상호작용UI.md 3-5-1: 이 문서 책임은 플래그 세팅·해제까지).
/// </summary>
public sealed class PlayerHiding : NetworkBehaviour
{
    [Header("Event Channels")]
    [SerializeField] private HidingStateChangedEventChannelSO hidingStateChanged; // 은신 상태 변경 알림 채널

    private readonly NetworkVariable<bool> isHidden = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public bool IsHidden => isHidden.Value; // 현재 은신 상태. 이동·시선 게이팅과 좀비 인지 제외가 조회한다

    /// <summary>은신 상태 변경 구독을 걸고, 늦은 구독자 동기화를 위해 현재값을 1회 브릿지한다.</summary>
    public override void OnNetworkSpawn()
    {
        Log.D($"[PlayerHiding] OnNetworkSpawn {NetworkObjectId}");

        isHidden.OnValueChanged += HandleHiddenChanged;
        // 늦게 스폰된 구독자도 현재 복제값으로 초기화할 수 있도록 한 번 브릿지한다.
        PublishState(isHidden.Value);
    }

    /// <summary>은신 상태 변경 구독을 해제한다.</summary>
    public override void OnNetworkDespawn()
    {
        Log.D($"[PlayerHiding] OnNetworkDespawn {NetworkObjectId}");

        isHidden.OnValueChanged -= HandleHiddenChanged;
    }

    /// <summary>
    /// 서버에서만 은신 플래그를 설정한다. 벽장이 진입/탈출을 확정한 뒤(자신의 ServerRpc 안에서) 호출한다.
    /// </summary>
    /// <param name="value">설정할 은신 상태.</param>
    public void ServerSetHidden(bool value)
    {
        Log.D($"[PlayerHiding] ServerSetHidden {value} on {NetworkObjectId}");

        if (!IsServer || isHidden.Value == value) return;

        isHidden.Value = value; // 복제·채널 발행은 OnValueChanged 경유
    }

    /// <summary>복제된 은신 상태 변경을 로컬 채널로 브릿지한다.</summary>
    /// <param name="previous">이전 값.</param>
    /// <param name="current">현재 값.</param>
    private void HandleHiddenChanged(bool previous, bool current)
    {
        Log.D($"[PlayerHiding] HandleHiddenChanged {previous}->{current} on {NetworkObjectId}");

        PublishState(current);
    }

    /// <summary>현재 은신 상태를 은신 채널로 발행한다. 늦은 구독자 초기 동기화에도 쓰인다.</summary>
    /// <param name="value">발행할 은신 상태.</param>
    private void PublishState(bool value)
    {
        Log.D($"[PlayerHiding] PublishState {value} on {NetworkObjectId}");

        hidingStateChanged.RaiseEvent(new HidingStateChangedEvent(NetworkObjectId, value));
    }
}
