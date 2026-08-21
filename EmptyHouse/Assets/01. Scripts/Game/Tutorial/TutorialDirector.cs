using System.Collections;
using Border.Core;
using EmptyHouse.NoiseSystem;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 튜토리얼 단계 식별자 (튜토리얼.md 4-1 상태 머신).
/// Completed 는 S2_4(위장 해제)까지 끝나 Lobby 복귀만 남은 종료 상태다.
/// </summary>
public enum TutorialStageKind : byte
{
    S0,        // 인트로 — 세계관 대사 (커리큘럼 1·2)
    S1_1,      // 투척물 줍기
    S1_2,      // 던지기 → 착지
    S1_3,      // 좀비 B 조사 전이 관측
    S2_1,      // 사체 도포 (E 홀드)
    S2_2,      // 위장 진입 (V)
    S2_3,      // 철창 좀비 A 앞 통과 — 유일한 실패·리스폰 단계
    S2_4,      // 위장 해제 (V)
    Completed, // 전 단계 종료 — Lobby 복귀 대기
}

/// <summary>
/// 튜토리얼 씬 전용 진행 매니저 (튜토리얼.md 4·6장).
/// 단계 상태 머신을 굴리고, 성공/실패 판정은 전부 본편 시스템의 SO 채널 구독으로만 한다 —
/// 위장·소음·의심도에 개입하지 않는다(9장 4번 "매니저는 관찰만 한다").
/// 튜토리얼 씬은 솔로 호스트(항상 서버)라 NetworkBehaviour 가 아닌 서버 로컬 컴포넌트다.
/// </summary>
public sealed class TutorialDirector : MonoBehaviour
{
    [Header("Listening to")]
    [SerializeField] private DisguiseStateChangedEventChannelSO disguiseStateChanged;     // S2_2 진입 · S2_4 해제 판정
    [SerializeField] private DisguiseRefillRequestedEventChannelSO disguiseRefillRequested; // S2_1 도포 완료 판정(사체 홀드 완료 시 발행). 리스폰 재충전 발행에도 겸용(4-4-4)
    [SerializeField] private ZombieStateChangedEventChannelSO zombieStateChanged;         // S1_3 조사(Investigate) 전이 · S2_3 발각(Chase) 실패 판정
    [SerializeField] private NoiseEmittedEventChannelSO noiseEmitted;                     // S1_2 투척물 착지(임팩트 소음) 판정
    [SerializeField] private ZombiePlayerCaughtEventChannelSO playerCaught;               // S2_3 잡힘 실패 판정 보조

    [Header("Scene")]
    [SerializeField] private TutorialDialogue dialogue;           // 씬 대화창. 시퀀스 종료 이벤트로 S0 등 대사 단계를 판정한다
    [SerializeField] private TutorialRespawnPoint respawnPoint;   // S2_3 실패 리스폰 지점(통과 복도 시작 — 4-4-4)
    [SerializeField] private ZombieController zombieB;            // 씬 배치 좀비 B — S1_3 조사 전이 필터
    [SerializeField] private string lobbySceneName = "Lobby";     // 전 단계 클리어 후 복귀할 씬 (1-1). NGO Shutdown 후 로컬 로드

    public TutorialStageKind CurrentStage { get; private set; } = TutorialStageKind.S0; // 현재 진행 단계

    private PlayerController playerController; // 로컬 플레이어 컨트롤러 캐시 — 리스폰 텔레포트(SnapPoseClientRpc) 대상
    private PlayerHandItem playerHandItem;     // 로컬 플레이어 손 아이템 캐시 — S1_1 획득 판정(Changed) 구독 대상
    private PlayerDisguise playerDisguise;     // 로컬 플레이어 위장 캐시 — S2_2 진입 시 이미 위장 중인지 재평가용(상태 조회만, 개입 없음)
    private ulong playerNetworkObjectId;       // 로컬 플레이어 NetworkObjectId — 소음 발생원 자기 식별·재충전 발행에 쓴다
    private bool ignoreSelfRefill;             // 리스폰 재충전 자기 발행 무시 플래그(4-4-4). RaiseEvent 가 동기 호출이라 발행 전 set·직후 해제로 충분하다

