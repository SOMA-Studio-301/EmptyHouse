using Border.Core;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 플레이어 위장 상태의 네트워크 권위자.
/// 소유 클라이언트는 V 토글을 서버에 요청하고, 서버만 상태와 게이지를 변경한다.
/// 위장 중 게이지는 초당 설정값만큼 감소하며 0이 되면 서버가 위장을 해제한다.
/// 위장 중 이동(자동 전진·감속)은 <see cref="PlayerController"/> 가 이 플래그를 읽어 처리한다.
/// </summary>
public sealed class PlayerDisguise : NetworkBehaviour
{
    [Header("Input")]
    [SerializeField] private InputReader inputReader;

    [Header("Event Channels")]
    [SerializeField] private DisguiseStateChangedEventChannelSO disguiseStateChanged;
    [SerializeField] private DisguiseRefillRequestedEventChannelSO disguiseRefillRequested;
    [SerializeField] private DisguiseGaugeChangedEventChannelSO disguiseGaugeChanged; // 발행: 잔량(0~1). 오너만 발행하며 씬 레벨 HUD 가 구독한다

    [Header("Gauge")]
    [SerializeField, Min(1f)] private float maxGauge = 100f;
    [SerializeField, Min(0f)] private float drainPerSecond = 10f;

    private readonly NetworkVariable<bool> isDisguised = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<float> currentGauge = new NetworkVariable<float>(
        100f,
        NetworkVariableReadPermission.Owner,
        NetworkVariableWritePermission.Server);

    private const double GaugeSyncIntervalSeconds = 0.1d;
    private double lastGaugeUpdateTime;

    public bool IsDisguised => isDisguised.Value;
    public float CurrentGauge => currentGauge.Value;
    public float Gauge01 => maxGauge <= 0f ? 0f : currentGauge.Value / maxGauge;
    public bool IsGaugeFull => currentGauge.Value >= maxGauge - 0.001f;

    /// <summary>위장 상태가 바뀔 때 발행된다. isDisguised.OnValueChanged 와 동일 시점에 전 클라이언트에서 발행된다(카메라·애니메이션 전환용).</summary>
    public event System.Action<bool> DisguiseChanged;

    public override void OnNetworkSpawn()
    {
        isDisguised.OnValueChanged += HandleDisguiseChanged;

        if (IsServer)
        {
            currentGauge.Value = maxGauge;
            lastGaugeUpdateTime = Time.timeAsDouble;

            if (disguiseRefillRequested == null)
            {
                Debug.LogError($"[{nameof(PlayerDisguise)}] Disguise refill channel is not assigned on {name}.", this);
            }
            else
            {
                disguiseRefillRequested.OnEventRaised += HandleRefillRequested;
            }
        }

        // 늦게 스폰된 구독자도 현재 복제값으로 초기화할 수 있도록 한 번 브릿지한다.
        PublishState(isDisguised.Value);

        if (IsOwner)
        {
            inputReader.DisguiseEvent += RequestToggle;

            currentGauge.OnValueChanged += HandleGaugeChanged;
            PublishGauge(currentGauge.Value);
        }
    }

    public override void OnNetworkDespawn()
    {
        isDisguised.OnValueChanged -= HandleDisguiseChanged;

        if (IsServer && disguiseRefillRequested != null)
            disguiseRefillRequested.OnEventRaised -= HandleRefillRequested;

        if (IsOwner)
        {
            inputReader.DisguiseEvent -= RequestToggle;

            currentGauge.OnValueChanged -= HandleGaugeChanged;
            PublishGauge(0f);
        }
    }

    private void Update()
    {
        if (!IsServer || !isDisguised.Value) return;

        double now = Time.timeAsDouble;
        if (now - lastGaugeUpdateTime < GaugeSyncIntervalSeconds) return;

        ServerDrainGauge(now);
    }

