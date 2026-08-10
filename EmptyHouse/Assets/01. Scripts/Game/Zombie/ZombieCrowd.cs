using UnityEngine;

/// <summary>
/// 군중 배치용 부모 스크립트. 씬 로드 시 자식 좀비 전체에 좁은 배회 반경을 주입해,
/// 각자 자기 자리 근처에서만 서성이게 한다(제자리 턴 포함).
///
/// 반경을 좁히는 것이 핵심이다 — ZombieCrowd 프리팹의 무리는 반지름 4m 원 위에 45° 간격으로 서므로
/// 인접 좀비 간 거리가 2 × 4 × sin(22.5°) ≈ 3.06m 다. 여기서 에이전트 반지름(0.5m)을 양쪽 빼면
/// 배회 영역이 겹치지 않는 상한은 약 1.03m, 원끼리만 안 겹치는 상한은 약 1.53m 다.
/// SO 기본값 5m 를 그대로 쓰면 원 하나가 무리 전체(지름 8m)를 삼켜 서로 부대낀다.
///
/// 배회를 끄면(예전 동작) 좀비가 홈에 정지한 채 회전 목표까지 자기 자신이 돼 완전히 굳으므로,
/// 여기서는 배회를 켠 채 반경으로만 제어한다. 지각·조사·추격·무리 동조는 각 좀비가 정상 수행한다.
/// </summary>
public class ZombieCrowd : MonoBehaviour
{
    [Tooltip("무리 좀비 전원에게 주입할 배회 반경(m). 인접 간격 3.06m 기준 상한은 약 1.5m.")]
    [SerializeField, Min(0f)] private float patrolRadiusMeters = 1.5f;

    /// <summary>자식 좀비를 전부 수집해 배회를 켜고 같은 반경을 주입한다.</summary>
    private void Awake()
    {
        ZombieController[] zombies = GetComponentsInChildren<ZombieController>(true);
        for (int i = 0; i < zombies.Length; i++)
        {
            zombies[i].SetWanderEnabled(true);
            zombies[i].SetPatrolRadius(patrolRadiusMeters);
        }
    }
}