    // 성공 조건 래치 — 대화창이 비차단(3-1)이라 플레이어가 담당 단계 앞에서 조건을 미리 충족시킬 수 있다(예: S0 중 병 투척).
    // 이벤트는 한 번 흘러가면 끝이므로(씬 유일 투척물 소멸 등) 단계 밖 충족을 기록해 두고 단계 진입 시 재평가한다 —
    // 4-1 "조건을 만족할 때까지 대기"가 좌초(영구 불충족)로 변질되는 것을 막는다. 시스템 개입이 아니라 내부 기록이다(9장 4).
    private bool hasHeldThrowable;      // 손이 투척물을 든 적 있음 — S1_1 성공 조건 래치
    private bool hasObservedImpact;     // 플레이어 외 발생원의 단발 소음(착지 임팩트) 관측됨 — S1_2 성공 조건 래치
    private bool hasZombieBInvestigated; // 좀비 B 가 조사로 전이한 적 있음 — S1_3 성공 조건 래치
    private bool hasRefillObserved;     // 사체 도포 충전이 발행된 적 있음(자기 발행 제외) — S2_1 성공 조건 래치

    /// <summary>채널·대화창 구독을 걸고 S0 대사를 시작한다.</summary>
    private void OnEnable()
    {
        Log.D("[TutorialDirector] OnEnable");

        disguiseStateChanged.OnEventRaised += HandleDisguiseStateChanged;
        disguiseRefillRequested.OnEventRaised += HandleRefillRequested;
        zombieStateChanged.OnEventRaised += HandleZombieStateChanged;
        noiseEmitted.OnEventRaised += HandleNoiseEmitted;
        playerCaught.OnEventRaised += HandlePlayerCaught;
        dialogue.SequenceCompleted += HandleDialogueCompleted;

        StartCoroutine(DiscoverLocalPlayer());

        dialogue.Play(TutorialStageKind.S0);
    }

    /// <summary>OnEnable 과 짝을 맞춰 구독을 전부 해제한다. SO 채널에 죽은 델리게이트가 남지 않게 한다.</summary>
    private void OnDisable()
    {
        Log.D("[TutorialDirector] OnDisable");

        disguiseStateChanged.OnEventRaised -= HandleDisguiseStateChanged;
        disguiseRefillRequested.OnEventRaised -= HandleRefillRequested;
        zombieStateChanged.OnEventRaised -= HandleZombieStateChanged;
        noiseEmitted.OnEventRaised -= HandleNoiseEmitted;
        playerCaught.OnEventRaised -= HandlePlayerCaught;
        dialogue.SequenceCompleted -= HandleDialogueCompleted;

        if (playerHandItem != null) playerHandItem.Changed -= HandleThrowableAcquired; // 발견 코루틴 완료 전 비활성화 대비 — 늦게 채워지는 의도적 선택 의존이라 가드가 정당하다(§4)
        StopAllCoroutines();
    }

    /// <summary>로컬 플레이어 스폰을 기다려 컨트롤러·손 아이템·NetworkObjectId 를 캐시하고 S1_1 획득 판정(Changed)을 배선한다.</summary>
    /// <returns>스폰 대기 코루틴.</returns>
    private IEnumerator DiscoverLocalPlayer()
    {
        while (NetworkManager.Singleton == null
               || !NetworkManager.Singleton.IsListening
               || NetworkManager.Singleton.LocalClient?.PlayerObject == null)
        {
            yield return null;
        }

        NetworkObject playerObject = NetworkManager.Singleton.LocalClient.PlayerObject;
        playerNetworkObjectId = playerObject.NetworkObjectId;
        playerController = playerObject.GetComponent<PlayerController>();
        playerHandItem = playerObject.GetComponent<PlayerHandItem>();
        playerDisguise = playerObject.GetComponent<PlayerDisguise>();
        playerHandItem.Changed += HandleThrowableAcquired;
        Log.D($"[TutorialDirector] 로컬 플레이어 캐시 완료 id={playerNetworkObjectId}");
    }