    /// <summary>
    /// V 토글 입력. 복제된 현재 상태의 반대를 목표 상태로 서버에 요청한다.
    /// "뒤집어라"가 아니라 "이 상태로 만들어라"를 보내는 이유는 멱등성이다 —
    /// 요청이 중복 도착하거나 게이지 고갈로 서버가 이미 해제한 뒤에 도착해도 결과가 어긋나지 않는다.
    /// isDisguised 는 Everyone 읽기라 소유자가 그대로 읽을 수 있고, 왕복 지연 동안 다시 누르면
    /// 아직 갱신되지 않은 값으로 같은 요청을 한 번 더 보내는 것이라 무해하다.
    /// </summary>
    private void RequestToggle()
    {
        if (!IsOwner || !IsSpawned) return;
        RequestStateRpc(!isDisguised.Value);
    }

    /// <summary>소유자의 요청을 받아 서버에서만 위장 유지 상태를 변경한다.</summary>
    [Rpc(SendTo.Server)]
    private void RequestStateRpc(bool requestedState)
    {
        if (!IsServer || !IsSpawned) return;

        if (!requestedState)
        {
            if (isDisguised.Value)
                ServerDrainGauge(Time.timeAsDouble);

            ServerSetDisguised(false);
            return;
        }

        if (currentGauge.Value <= 0f) return;

        lastGaugeUpdateTime = Time.timeAsDouble;
        ServerSetDisguised(true);
    }

    private void ServerDrainGauge(double now)
    {
        if (!IsServer || !isDisguised.Value) return;

        float elapsed = Mathf.Max(0f, (float)(now - lastGaugeUpdateTime));
        lastGaugeUpdateTime = now;
        currentGauge.Value = Mathf.Max(0f, currentGauge.Value - drainPerSecond * elapsed);

        if (currentGauge.Value <= 0f)
            ServerSetDisguised(false);
    }

    private void ServerSetDisguised(bool value)
    {
        if (isDisguised.Value == value) return;

        isDisguised.Value = value;
        Log.D($"[PlayerDisguise] Player {NetworkObjectId} disguise={value}, gauge={currentGauge.Value:F1}");
    }

    private void HandleRefillRequested(DisguiseRefillRequestedEvent evt)
    {
        if (!IsServer || evt.PlayerNetworkObjectId != NetworkObjectId) return;

        currentGauge.Value = maxGauge;
        lastGaugeUpdateTime = Time.timeAsDouble;
        Log.D($"[PlayerDisguise] Player {NetworkObjectId} gauge refilled to {maxGauge:F1}");
    }

    private void HandleDisguiseChanged(bool previous, bool current)
    {
        PublishState(current);
        DisguiseChanged?.Invoke(current);
    }

    /// <summary>복제된 잔량 변경을 HUD 채널로 중계한다(오너 전용 구독이라 남의 잔량은 여기로 오지 않는다).</summary>
    /// <param name="previous">직전 잔량.</param>
    /// <param name="current">변경된 잔량.</param>
    private void HandleGaugeChanged(float previous, float current)
    {
        // 위장 중 0.1초 주기로 호출되므로 진입 트레이스를 두지 않는다.
        PublishGauge(current);
    }

    /// <summary>잔량을 0~1 로 정규화해 HUD 채널에 발행한다.</summary>
    /// <param name="gauge">발행할 잔량(0~maxGauge).</param>
    private void PublishGauge(float gauge)
    {
        // 위장 중 0.1초 주기로 호출되므로 진입 트레이스를 두지 않는다.
        // maxGauge 는 [Min(1f)] 이지만, 0 으로 새면 슬라이더가 NaN 을 먹으므로 나눗셈 전에 막는다.
        disguiseGaugeChanged.RaiseEvent(maxGauge <= 0f ? 0f : gauge / maxGauge);
    }

    private void PublishState(bool value)
    {
        if (disguiseStateChanged == null)
        {
            Debug.LogError($"[{nameof(PlayerDisguise)}] Disguise state channel is not assigned on {name}.", this);
            return;
        }

        disguiseStateChanged.RaiseEvent(new DisguiseStateChangedEvent(NetworkObjectId, value));
    }
}
