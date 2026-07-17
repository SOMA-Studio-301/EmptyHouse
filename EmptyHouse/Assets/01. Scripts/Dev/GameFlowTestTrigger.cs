using System.Collections.Generic;
using Border.Core;
using NaughtyAttributes;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// [테스트 전용] 세션 흐름(GameOver/Settlement · 8초 자동 복귀)을 편집기 단일 호스트에서 수동 검증하는 트리거.
/// 실제 사망/귀환/포기·백신 시스템이 없으므로 가짜 clientId 로스터를 만들어 신호를 대신 발화한다.
/// 서버(호스트)에서만 의미가 있다 — '호스트 시작'을 제외한 버튼은 IsServer 아니면 무시한다.
/// 프로덕션 빌드에 넣지 말 것. 진짜 신호 배선(사망/귀환/포기 발행)은 네트워크 레이어(오규빈) 몫이다.
/// </summary>
public class GameFlowTestTrigger : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private ServerGameManager server;
    [SerializeField] private PlayerLifecycleEventChannelSO playerLifecycle;

    [Header("Fake Players")]
    [SerializeField] private int fakePlayerCount = 2;

    private const ulong FakeIdBase = 9001;
    private readonly List<ulong> registered = new List<ulong>();
    private readonly HashSet<ulong> stillActive = new HashSet<ulong>();

    /// <summary>편집기 단일 프로세스로 호스트를 띄운다 — 씬 NetworkObject(ServerGameManager) 스폰용.</summary>
    [Button("1) 호스트 시작")]
    private void StartHost()
    {
        if (NetworkManager.Singleton == null)
        {
            Log.W("[GameFlowTestTrigger] NetworkManager 가 씬에 없다.", this);
            return;
        }
        if (NetworkManager.Singleton.IsListening)
        {
            Log.D("[GameFlowTestTrigger] 이미 호스트/클라 실행 중");
            return;
        }
        NetworkManager.Singleton.StartHost();
        Log.D("[GameFlowTestTrigger] StartHost");
    }

    /// <summary>가짜 플레이어 fakePlayerCount 명을 로스터에 Active 로 등록한다(스포너와 같은 합류 채널 경로).</summary>
    [Button("2) 가짜 N명 등록")]
    private void RegisterFakePlayers()
    {
        if (!IsServerReady()) return;

        registered.Clear();
        stillActive.Clear();
        for (int i = 0; i < fakePlayerCount; i++)
        {
            ulong id = FakeIdBase + (ulong)i;
            playerLifecycle.RaiseJoined(id);
            registered.Add(id);
            stillActive.Add(id);
        }
        Log.D($"[GameFlowTestTrigger] 가짜 {registered.Count}명 등록");
    }

    /// <summary>전원 사망 → 활성 0(귀환자 없음) → GameOver 게임오버 결과창(#2).</summary>
    [Button("전원 사망 → GameOver")]
    private void KillAll()
    {
        if (!IsServerReady()) return;

        foreach (ulong id in registered) playerLifecycle.RaiseDied(id);
        stillActive.Clear();
    }

    /// <summary>전원 귀환 → 활성 0(귀환자 있음) → Settlement 정산 요약창(#3).</summary>
    [Button("전원 귀환 → Settlement")]
    private void ExtractAll()
    {
        if (!IsServerReady()) return;

        foreach (ulong id in registered) playerLifecycle.RaiseExtracted(id);
        stillActive.Clear();
    }

    /// <summary>활성 1명 사망(부분 종료·혼합 검증용).</summary>
    [Button("한 명 사망")]
    private void KillOne()
    {
        if (!IsServerReady()) return;
        if (!TryPopActive(out ulong id)) { Log.D("[GameFlowTestTrigger] 활성 플레이어 없음"); return; }

        playerLifecycle.RaiseDied(id);
    }

    /// <summary>활성 1명 귀환(부분 종료·혼합 검증용 — 마지막 1명 귀환 시 Settlement).</summary>
    [Button("한 명 귀환")]
    private void ExtractOne()
    {
        if (!IsServerReady()) return;
        if (!TryPopActive(out ulong id)) { Log.D("[GameFlowTestTrigger] 활성 플레이어 없음"); return; }

        playerLifecycle.RaiseExtracted(id);
    }

    /// <summary>백신 1개 수집(정산 요약 표시용).</summary>
    [Button("백신 수집")]
    private void CollectVaccine()
    {
        if (!IsServerReady()) return;

        server.NotifyVaccineCollected();
    }

    /// <summary>활성 집합에서 하나를 꺼내 반환한다.</summary>
    /// <param name="id">꺼낸 clientId. 없으면 0.</param>
    /// <returns>꺼냈으면 true, 활성이 없으면 false.</returns>
    private bool TryPopActive(out ulong id)
    {
        id = 0;
        if (stillActive.Count == 0) return false;

        foreach (ulong a in stillActive) { id = a; break; }
        stillActive.Remove(id);
        return true;
    }

    /// <summary>서버(호스트)에서 실행 중인지 확인한다. 아니면 경고 후 false.</summary>
    /// <returns>서버면 true.</returns>
    private bool IsServerReady()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer) return true;

        Log.W("[GameFlowTestTrigger] 서버(호스트)에서만 동작한다. 먼저 '1) 호스트 시작'.", this);
        return false;
    }
}
