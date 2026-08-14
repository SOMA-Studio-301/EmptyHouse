using UnityEngine;

/// <summary>
/// 고정 오프셋으로 매달린 3인칭 Vcam 이 벽 뒤로 넘어가지 않게, 기준점에서 원래 자리로 구를 훑어
/// 막히면 그 앞까지 당긴다. 원래 자리는 프리팹에 지정된 로컬 위치이고, 매 프레임 거기서 다시 계산하므로
/// 차폐가 풀리면 그대로 복귀한다(누적 오차 없음).
///
/// <see cref="Unity.Cinemachine.CinemachineDeoccluder"/> 를 쓰지 않는 이유는 그쪽이 "차폐물을 피해 돌아
/// 시야를 확보"하는 알고리즘이기 때문이다 — 기둥이 줄지어 선 곳에서는 프레임마다 가장 가까운 차폐물이 바뀌어
/// 해가 좌우로 튀고, 감쇠가 그 사이를 이어 카메라가 계속 흔들린다. 여기서는 기준점→원래 자리 선분 위에서
/// 거리만 줄이므로 해가 하나뿐이라 꼬일 수 없다. 대가는 벽 모서리에서 카메라가 대상에 바짝 붙는 것이다.
///
/// 셀프 컨테인드다 — 기준점은 부모 트랜스폼이라 씬 오브젝트를 물릴 필요가 없다.
/// 실행 순서를 앞당긴 이유는 CinemachineBrain 이 기본 순서(0)의 LateUpdate 에서 Vcam 상태를 읽기 때문이다.
/// </summary>
[DefaultExecutionOrder(-100)]
public sealed class CameraOcclusionPull : MonoBehaviour
{
    [SerializeField] private float pivotHeight = 1.1f;      // 부모 원점에서 기준점을 올릴 높이(m). 판정선이 대상 몸통에서 출발하게 한다. ⚪ 튜닝값
    [SerializeField] private LayerMask occlusionMask = ~0;  // 카메라를 막는 레이어. Default·Ground·Wall·Door 를 선택한다(기둥·천장이 Default 다)
    [SerializeField] private float cameraRadius = 0.25f;    // 차폐 판정 구 반지름(m). 근평면(0.1)보다 커야 벽면이 잘려 보이지 않는다. ⚪ 튜닝값
    [SerializeField] private float minDistance = 0.6f;      // 벽에 밀렸을 때 허용할 최소 거리(m). 대상 몸통 안까지 파고드는 것을 막는다. ⚪ 튜닝값

    private Transform pivot;   // 기준점이자 원래 자리의 기준 좌표계. 부모 트랜스폼이다
    private Vector3 restLocal; // 프리팹에 지정된 원래 로컬 위치. 차폐가 없을 때 돌아갈 자리
    private readonly RaycastHit[] hits = new RaycastHit[8]; // 차폐 판정 결과 버퍼. 매 프레임 쏘므로 NonAlloc 으로 GC 를 피한다

    /// <summary>기준점(부모)과 원래 로컬 위치를 캐시한다.</summary>
    private void Awake()
    {
        pivot = transform.parent;
        restLocal = transform.localPosition;
    }

    /// <summary>기준점에서 원래 자리로 구를 훑어 차폐 앞까지 당긴다. Brain 이 상태를 읽기 전에 끝내야 한 프레임 밀리지 않는다.</summary>
    private void LateUpdate()
    {
        Vector3 focus = pivot.position + Vector3.up * pivotHeight;
        Vector3 offset = pivot.TransformPoint(restLocal) - focus;

        float restDistance = offset.magnitude;
        if (restDistance < 0.0001f) return; // 기준점과 겹친 설정이면 당길 방향이 없다

        Vector3 direction = offset / restDistance;
        transform.position = focus + direction * ResolveDistance(focus, direction, restDistance);
    }

    /// <summary>
    /// 기준점에서 방향으로 구를 쓸어 차폐물 앞까지로 거리를 줄인다.
    /// 반지름만큼 여유를 두고 멈추므로(SphereCast 거리는 구 중심 이동량) 벽면이 근평면에 잘리지 않는다.
    /// </summary>
    /// <param name="focus">판정 시작점(부모 원점 + pivotHeight).</param>
    /// <param name="direction">기준점에서 원래 자리로 향하는 단위 방향.</param>
    /// <param name="restDistance">차폐가 없을 때의 거리(m).</param>
    /// <returns>차폐를 반영한 거리(m). 최소 minDistance.</returns>
    private float ResolveDistance(Vector3 focus, Vector3 direction, float restDistance)
    {
        int count = Physics.SphereCastNonAlloc(
            focus, cameraRadius, direction, hits, restDistance, occlusionMask, QueryTriggerInteraction.Ignore);

        float distance = restDistance;
        for (int i = 0; i < count; i++)
        {
            // 자기 소유자는 건너뛴다 — 플레이어 캡슐이 Default 라 마스크에 걸리는데 기준점이 그 안이라 거리 0 으로 잡힌다.
            if (hits[i].transform.root == pivot.root) continue;

            distance = Mathf.Min(distance, hits[i].distance);
        }

        return Mathf.Max(minDistance, distance);
    }
}
