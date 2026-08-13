using System.Collections;
using System.Collections.Generic;
using Border.Core;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 세션 종료 판정의 서버 권위자. 플레이어 로스터(활성/귀환/사망/포기)를 들고,
/// 활성(생존&미귀환) 수가 0 이 되는 순간 결과를 확정한다 — 귀환자 0 → GameOver, 귀환자 ≥ 1 → Settlement
/// 결과 확정 후 8초 서버 타이머로 ReturningToLobby 로 자동 전이하며 로비 씬을 NGO 로 로드한다 — 버튼·호스트 조작·전원 확인이 아님(5-1).
/// 복귀는 재접속이 아니라 씬 스왑이다 — NGO 세션은 유지되고 전 클라가 같은 세션으로 로비 씬을 로드한다(4장).
/// GamePhase/GameResultReason 을 전 클라에 복제하고, 결과는 채널로만 방송한다(UI 미참조).
/// 사망/귀환/포기 신호는 PlayerLifecycleEventChannelSO 로 서버에서만 수신한다.
/// 결과 확정 시 로스터 전원 분의 개인 칭호를 ClientRpc 로 송신하고, 각 클라 수신부가 PlayerTitlesEventChannelSO 로 로컬 방송한다(EH-38 스텁).
/// </summary>
public class ServerGameManager : NetworkBehaviour
{
    /// <summary>로스터 엔트리의 세션 상태. 활성 = Active 하나뿐이며 나머지는 전부 비활성이다.</summary>
    private enum PlayerSessionStatus
    {
        Active,    // 생존·필드. 활성.
        Extracted, // 귀환(버스 복귀). 비활성. 인벤 보존.
        Dead,      // 사망. 비활성. 인벤 전량 드롭(D19).
        Left,      // 개인 포기(게임 나가기). 비활성. 드롭 준용(D19).
    }

    /// <summary>결과 표시 후 로비 복귀까지의 대기 시간(초). ⚪ 플레이테스트 튜닝값(5-1).</summary>
    [Header("Timing")]
    [SerializeField] private float resultDisplaySeconds = 8f;

    /// <summary>로비 복귀 시 로드할 씬 이름. NGO SceneManager 로 전 클라에 복제 로드된다(재접속 아님·씬 스왑, 4장). 빌드 세팅에 등록돼 있어야 한다.</summary>
    [Header("Scene")]
    [SerializeField] private string lobbySceneName = "Lobby";

    /// <summary>백신 달성 판정 기준 수(정산 요약 표시용, 종료 게이트 아님). MVP 1 / 정식 3 — 레벨디자인 3-4.</summary>
    [Header("Objective")]
    [SerializeField] private int requiredVaccines = 1;

    /// <summary>결과 확정 시 클라로 방송할 채널. 서버 매니저는 UI 를 직접 참조하지 않는다.</summary>
    [Header("Broadcast")]
    [SerializeField] private GameResultEventChannelSO gameResultChanged;

    [SerializeField] private PlayerTitlesEventChannelSO playerTitlesChanged; // 개인 칭호 로컬 방송 채널. ClientRpc 수신부가 각 클라에서 raise 한다

    /// <summary>사망/귀환/포기 신호 수신 채널. 서버에서만 구독한다.</summary>
    [Header("Signals")]
    [SerializeField] private PlayerLifecycleEventChannelSO playerLifecycle;

    // 세션 국면. 서버만 쓰고 전 클라에 복제된다.
    private readonly NetworkVariable<GamePhase> phase = new NetworkVariable<GamePhase>(GamePhase.InProgress);
    // 종료 결과. 어느 결과창을 띄울지 결정한다.
    private readonly NetworkVariable<GameResultReason> resultReason = new NetworkVariable<GameResultReason>(GameResultReason.None);
    // 서버 전용 — clientId별 세션 상태. GameScenePlayerSpawner 가 등록/해제한다.
    private readonly Dictionary<ulong, PlayerSessionStatus> roster = new Dictionary<ulong, PlayerSessionStatus>();
    // 서버 전용 — 수집된 백신 수(정산 요약 표시용).
    private int collectedVaccines;
    // 서버 전용 — 한 명이라도 등록된 적이 있는지. 시작 직후 빈 로스터의 종료 오발을 막는다.
    private bool hasRegisteredAny;

    /// <summary>결과 사유 방송 구독과, 서버 한정 생명주기 신호 구독을 건다.</summary>
    public override void OnNetworkSpawn()
    {
        resultReason.OnValueChanged += HandleResultReasonChanged;

        if (!IsServer) return;
        playerLifecycle.OnJoined += RegisterPlayer;
        playerLifecycle.OnDied += NotifyPlayerDied;
        playerLifecycle.OnExtracted += NotifyPlayerExtracted;
        playerLifecycle.OnLeft += NotifyPlayerLeft;
    }

