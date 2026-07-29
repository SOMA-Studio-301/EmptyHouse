using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 로컬 플레이어의 무전기 보유 상태와 Dissonance Radio 방을 연결한다.
/// 무전기를 인벤토리에 하나라도 보유하면 Radio 방을 수신하고,
/// J를 누르는 동안에만 비공간(2D) 송신 채널을 연다.
/// 근접 보이스 채널은 별도 트리거가 계속 관리하므로 무전 중에도 유지된다.
/// </summary>
[RequireComponent(typeof(PlayerRadioSlot))]
public sealed class PlayerRadioVoiceController : NetworkBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private InputReader inputReader;
    [SerializeField] private PlayerRadioSlot radioSlot;

    private bool pushToTalkHeld;
    private bool subscribed;
    private readonly NetworkVariable<bool> isTransmitting = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    public bool HasRadio => radioSlot != null && radioSlot.IsFilled;
    public bool IsTransmitting => isTransmitting.Value;

    private void Awake()
    {
        if (radioSlot == null)
        {
            radioSlot = GetComponent<PlayerRadioSlot>();
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (!IsOwner)
        {
            enabled = false;
            return;
        }

        Subscribe();
        ReconcileRadioState();
    }

    public override void OnNetworkDespawn()
    {
        Unsubscribe();
        pushToTalkHeld = false;
        SetTransmitting(false);
        base.OnNetworkDespawn();
    }

    private void OnEnable()
    {
        if (IsSpawned && IsOwner)
        {
            Subscribe();
            ReconcileRadioState();
        }
    }

    private void OnDisable()
    {
        if (!IsOwner) return;

        Unsubscribe();
        pushToTalkHeld = false;
        SetTransmitting(false);
    }

    private void Update()
    {
        if (!IsOwner) return;

        // DissonanceSetup은 씬 오브젝트라 플레이어보다 늦게 초기화될 수 있다.
        // 준비 전에는 대기하고, 시작된 직후 보유/입력 상태를 다시 적용한다.
        ReconcileRadioState();
    }

    private void OnRadioPressed()
    {
        pushToTalkHeld = true;
        ReconcileRadioState();
    }

    private void OnRadioCanceled()
    {
        pushToTalkHeld = false;
        SetTransmitting(false);
    }

    private void Subscribe()
    {
        if (subscribed || inputReader == null || radioSlot == null) return;

        inputReader.RadioPressedEvent += OnRadioPressed;
        inputReader.RadioCanceledEvent += OnRadioCanceled;
        radioSlot.Changed += ReconcileRadioState;
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed) return;

        inputReader.RadioPressedEvent -= OnRadioPressed;
        inputReader.RadioCanceledEvent -= OnRadioCanceled;
        radioSlot.Changed -= ReconcileRadioState;
        subscribed = false;
    }

    private void ReconcileRadioState()
    {
        if (!IsOwner) return;

        if (!HasRadio)
        {
            pushToTalkHeld = false;
            SetTransmitting(false);
            return;
        }

        if (pushToTalkHeld && !IsTransmitting)
        {
            SetTransmitting(true);
            Debug.Log($"[RadioVoice] PTT started owner={OwnerClientId}");
        }
        else if (!pushToTalkHeld && IsTransmitting)
        {
            SetTransmitting(false);
        }
    }

    private void SetTransmitting(bool value)
    {
        if (IsSpawned && IsOwner && isTransmitting.Value != value)
        {
            isTransmitting.Value = value;
            if (!value)
            {
                Debug.Log($"[RadioVoice] PTT stopped owner={OwnerClientId}");
            }
        }
    }

}
