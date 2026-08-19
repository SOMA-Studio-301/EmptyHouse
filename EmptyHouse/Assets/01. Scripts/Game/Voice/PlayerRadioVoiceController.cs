using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 로컬 플레이어의 무전기 보유 상태와 Dissonance Radio 방을 연결한다.
/// 무전기를 인벤토리에 하나라도 보유하면 Radio 방을 수신하고,
/// Space 를 누르는 동안에만 비공간(2D) 송신 채널을 연다.
/// 근접 보이스 채널은 별도 트리거가 계속 관리하므로 무전 중에도 유지된다.
/// 관전(사망 OR 귀환) 상태에서는 무전기가 죽는다 — 관전자 음성은 관전 전용 방으로만 나가야 하는데,
/// 무전은 그 방을 우회해 생존자에게 닿는 경로이기 때문이다(VoiceChatGlobalBridge).
/// </summary>
[RequireComponent(typeof(PlayerRadioSlot))]
public sealed class PlayerRadioVoiceController : NetworkBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private InputReader inputReader;
    [SerializeField] private PlayerRadioSlot radioSlot;

    [Header("Channels")]
    [SerializeField] private RadioHudStateEventChannelSO radioHudStateChanged; // 발행: 무전기 표시 상태. 오너만 발행하며 씬 레벨 HUD 아이콘이 구독한다

    private bool pushToTalkHeld;
    private bool subscribed;
    private RadioHudState publishedHudState = RadioHudState.None; // 마지막으로 발행한 표시 상태. Update 마다 도는 경로라 중복 발행을 여기서 걸러낸다
    private PlayerDeathHandler deathHandler; // 관전 판정용 형제 컴포넌트. 복제 상태라 원격 인스턴스에서도 읽힌다
    private PlayerReturn playerReturn;       // 관전 판정용 형제 컴포넌트. 위와 같음
    private readonly NetworkVariable<bool> isTransmitting = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    // 무전기가 실제로 동작하는가. 관전자는 슬롯이 차 있어도 송신·수신 대상에서 빠진다.
    // 원격 인스턴스에서도 평가되므로(RadioVoiceLeakRouter) 소유자 전용 상태에 기대지 않는다.
    public bool HasRadio => radioSlot != null && radioSlot.IsFilled && !IsSpectating;
    public bool IsTransmitting => isTransmitting.Value;

    /// <summary>사망·귀환 어느 쪽이든 비활성이면 관전으로 본다 — PlayerSpectatorController 와 같은 기준.</summary>
    private bool IsSpectating =>
        (deathHandler != null && deathHandler.IsDead.Value)
        || (playerReturn != null && playerReturn.HasExtracted.Value);

    private void Awake()
    {
        if (radioSlot == null)
        {
            radioSlot = GetComponent<PlayerRadioSlot>();
        }

        deathHandler = GetComponent<PlayerDeathHandler>();
        playerReturn = GetComponent<PlayerReturn>();
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
        PublishHudState(RadioHudState.None);
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
        PublishHudState(RadioHudState.None);
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
        PublishHudState();
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
            PublishHudState();
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

        PublishHudState();
    }

    /// <summary>현재 보유·송신 상태를 표시 상태로 번역해 발행한다.</summary>
    private void PublishHudState()
    {
        PublishHudState(!HasRadio
            ? RadioHudState.None
            : IsTransmitting ? RadioHudState.Transmitting : RadioHudState.Idle);
    }

    /// <summary>표시 상태를 HUD 채널에 방송한다. 값이 실제로 바뀐 호출에만 발행한다 — Update 마다 도는 경로다.</summary>
    /// <param name="state">발행할 무전기 표시 상태.</param>
    private void PublishHudState(RadioHudState state)
    {
        if (publishedHudState == state) return;

        publishedHudState = state;
        radioHudStateChanged.RaiseEvent(state);
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