    /// <summary>구독을 해제한다. OnNetworkSpawn 과 짝을 맞춘다.</summary>
    public override void OnNetworkDespawn()
    {
        resultReason.OnValueChanged -= HandleResultReasonChanged;

        if (!IsServer) return;
        playerLifecycle.OnJoined -= RegisterPlayer;
        playerLifecycle.OnDied -= NotifyPlayerDied;
        playerLifecycle.OnExtracted -= NotifyPlayerExtracted;
        playerLifecycle.OnLeft -= NotifyPlayerLeft;
    }

    /// <summary>
    /// 플레이어를 로스터에 Active 로 등록한다. PlayerLifecycle.OnJoined 로 서버에서만 호출된다(스포너가 스폰 직후 발화).
    /// 재합류 시 Left/Dead 였던 엔트리를 다시 Active 로 되돌린다.
    /// </summary>
    /// <param name="clientId">등록할 클라이언트 ID.</param>
    private void RegisterPlayer(ulong clientId)
    {
        Log.D($"[ServerGameManager] RegisterPlayer {clientId}");
        if (!IsServer) return;

        roster[clientId] = PlayerSessionStatus.Active;
        hasRegisteredAny = true;
        // 등록은 종료 판정을 부르지 않는다 — 활성이 늘기만 하므로 종료로 이어질 수 없다.
    }

    /// <summary>플레이어 사망을 로스터에 반영한다. PlayerLifecycle.OnDied 로 서버에서만 호출된다.</summary>
    /// <param name="clientId">사망한 클라이언트 ID.</param>
    private void NotifyPlayerDied(ulong clientId)
    {
        Log.D($"[ServerGameManager] NotifyPlayerDied {clientId}");
        SetStatus(clientId, PlayerSessionStatus.Dead);
    }

    /// <summary>플레이어 귀환(버스 복귀)을 로스터에 반영한다. PlayerLifecycle.OnExtracted 로 서버에서만 호출된다.</summary>
    /// <param name="clientId">귀환한 클라이언트 ID.</param>
    private void NotifyPlayerExtracted(ulong clientId)
    {
        Log.D($"[ServerGameManager] NotifyPlayerExtracted {clientId}");
        SetStatus(clientId, PlayerSessionStatus.Extracted);
    }

    /// <summary>플레이어 개인 포기를 로스터에 반영한다. PlayerLifecycle.OnLeft 로 서버에서만 호출된다.</summary>
    /// <param name="clientId">포기한 클라이언트 ID.</param>
    private void NotifyPlayerLeft(ulong clientId)
    {
        Log.D($"[ServerGameManager] NotifyPlayerLeft {clientId}");
        SetStatus(clientId, PlayerSessionStatus.Left);
    }

    /// <summary>백신 1개 수집을 반영한다(정산 요약 표시용, 종료 판정과 무관). 추후 ObjectiveEventChannelSO 로 대체된다.</summary>
    public void NotifyVaccineCollected()
    {
        Log.D("[ServerGameManager] NotifyVaccineCollected");
        if (!IsServer) return;

        collectedVaccines = Mathf.Min(collectedVaccines + 1, requiredVaccines);
    }

    /// <summary>로스터 엔트리 상태를 갱신하고 종료 조건을 재판정한다. 등록되지 않은 clientId 는 무시한다.</summary>
    /// <param name="clientId">대상 클라이언트 ID.</param>
    /// <param name="status">새 세션 상태.</param>
    private void SetStatus(ulong clientId, PlayerSessionStatus status)
    {
        if (!IsServer) return;
        if (!roster.ContainsKey(clientId))
        {
            // 배선이 정상이면 도달하지 않는다 — 찍히면 합류(OnJoined) 발화가 누락됐다는 뜻이라 경고로 남긴다.
            Log.W($"[ServerGameManager] SetStatus 무시 — 미등록 clientId {clientId} → {status} (roster {roster.Count}명, 활성 {CountActive()}명)", this);
            return;
        }

        roster[clientId] = status;
        // 테스트 로그: 탈출/사망 반영 직후의 로스터 현황.
        Log.D($"[ServerGameManager] SetStatus {clientId} → {status} · 활성 {CountActive()}명 / 전체 {roster.Count}명");
        EvaluateTerminalCondition();
    }

    /// <summary>로스터의 활성(Active) 인원 수를 센다.</summary>
    /// <returns>Active 상태 엔트리 수.</returns>
    private int CountActive()
    {
        int active = 0;
        foreach (PlayerSessionStatus status in roster.Values)
        {
            if (status == PlayerSessionStatus.Active) active++;
        }
        return active;
    }

