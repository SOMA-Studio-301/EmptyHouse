using Dissonance;
using Dissonance.Integrations.Unity_NFGO;
using TMPro;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using Log = Border.Core.Log; // Dissonance.Log 와 이름이 겹쳐 별칭으로 고정한다

/// <summary>
/// 다른 플레이어 머리 위 월드 캔버스에 스팀 닉네임을 띄우고, 그 플레이어가 말하는 동안 마이크 아이콘을 켠다.
/// 닉네임은 <see cref="PlayerIdentity"/> 가 복제하는 값을 그대로 읽는다 — 스팀 조회는 소유자가 스폰 때 이미 끝냈다.
/// 발화 판정은 Dissonance 재생 진폭으로 한다. 이 프로젝트의 음성은 VoiceChatGlobalBridge 가 생존자 공용 방을
/// 상시 개방해 두는 구조라 IsSpeaking 은 살아있는 내내 참이고, 진폭만이 실제 발화를 가른다.
/// 원격 플레이어의 Dissonance 상태는 <see cref="NfgoPlayer.PlayerId"/> 로 찾는다 — 전 클라에 복제되는 값이라
/// 모든 클라이언트가 같은 키로 조회할 수 있다(RadioVoiceLeakRouter 와 같은 경로).
/// 소유자 본인 인스턴스에서는 캔버스를 끈다 — 1인칭이라 자기 이름표는 시야만 가린다.
/// 사망·귀환한 플레이어도 감춘다 — PlayerBodyVisibility 는 Renderer 만 끄는데 캔버스는 Renderer 가 아니라
/// 그대로 두면 사라진 몸 위에 이름만 떠 있게 된다.
/// </summary>
public sealed class UIPlayerNameplate : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private GameObject nameplateRoot; // 켜고 끌 월드 캔버스 오브젝트. 프리팹 내부 자식
    [SerializeField] private TextMeshProUGUI nameText; // 닉네임 라벨. 복제값을 그대로 넣는다
    [SerializeField] private GameObject micIcon;       // 발화 중에만 켜는 마이크 아이콘

    [Header("Visibility")]
    [Min(0f)] [SerializeField] private float maxVisibleDistance = 15f;     // 이 거리를 넘으면 캔버스를 끈다 — 8인 세션의 캔버스·TMP 비용을 잘라낸다
    [Min(0.02f)] [SerializeField] private float visibilityInterval = 0.2f; // 거리·생사 재평가 주기(초)

    [Header("Voice")]
    [Range(0f, 1f)] [SerializeField] private float speakingOnThreshold = 0.05f;  // 이 진폭을 넘으면 아이콘을 켠다 — PlayerVoiceNoise 와 같은 하한
    [Range(0f, 1f)] [SerializeField] private float speakingOffThreshold = 0.02f; // 이 진폭 아래로 내려가야 끈다. 켜짐 임계보다 낮아 히스테리시스가 된다
    [Min(0f)] [SerializeField] private float speakingHoldSeconds = 0.25f;        // 임계 미만이어도 유지할 시간 — 음절 사이 공백에 아이콘이 깜빡이지 않게 한다
    [Min(0.02f)] [SerializeField] private float voicePollInterval = 0.05f;       // 진폭 폴링 주기(초). 매 프레임 읽을 필요가 없는 값이다

    private PlayerIdentity identity;         // 닉네임 원천 — 형제
    private NfgoPlayer voiceTracker;         // Dissonance PlayerId 원천 — 형제. 전 클라에 복제된다
    private PlayerDeathHandler deathHandler; // 사망 게이팅 — 형제
    private PlayerReturn playerReturn;       // 귀환 게이팅 — 형제
    private PlayerHiding hiding;             // 은신 게이팅 — 형제. 벽장 안 이름표가 밖으로 튀어나오는 것을 막는다

    private Canvas nameplateCanvas;              // 거리 토글 대상. SetActive 왕복은 TMP 메시를 통째로 다시 만들어 enabled 만 끈다
    private UIWorldBillboard nameplateBillboard; // 캔버스가 꺼진 동안 회전 계산까지 멈추려고 함께 토글한다

    private VoicePlayerState voiceState; // 이 플레이어의 Dissonance 상태. PlayerId 가 바뀌거나 끊기면 다시 찾는다
    private string resolvedVoiceId;      // voiceState 를 찾을 때 쓴 PlayerId — 재탐색 여부 판단용
    private Transform localCamera;       // 거리 계산 기준. 관전 중에도 Camera.main 이 그대로 기준이다

    private float visibilityTimer; // 다음 가시성 평가까지 남은 시간
    private float voiceTimer;      // 마지막 진폭 폴링 이후 누적 시간
    private float speakingHold;    // 남은 발화 유지 시간. 0 보다 크면 아이콘을 켠다
    private bool nameplateShown;   // 현재 캔버스 표시 상태 — 중복 토글 방지
    private bool micShown;         // 현재 아이콘 표시 상태 — 중복 SetActive 방지

    /// <summary>닉네임·음성·생사·은신 상태를 읽어올 형제 컴포넌트와 캔버스 자식을 캐시한다.</summary>
    private void Awake()
    {
        identity = GetComponent<PlayerIdentity>();
        voiceTracker = GetComponent<NfgoPlayer>();
        deathHandler = GetComponent<PlayerDeathHandler>();
        playerReturn = GetComponent<PlayerReturn>();
        hiding = GetComponent<PlayerHiding>();

        nameplateCanvas = nameplateRoot.GetComponent<Canvas>();
        nameplateBillboard = nameplateRoot.GetComponent<UIWorldBillboard>();
    }

    /// <summary>
    /// 소유자면 캔버스를 끄고 물러난다. 원격 인스턴스에서만 닉네임 복제를 구독하고 표시를 시작한다 —
    /// 스폰 시점엔 아직 비어 있을 수 있어 현재 값 1차 반영과 구독을 함께 건다.
    /// </summary>
    public override void OnNetworkSpawn()
    {
        Log.D($"[UIPlayerNameplate] Spawn — id {NetworkObjectId} / owner {IsOwner}");

        if (IsOwner)
        {
            nameplateRoot.SetActive(false);
            enabled = false;
            return;
        }

        nameplateShown = nameplateCanvas.enabled;
        micShown = micIcon.activeSelf;

        // 같은 프레임에 전원이 몰려 평가되지 않도록 첫 주기를 오브젝트마다 어긋나게 시작한다.
        visibilityTimer = visibilityInterval * (NetworkObjectId % 8) / 8f;

        identity.PlayerName.OnValueChanged += HandleNameChanged;
        ApplyName(identity.PlayerName.Value);

        EvaluateVisibility();
    }

    /// <summary>닉네임 구독을 해제한다. 소유자 인스턴스는 애초에 구독하지 않았으므로 건너뛴다.</summary>
    public override void OnNetworkDespawn()
    {
        if (IsOwner) return;

        identity.PlayerName.OnValueChanged -= HandleNameChanged;
    }

    /// <summary>가시성은 저주기로, 진폭은 그보다 잦게 본다. 캔버스가 꺼져 있으면 진폭도 읽지 않는다.</summary>
    private void Update()
    {
        visibilityTimer -= Time.deltaTime;
        if (visibilityTimer <= 0f)
        {
            visibilityTimer = visibilityInterval;
            EvaluateVisibility();
        }

        if (!nameplateShown) return;

        voiceTimer += Time.deltaTime;
        if (voiceTimer < voicePollInterval) return;

        float elapsed = voiceTimer;
        voiceTimer = 0f;

        RefreshVoiceState();
        EvaluateSpeaking(elapsed);
    }

    /// <summary>복제된 닉네임이 갱신되면 라벨에 반영한다.</summary>
    /// <param name="previous">이전 닉네임(미사용).</param>
    /// <param name="current">새 닉네임.</param>
    private void HandleNameChanged(FixedString128Bytes previous, FixedString128Bytes current)
    {
        ApplyName(current);
    }

    /// <summary>닉네임을 라벨에 넣는다. 빈 값이면 아직 소유자 제출이 도착하지 않은 것이라 그대로 비워 둔다.</summary>
    /// <param name="value">표시할 닉네임.</param>
    private void ApplyName(FixedString128Bytes value)
    {
        nameText.text = value.ToString();
    }

    /// <summary>
    /// 사망·귀환·은신 상태와 로컬 카메라 거리로 캔버스를 켜고 끈다.
    /// 은신을 함께 보는 이유는 벽장 안에 들어간 플레이어의 이름표가 벽장 밖으로 그대로 뚫고 나오기 때문이다.
    /// 카메라가 아직 없으면(씬 전환 중) 감춰 둔다 — 다음 주기에 다시 평가된다.
    /// </summary>
    private void EvaluateVisibility()
    {
        if (deathHandler.IsDead.Value || playerReturn.HasExtracted.Value || hiding.IsHidden)
        {
            SetNameplateShown(false);
            return;
        }

        if (localCamera == null)
        {
            Camera main = Camera.main;
            if (main == null)
            {
                SetNameplateShown(false);
                return;
            }

            localCamera = main.transform;
        }

        float sqrDistance = (localCamera.position - transform.position).sqrMagnitude;
        SetNameplateShown(sqrDistance <= maxVisibleDistance * maxVisibleDistance);
    }

    /// <summary>
    /// 이 플레이어의 Dissonance 상태를 확보한다. PlayerId 는 스폰 직후 비어 있다가 복제로 채워지고,
    /// 재접속하면 값이 바뀌므로 키가 달라졌거나 연결이 끊겼을 때만 다시 찾는다.
    /// </summary>
    private void RefreshVoiceState()
    {
        DissonanceComms comms = DissonanceComms.GetSingleton();
        if (comms == null || !comms.IsNetworkInitialized)
        {
            voiceState = null;
            resolvedVoiceId = null;
            return;
        }

        string voiceId = voiceTracker.PlayerId;
        if (string.IsNullOrEmpty(voiceId))
        {
            voiceState = null;
            resolvedVoiceId = null;
            return;
        }

        if (voiceState != null && voiceState.IsConnected && resolvedVoiceId == voiceId) return;

        voiceState = comms.FindPlayer(voiceId);
        resolvedVoiceId = voiceId;
    }

    /// <summary>
    /// 재생 진폭으로 발화 여부를 갱신한다. 켜짐·꺼짐 임계를 분리해 경계에서 떨리지 않게 하고,
    /// 임계 아래로 내려가도 유지 시간만큼은 켜 둬 음절 사이 공백에 깜빡이지 않게 한다.
    /// 진폭은 디코딩된 원본 샘플의 ARV 라 3D 거리 감쇠의 영향을 받지 않는다 — 임계값이 거리와 무관하게 성립한다.
    /// IsSpeaking 을 함께 보는 이유는 세션이 끝난 뒤 ARV 가 마지막 값에 멈춰 아이콘이 켜진 채 남는 경우를 막기 위해서다.
    /// </summary>
    /// <param name="deltaTime">직전 폴링 이후 흐른 시간.</param>
    private void EvaluateSpeaking(float deltaTime)
    {
        bool streaming = voiceState != null && voiceState.IsConnected && voiceState.IsSpeaking;
        float amplitude = streaming ? voiceState.Amplitude : 0f;

        if (amplitude >= speakingOnThreshold || (micShown && amplitude >= speakingOffThreshold))
        {
            speakingHold = speakingHoldSeconds;
        }
        else
        {
            speakingHold -= deltaTime;
        }

        SetMicShown(speakingHold > 0f);
    }

    /// <summary>
    /// 캔버스 표시를 토글한다. GameObject 를 껐다 켜면 Graphic 재등록과 TMP 메시 재생성이 매번 따라오므로
    /// 거리 경계를 오가는 동안에는 Canvas.enabled 만 건드린다. 감출 때는 발화 상태도 초기화해 잔상을 없앤다.
    /// </summary>
    /// <param name="shown">표시 여부.</param>
    private void SetNameplateShown(bool shown)
    {
        if (nameplateShown == shown) return;

        nameplateShown = shown;
        nameplateCanvas.enabled = shown;
        nameplateBillboard.enabled = shown;

        if (!shown)
        {
            speakingHold = 0f;
            voiceTimer = 0f;
            SetMicShown(false);
        }
    }

    /// <summary>마이크 아이콘 표시를 토글한다.</summary>
    /// <param name="shown">표시 여부.</param>
    private void SetMicShown(bool shown)
    {
        if (micShown == shown) return;

        micShown = shown;
        micIcon.SetActive(shown);
    }
}