    /// <summary>단계를 전이하고 해당 단계의 대사 시퀀스를 재생한다. 전이 순서는 튜토리얼.md 4-1 이 정본이다.</summary>
    /// <param name="next">전이할 다음 단계.</param>
    private void AdvanceTo(TutorialStageKind next)
    {
        Log.D($"[TutorialDirector] AdvanceTo {next}");

        CurrentStage = next;
        dialogue.Play(next);
        if (next == TutorialStageKind.Completed)
        {
            CompleteTutorial();
            return;
        }

        TryAdvanceFromLatchedState();
    }

    /// <summary>
    /// 단계 진입 시 이미 충족된 성공 조건(래치·현재 상태)을 재평가해 즉시 다음 단계로 넘긴다.
    /// 비차단 진행(3-1) 특성상 조건이 담당 단계보다 먼저 충족될 수 있어, 이 재평가가 없으면 상태 머신이 영구 대기로 좌초한다.
    /// 연쇄 충족이면 재귀적으로 끝까지 전진한다(중간 단계 대사는 이미 행동이 끝난 지시라 대체돼도 무방).
    /// </summary>
    private void TryAdvanceFromLatchedState()
    {
        switch (CurrentStage)
        {
            case TutorialStageKind.S1_1 when hasHeldThrowable:
                AdvanceTo(TutorialStageKind.S1_2);
                break;
            case TutorialStageKind.S1_2 when hasObservedImpact:
                AdvanceTo(TutorialStageKind.S1_3);
                break;
            case TutorialStageKind.S1_3 when hasZombieBInvestigated:
                AdvanceTo(TutorialStageKind.S2_1);
                break;
            case TutorialStageKind.S2_1 when hasRefillObserved:
                AdvanceTo(TutorialStageKind.S2_2);
                break;
            case TutorialStageKind.S2_2 when playerDisguise != null && playerDisguise.IsDisguised:
                AdvanceTo(TutorialStageKind.S2_3);
                break;
        }
    }

    /// <summary>통과선 트리거의 통과 보고 — S2_3 성공 판정. TutorialPassLineTrigger 가 호출한다.</summary>
    public void NotifyPassLineReached()
    {
        Log.D("[TutorialDirector] NotifyPassLineReached");

        if (CurrentStage != TutorialStageKind.S2_3) return;
        AdvanceTo(TutorialStageKind.S2_4);
    }

    /// <summary>대사 시퀀스 종료 처리. S0 처럼 대사 종료가 곧 성공 조건인 단계를 전진시킨다(4-2).</summary>
    /// <param name="stage">종료된 시퀀스의 단계.</param>
    private void HandleDialogueCompleted(TutorialStageKind stage)
    {
        Log.D($"[TutorialDirector] HandleDialogueCompleted {stage}");

        if (stage != TutorialStageKind.S0) return;
        AdvanceTo(TutorialStageKind.S1_1);
    }

    /// <summary>위장 상태 변경 판정 — 진입이면 S2_2 성공, 해제면 S2_4 성공 또는 S2_3 실패(4-4-1 ①)다.</summary>
    /// <param name="evt">위장 상태 페이로드.</param>
    private void HandleDisguiseStateChanged(DisguiseStateChangedEvent evt)
    {
        Log.D($"[TutorialDirector] HandleDisguiseStateChanged disguised={evt.IsDisguised}");

        if (evt.IsDisguised)
        {
            if (CurrentStage == TutorialStageKind.S2_2) AdvanceTo(TutorialStageKind.S2_3);
            return;
        }

        if (CurrentStage == TutorialStageKind.S2_3)
        {
            // 자발적 해제만으로는 실패가 아니다(4-4-1: 실패 = 발각) — Chase 전이·잡힘 채널이 확정할 때까지 관찰만 한다.
            Log.D("[TutorialDirector] S2_3 중 위장 해제 — 발각 판정 대기");
            return;
        }

        if (CurrentStage == TutorialStageKind.S2_4) AdvanceTo(TutorialStageKind.Completed);
    }

