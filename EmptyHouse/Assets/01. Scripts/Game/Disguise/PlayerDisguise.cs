using Border.Core;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 플레이어 위장 상태의 네트워크 권위자.
/// 소유 클라이언트는 V 누름/해제를 서버에 요청하고, 서버만 상태와 게이지를 변경한다.
/// 위장 중 게이지는 초당 설정값만큼 감소하며 0이 되면 서버가 위장을 해제한다.
/// </summary>
public sealed class PlayerDisguise : NetworkBehaviour
{
    [Header("Input")]
    [SerializeField] private InputReader inputReader;

    [Header("Event Channels")]
    [SerializeField] private DisguiseStateChangedEventChannelSO disguiseStateChanged;
    [SerializeField] private DisguiseRefillRequestedEventChannelSO disguiseRefillRequested;

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
            inputReader.DisguisePressedEvent += RequestEnter;
            inputReader.DisguiseCanceledEvent += RequestExit;
        }
    }

    public override void OnNetworkDespawn()
    {
        isDisguised.OnValueChanged -= HandleDisguiseChanged;

        if (IsServer && disguiseRefillRequested != null)
            disguiseRefillRequested.OnEventRaised -= HandleRefillRequested;

        if (IsOwner)
        {
            inputReader.DisguisePressedEvent -= RequestEnter;
            inputReader.DisguiseCanceledEvent -= RequestExit;
        }
    }

    private void Update()
    {
        if (!IsServer || !isDisguised.Value) return;

        double now = Time.timeAsDouble;
        if (now - lastGaugeUpdateTime < GaugeSyncIntervalSeconds) return;

        ServerDrainGauge(now);
    }

    private void RequestEnter()
    {
        RequestState(true);
    }

    private void RequestExit()
    {
        RequestState(false);
    }

    private void RequestState(bool requestedState)
    {
        if (!IsOwner || !IsSpawned) return;
        RequestStateRpc(requestedState);
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
