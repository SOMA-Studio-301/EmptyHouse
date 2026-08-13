using System.Collections.Generic;
using Border.Core;
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

    private readonly Dictionary<ZombieController, float> lastSeenTime = new Dictionary<ZombieController, float>(); // 좀비 → 마지막 목격 시각(잔상 판정)

    /// <summary>
    /// 현재 표시 대상 좀비의 월드 좌표를 수집한다(잔상 포함). 호출자가 준 버퍼를 채운다(GC 0).
    /// 카메라는 로컬 메인 카메라 — 관전 중에는 관전 시점이 그대로 기준이 된다.
    /// </summary>
    /// <param name="results">월드 좌표 수신 버퍼(호출자 소유 — 메서드가 Clear 후 채운다).</param>
    public void CollectVisible(List<Vector3> results)
    {
        // TODO(impl): 뷰포트 판정 → 거리 컷 → Linecast(카메라→좀비 흉부) → lastSeenTime 갱신 → 잔상 포함 수집
        Log.D("[ZombieSightProbe] CollectVisible");
    }
}