    /// <summary>위장 재료 충전 판정 — S2_1(사체 도포 홀드 완료) 성공 조건이다.</summary>
    /// <param name="evt">충전 요청 페이로드.</param>
    private void HandleRefillRequested(DisguiseRefillRequestedEvent evt)
    {
        Log.D("[TutorialDirector] HandleRefillRequested");

        if (ignoreSelfRefill) return; // 리스폰 재충전 자기 발행분(4-4-4) — 채널 발행이 동기라 플래그 구간 밖 수신은 전부 사체 도포다
        hasRefillObserved = true;
        if (CurrentStage != TutorialStageKind.S2_1) return;
        AdvanceTo(TutorialStageKind.S2_2);
    }

    /// <summary>좀비 상태 전이 판정 — Investigate 전이는 S1_3 성공, Chase 전이는 S2_3 실패(발각)다.</summary>
    /// <param name="evt">상태 전이 페이로드.</param>
    private void HandleZombieStateChanged(ZombieStateChangedEvent evt)
    {
        Log.D($"[TutorialDirector] HandleZombieStateChanged {evt.Previous}->{evt.Current}");

        if (evt.Current == ZombieStateKind.Investigate && evt.ZombieNetworkObjectId == zombieB.NetworkObjectId)
        {
            hasZombieBInvestigated = true;
            if (CurrentStage == TutorialStageKind.S1_3)
            {
                AdvanceTo(TutorialStageKind.S2_1);
                return;
            }
        }

        // S2_3 발각 실패 — 좀비를 특정하지 않는다(어느 좀비든 Chase 전이 = 발각).
        if (CurrentStage == TutorialStageKind.S2_3 && evt.Current == ZombieStateKind.Chase)
        {
            RespawnAtCorridorStart();
        }
    }

    /// <summary>소음 발행 관측 — S1_2(투척물 착지) 성공 판정이다. 임팩트 소음(발생원 = 착지점)을 식별한다.</summary>
    /// <param name="evt">발생 소음 페이로드.</param>
    private void HandleNoiseEmitted(NoiseEmittedEvent evt)
    {
        // 지속 소음(이동·음성·무전)은 0.1초 주기로 계속 흘러든다 — 트레이스가 그 주기로 찍히면 로그 폭주로
        // 에디터가 멈춘다(§4 관례: 0.1s 경로 로깅 금지). 판정 대상인 단발만 통과시킨 뒤 트레이스를 남긴다.
        if (evt.Channel != NoiseSourceChannel.OneShot) return;

        Log.D($"[TutorialDirector] HandleNoiseEmitted {evt.EmittedDb}dB");

        // 투척물은 자기 자신의 NetworkObjectId 로 발행한다(ThrownProjectile) — 픽업·사체 등 나머지 단발은 전부 플레이어 id 를 실으므로
        // "플레이어가 아닌 발생원의 단발" = 착지 임팩트다. dB 상수 비교는 금지(스펙 9장 1).
        if (playerNetworkObjectId == 0) return; // 플레이어 발견 전에는 자기 식별이 불가능하다(NGO id 는 1부터) — 판정 보류
        if (evt.SourceId == playerNetworkObjectId) return;
        hasObservedImpact = true;
        if (CurrentStage != TutorialStageKind.S1_2) return;
        AdvanceTo(TutorialStageKind.S1_3);
    }

