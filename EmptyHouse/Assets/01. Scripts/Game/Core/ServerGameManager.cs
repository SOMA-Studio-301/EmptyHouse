using System.Collections;
using System.Collections.Generic;
using Border.Core;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 세션 종료 판정의 서버 권위자(세션루프.md 3·5장). 플레이어 로스터(활성/귀환/사망/포기)를 들고,
/// 활성(생존 &amp;&amp; 미귀환) 수가 0 이 되는 순간 결과를 확정한다 — 귀환자 0 → GameOver, 귀환자 ≥ 1 → Settlement(D35).
/// 결과 확정 후 8초(⚪) 서버 타이머로 ReturningToLobby 로 자동 전이한다 — 버튼·호스트 조작·전원 확인이 아니다(5-1).
/// GamePhase/GameResultReason 을 전 클라에 복제하고, 결과는 채널로만 방송한다(UI 미참조).
/// 사망/귀환/포기 신호는 PlayerLifecycleEventChannelSO 로 서버에서만 수신한다.
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

    /// <summary>백신 달성 판정 기준 수(정산 요약 표시용, 종료 게이트 아님). MVP 1 / 정식 3 — 레벨디자인 3-4.</summary>
    [Header("Objective")]
    [SerializeField] private int requiredVaccines = 1;

    /// <summary>결과 확정 시 클라로 방송할 채널. 서버 매니저는 UI 를 직접 참조하지 않는다.</summary>
    [Header("Broadcast")]
    [SerializeField] private GameResultEventChannelSO gameResultChanged;

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
        playerLifecycle.OnDied += NotifyPlayerDied;
        playerLifecycle.OnExtracted += NotifyPlayerExtracted;
        playerLifecycle.OnLeft += NotifyPlayerLeft;
    }

    /// <summary>구독을 해제한다. OnNetworkSpawn 과 짝을 맞춘다.</summary>
    public override void OnNetworkDespawn()
    {
        resultReason.OnValueChanged -= HandleResultReasonChanged;

        if (!IsServer) return;
        playerLifecycle.OnDied -= NotifyPlayerDied;
        playerLifecycle.OnExtracted -= NotifyPlayerExtracted;
        playerLifecycle.OnLeft -= NotifyPlayerLeft;
    }

    /// <summary>플레이어를 로스터에 Active 로 등록한다. GameScenePlayerSpawner 가 스폰 직후 서버에서 호출한다.</summary>
    /// <param name="clientId">등록할 클라이언트 ID.</param>
    public void RegisterPlayer(ulong clientId)
    {
        Log.D($"[ServerGameManager] RegisterPlayer {clientId}");
        if (!IsServer) return;

        roster[clientId] = PlayerSessionStatus.Active;
        hasRegisteredAny = true;
        // 등록은 종료 판정을 부르지 않는다 — 활성이 늘기만 하므로 종료로 이어질 수 없다.
    }

    /// <summary>플레이어를 로스터에서 제거한다(연결 끊김) 후 종료 조건을 재판정한다.</summary>
    /// <param name="clientId">제거할 클라이언트 ID.</param>
    public void UnregisterPlayer(ulong clientId)
    {
        Log.D($"[ServerGameManager] UnregisterPlayer {clientId}");
        if (!IsServer) return;

        roster.Remove(clientId);
        EvaluateTerminalCondition();
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
        if (!roster.ContainsKey(clientId)) return;

        roster[clientId] = status;
        EvaluateTerminalCondition();
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
        BeginReturnToLobbyCountdown();
    }

    /// <summary>
    /// 결과 표시 후 resultDisplaySeconds(⚪) 뒤 phase 를 ReturningToLobby 로 전이하는 타이머를 시작한다 — 버튼이 아닌 서버 타이머(5-1).
    /// 실제 로비 씬 스왑(로비 상주·재접속 아님)은 네트워크 레이어가 phase 를 보고 처리한다(오규빈).
    /// </summary>
    private void BeginReturnToLobbyCountdown()
    {
        Log.D("[ServerGameManager] BeginReturnToLobbyCountdown");
        if (!IsServer) return;

        StartCoroutine(ReturnToLobbyAfterDelay());
    }

    /// <summary>resultDisplaySeconds 대기 후 phase 를 ReturningToLobby 로 전이한다.</summary>
    /// <returns>대기용 열거자.</returns>
    private IEnumerator ReturnToLobbyAfterDelay()
    {
        yield return new WaitForSeconds(resultDisplaySeconds);

        Log.D("[ServerGameManager] → ReturningToLobby");
        phase.Value = GamePhase.ReturningToLobby;
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