    /// <summary>
    /// 활성(Active) 수가 0 이면 결과를 확정하고 Result 로 전이한 뒤 로비 복귀 카운트다운을 시작한다.
    /// 귀환자(Extracted) ≥ 1 → Settlement, 그 외 → GameOver(D35). 이미 종료됐거나 아무도 등록 전이면 무시(래치·오발 방지).
    /// </summary>
    private void EvaluateTerminalCondition()
    {
        Log.D("[ServerGameManager] EvaluateTerminalCondition");
        if (phase.Value != GamePhase.InProgress) return;
        if (!hasRegisteredAny) return;

        int active = 0;
        bool anyExtracted = false;
        foreach (PlayerSessionStatus status in roster.Values)
        {
            if (status == PlayerSessionStatus.Active) active++;
            else if (status == PlayerSessionStatus.Extracted) anyExtracted = true;
        }
        if (active > 0) return;

        GameResultReason reason = anyExtracted ? GameResultReason.Settlement : GameResultReason.GameOver;
        Log.D($"[ServerGameManager] 세션 종료 → {reason} (귀환자 있음={anyExtracted})");
        phase.Value = GamePhase.Result;
        resultReason.Value = reason;
        BroadcastTitles();
        BeginReturnToLobbyCountdown();
    }

    /// <summary>
    /// 로스터 전원 분의 개인 칭호를 확정해 전 클라로 송신한다. 결과 확정 직후 서버에서 1회 호출된다.
    /// 스텁 단계 — 전원 LazyTeammate 고정. 기획 확정 시 집계·배정 로직(TitleAssigner)으로 교체될 자리다.
    /// </summary>
    private void BroadcastTitles()
    {
        // Log.D("[ServerGameManager] BroadcastTitles");

        // 스텁 단계 — 로스터 전원 LazyTeammate 고정. 기획 확정 시 이 배정부가 집계 로직으로 교체된다.
        PlayerTitle[] titles = new PlayerTitle[roster.Count];
        int index = 0;
        foreach (ulong clientId in roster.Keys)
        {
            titles[index++] = new PlayerTitle { ClientId = clientId, Title = TitleId.LazyTeammate };
        }

        BroadcastTitlesClientRpc(titles);
    }

    /// <summary>서버가 확정한 칭호 배열을 수신해 각 클라 로컬 채널로 방송한다.</summary>
    /// <param name="titles">플레이어별 칭호. 로스터 전원 분.</param>
    [ClientRpc]
    private void BroadcastTitlesClientRpc(PlayerTitle[] titles)
    {
        // Log.D($"[ServerGameManager] BroadcastTitlesClientRpc {titles.Length}명");
        playerTitlesChanged.RaiseEvent(titles);
    }

    /// <summary>
    /// 결과 표시 후 resultDisplaySeconds(⚪) 뒤 phase 를 ReturningToLobby 로 전이하고 로비 씬을 로드하는 타이머를 시작한다 — 버튼이 아닌 서버 타이머(5-1).
    /// 씬 스왑은 NGO SceneManager 로 전 클라에 복제된다(로비 상주·재접속 아님, 4장).
    /// </summary>
    private void BeginReturnToLobbyCountdown()
    {
        Log.D("[ServerGameManager] BeginReturnToLobbyCountdown");
        if (!IsServer) return;

        StartCoroutine(ReturnToLobbyAfterDelay());
    }

    /// <summary>resultDisplaySeconds 대기 후 phase 를 ReturningToLobby 로 전이하고 로비 씬을 NGO 로 로드한다(전 클라 복제·재접속 아님).</summary>
    /// <returns>대기용 열거자.</returns>
    private IEnumerator ReturnToLobbyAfterDelay()
    {
        yield return new WaitForSeconds(resultDisplaySeconds);

        Log.D($"[ServerGameManager] → ReturningToLobby · 로비 씬 로드 {lobbySceneName}");
        phase.Value = GamePhase.ReturningToLobby;
        NetworkManager.SceneManager.LoadScene(lobbySceneName, LoadSceneMode.Single);
    }

    /// <summary>결과 사유 변화를 클라 채널로 방송한다(None 은 방송하지 않음). 전 클라에서 발화된다.</summary>
    /// <param name="previous">이전 사유.</param>
    /// <param name="current">새 사유.</param>
    private void HandleResultReasonChanged(GameResultReason previous, GameResultReason current)
    {
        Log.D($"[ServerGameManager] HandleResultReasonChanged {previous}->{current}");
        if (current == GameResultReason.None) return;
        gameResultChanged.RaiseEvent(current);
    }
}
