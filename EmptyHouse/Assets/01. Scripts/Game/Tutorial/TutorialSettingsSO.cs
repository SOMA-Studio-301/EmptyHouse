using UnityEngine;

/// <summary>
/// 튜토리얼 파라미터 SO — Docs/Spec/Data/tutorial.csv 의 코드 측 대응물 (튜토리얼.md 7장).
/// 문서·코드에 숫자를 박지 않는다는 계약의 실현이며, 값은 전부 인스펙터에서 튜닝한다.
/// 배치 규격 값(복도 비율·철창 거리)은 런타임 판정용이 아니라 씬 배치 검증용이다.
/// </summary>
[CreateAssetMenu(fileName = "SO_TutorialSettings", menuName = "EmptyHouse/Tutorial/Tutorial Settings")]
public sealed class TutorialSettingsSO : ScriptableObject
{
    [Header("배치 규격 (씬 검증용)")]
    [SerializeField, Range(0.1f, 1f)] private float passCorridorRatio = 0.5f; // 통과 복도 길이 상한 = 게이지 지속 시간의 이 비율(4-4-3). 소모율 확정 시 절대값 재계산
    [SerializeField, Min(0f)] private float cageToPathMaxMeters = 5f;         // 철창↔통과 경로 최대 거리(m). zombie.csv vis_instant_range 와 같은 값 — 해제 즉시 발각 보장(D31 · 4-4-2)

    public float PassCorridorRatio => passCorridorRatio;   // 통과 복도 길이 비율 상한
    public float CageToPathMaxMeters => cageToPathMaxMeters; // 철창↔경로 최대 거리(m)
}