    /// <summary>좀비에게 잡힘 — S2_3 실패 판정 보조. 튜토리얼에는 사망이 없으므로 리스폰으로만 처리한다.</summary>
    /// <param name="evt">잡힘 페이로드.</param>
    private void HandlePlayerCaught(ZombiePlayerCaughtEvent evt)
    {
        Log.D("[TutorialDirector] HandlePlayerCaught");

        if (CurrentStage != TutorialStageKind.S2_3) return;
        RespawnAtCorridorStart();
    }

    /// <summary>투척물 획득 판정 — S1_1 성공 조건. 채널 신설 대신 기존 PlayerHandItem.Changed 를 구독한다 — 픽업 즉시 자동 장착(조작UI 4-2)이라 "손이 투척물을 든 순간" = 획득이다.</summary>
    private void HandleThrowableAcquired()
    {
        Log.D("[TutorialDirector] HandleThrowableAcquired");

        if (playerHandItem.HeldItem == null || playerHandItem.HeldItem.Kind != ItemKind.Throwable) return;
        hasHeldThrowable = true;
        if (CurrentStage != TutorialStageKind.S1_1) return;
        AdvanceTo(TutorialStageKind.S1_2);
    }

    /// <summary>
    /// S2_3 실패 처리 — 플레이어를 통과 복도 시작(respawnPoint)으로 되돌리고 위장 재료를 다시 채운다(4-4-4).
    /// 도포(S2_1)부터 다시 시키지 않는다 — 실패 원인과 무관한 반복은 학습이 아니라 노동이다.
    /// </summary>
    private void RespawnAtCorridorStart()
    {
        Log.D("[TutorialDirector] RespawnAtCorridorStart");

        // 솔로 호스트 = 서버이므로 서버 전용 텔레포트 정본 경로를 그대로 쓴다 — yaw/pitch 재시드까지 포함(WardrobeInteractable 과 동일).
        playerController.SnapPoseClientRpc(respawnPoint.Position, respawnPoint.Rotation);

        // 위장 재료 재충전(4-4-4). RaiseEvent 는 동기 호출이라 발행 전 set → 직후 해제로 자기 발행분만 정확히 걸러진다.
        ignoreSelfRefill = true;
        disguiseRefillRequested.RaiseEvent(new DisguiseRefillRequestedEvent(playerNetworkObjectId));
        ignoreSelfRefill = false;

        dialogue.PlayFail(TutorialStageKind.S2_3);

        // 좀비 A 상태 복원(리스폰 후 Chase 지속 여부)은 기획 미결(핸드오프 §7) — 협의 확정 전까지 아무것도 하지 않는다.
    }

    /// <summary>전 단계 클리어 — NGO Shutdown 을 마친 뒤 Lobby 씬을 로컬 로드한다(1-1). SessionExitHandler 의 종료 순서를 따른다.</summary>
    private void CompleteTutorial()
    {
        Log.D("[TutorialDirector] CompleteTutorial");

        StartCoroutine(CompleteTutorialRoutine());
    }

    /// <summary>마무리 대사가 끝나기를 기다린 뒤 NGO 를 내리고 Lobby 씬을 로컬 로드한다.</summary>
    /// <returns>종료 시퀀스 코루틴.</returns>
    private IEnumerator CompleteTutorialRoutine()
    {
        // 마무리 대사 대기 — 스펙 4-4-3 "마무리 대사" AC. 대사가 읽히기 전에 씬을 내리면 AC 위반이다.
        while (dialogue.IsPlaying) yield return null;

        NetworkManager.Singleton.Shutdown();

        // SessionCoordinator.ShutdownNetworkAsync 와 같은 순서(UGS 제외) — 셧다운 완료를 50ms 간격, 최대 5초 폴링.
        var interval = new WaitForSeconds(0.05f);
        float elapsed = 0f;
        while ((NetworkManager.Singleton.IsListening || NetworkManager.Singleton.ShutdownInProgress) && elapsed < 5f)
        {
            yield return interval;
            elapsed += 0.05f;
        }

        SceneManager.LoadScene(lobbySceneName);
    }
}
