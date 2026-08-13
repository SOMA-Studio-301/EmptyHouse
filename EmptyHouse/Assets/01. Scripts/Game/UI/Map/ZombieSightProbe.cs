using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 시야 내 좀비 판정(EH-62) — 로컬 카메라 기준 순수 표시용 판정. **좀비 AI 인지와 완전히 무관하다.**
/// 판정: 뷰포트 안 + 최대 거리 이내 + Wall·Door 레이어 Linecast 비차폐. 깜빡임 방지로 시야 이탈 후
/// 잔상 유지 시간(⚪) 동안은 표시를 유지한다. 좀비 열거는 <see cref="ZombieRuntimeRegistrySO"/> — 씬 검색 금지.
/// </summary>
public sealed class ZombieSightProbe : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private ZombieRuntimeRegistrySO zombieRegistry; // 좀비 열거 원천(프로젝트 에셋)

    [Header("Sight")]
    [SerializeField] private float maxDistance = 30f; // 표시 최대 거리(m) ⚪
    [SerializeField] private LayerMask occlusionMask; // 차폐 레이어(Wall·Door) — 벽 뒤 좀비 미표시(요구 2)
    [SerializeField] private float lingerSeconds = 0.5f; // 시야 이탈 후 잔상 유지(s) ⚪ — 경계 깜빡임 방지
    [SerializeField] private float chestHeightMeters = 1.2f; // 차폐 Linecast 목표 높이(m) ⚪ — 발밑은 바닥에 먹혀 항상 가려진다

    private readonly Dictionary<ZombieController, float> lastSeenTime = new Dictionary<ZombieController, float>(); // 좀비 → 마지막 목격 시각(잔상 판정)
    private readonly List<ZombieController> pruneBuffer = new List<ZombieController>(); // 파괴된 키 정리용 재사용 버퍼(열거 중 제거 불가)
    private Camera cachedCamera; // 로컬 메인 카메라 캐시 — 관전 전환 시 교체되므로 파괴되면 다시 찾는다

    /// <summary>
    /// 현재 표시 대상 좀비의 월드 좌표를 수집한다(잔상 포함). 호출자가 준 버퍼를 채운다(GC 0).
    /// 카메라는 로컬 메인 카메라 — 관전 중에는 관전 시점이 그대로 기준이 된다.
    /// </summary>
    /// <param name="results">월드 좌표 수신 버퍼(호출자 소유 — 메서드가 Clear 후 채운다).</param>
    public void CollectVisible(List<Vector3> results)
    {
        // 매 프레임 호출되므로 진입 트레이스를 두지 않는다.
        results.Clear();

        if (cachedCamera == null)
        {
            cachedCamera = Camera.main; // 관전 전환·리스폰으로 카메라가 갈릴 때만 재탐색
        }

        Vector3 eye = cachedCamera.transform.position;
        float now = Time.time;
        IReadOnlyList<ZombieController> zombies = zombieRegistry.Zombies;
        for (int i = 0; i < zombies.Count; i++)
        {
            ZombieController zombie = zombies[i];
            if (zombie == null)
            {
                continue; // despawn 경합 — 레지스트리 해제 전 프레임
            }

            Vector3 chest = zombie.transform.position + Vector3.up * chestHeightMeters;
            if (IsInSight(eye, chest))
            {
                lastSeenTime[zombie] = now;
            }

            if (lastSeenTime.TryGetValue(zombie, out float seenAt) && now - seenAt <= lingerSeconds)
            {
                results.Add(chest);
            }
        }

        PruneDeadEntries(zombies.Count);
    }

    /// <summary>시야 판정 3단 — 뷰포트 안 · 최대 거리 이내 · 차폐 레이어 비관통.</summary>
    /// <param name="eye">카메라 월드 좌표.</param>
    /// <param name="target">좀비 흉부 월드 좌표.</param>
    /// <returns>표시 대상이면 true.</returns>
    private bool IsInSight(Vector3 eye, Vector3 target)
    {
        // 좀비 수만큼 매 프레임 호출되므로 진입 트레이스를 두지 않는다.
        Vector3 viewport = cachedCamera.WorldToViewportPoint(target);
        if (viewport.z <= 0f || viewport.x < 0f || viewport.x > 1f || viewport.y < 0f || viewport.y > 1f)
        {
            return false;
        }

        if ((target - eye).sqrMagnitude > maxDistance * maxDistance)
        {
            return false;
        }

        return !Physics.Linecast(eye, target, occlusionMask, QueryTriggerInteraction.Ignore);
    }

    /// <summary>파괴된 좀비가 남긴 목격 기록을 정리한다 — 레지스트리보다 기록이 많을 때만 훑는다.</summary>
    /// <param name="liveCount">현재 레지스트리 좀비 수.</param>
    private void PruneDeadEntries(int liveCount)
    {
        // 매 프레임 호출되므로 진입 트레이스를 두지 않는다.
        if (lastSeenTime.Count <= liveCount)
        {
            return;
        }

        pruneBuffer.Clear();
        foreach (KeyValuePair<ZombieController, float> entry in lastSeenTime)
        {
            if (entry.Key == null)
            {
                pruneBuffer.Add(entry.Key);
            }
        }

        for (int i = 0; i < pruneBuffer.Count; i++)
        {
            lastSeenTime.Remove(pruneBuffer[i]);
        }
    }
}
