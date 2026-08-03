using UnityEngine;

/// <summary>
/// 군중 배치용 부모 스크립트. 씬 로드 시 자식 좀비 전체의 배회를 꺼 제자리 대기시킨다.
/// 지각·조사·추격·무리 동조는 각 좀비가 정상 수행한다.
/// </summary>
public class ZombieCrowd : MonoBehaviour
{
    /// <summary>자식 좀비를 전부 수집해 배회를 끈다.</summary>
    private void Awake()
    {
        ZombieController[] zombies = GetComponentsInChildren<ZombieController>(true);
        for (int i = 0; i < zombies.Length; i++)
        {
            zombies[i].SetWanderEnabled(false);
        }
    }
}
