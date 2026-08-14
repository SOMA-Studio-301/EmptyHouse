using UnityEngine;

/// <summary>
/// 월드 공간 UI 가 항상 로컬 카메라를 바라보게 돌리고, 거리에 따라 크기를 보정해 멀리서도 읽히게 한다.
/// 카메라는 씬마다 갈리고 관전 중에는 대상이 바뀌므로, Transform 을 캐시해 두되 사라지면 다시 잡는다.
/// 회전과 스케일이 한 컴포넌트에 있는 이유는 둘 다 카메라까지의 방향·거리를 쓰기 때문이다 — 나누면 같은 계산을 두 번 한다.
/// 실행 순서를 뒤로 미루는 이유는 CinemachineBrain 이 기본 순서(0)의 LateUpdate 에서 카메라를 옮기기 때문이다 —
/// 순서를 고정하지 않으면 지난 프레임 카메라 위치로 정렬해 회전이 한 프레임 밀린다.
/// </summary>
[DefaultExecutionOrder(200)]
public sealed class UIWorldBillboard : MonoBehaviour
{
    [SerializeField] private bool lockVerticalTilt = true; // true 면 수평 회전만 따라간다 — 올려다볼 때 글자가 눕지 않는다

    [Header("Distance Scaling")]
    [SerializeField] private bool scaleWithDistance = true;            // 끄면 프리팹에 지정한 크기를 그대로 쓴다
    [Min(0.1f)] [SerializeField] private float referenceDistance = 5f; // 이 거리에서 프리팹 원래 크기로 보인다
    [Min(0.1f)] [SerializeField] private float minScaleFactor = 1f;    // 가까울 때 더 작아지지 않게 하는 하한
    [Min(0.1f)] [SerializeField] private float maxScaleFactor = 3f;    // 멀 때 무한정 커지지 않게 하는 상한. 기준거리 × 이 값까지는 화면상 크기가 일정하다

    private Transform cameraTransform; // 바라볼 로컬 카메라. Camera.main 은 태그 탐색을 탈 수 있어 캐시한다
    private Vector3 baseScale;         // 프리팹에 지정된 원래 스케일. 거리 보정의 기준값

    /// <summary>거리 보정의 기준이 될 원래 스케일을 저장한다.</summary>
    private void Awake()
    {
        baseScale = transform.localScale;
    }

    /// <summary>카메라 이동·회전이 끝난 뒤 정렬해야 한 프레임 밀리지 않는다.</summary>
    private void LateUpdate()
    {
        if (cameraTransform == null)
        {
            Camera main = Camera.main;
            if (main == null) return; // 카메라가 아직 없는 프레임(씬 전환 등)은 조용히 넘어간다

            cameraTransform = main.transform;
        }

        // 캔버스의 +Z 가 화면 안쪽을 향해야 글자가 뒤집히지 않으므로 카메라 → UI 방향을 그대로 쓴다.
        Vector3 direction = transform.position - cameraTransform.position;
        if (lockVerticalTilt) direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f) return; // 카메라와 겹친 순간의 LookRotation 경고를 피한다

        transform.rotation = Quaternion.LookRotation(direction);

        if (!scaleWithDistance) return;

        // 거리에 비례해 키우면 화면상 크기가 일정해진다. 상·하한으로 과장을 막는다.
        float distance = Vector3.Distance(transform.position, cameraTransform.position);
        float factor = Mathf.Clamp(distance / referenceDistance, minScaleFactor, maxScaleFactor);
        transform.localScale = baseScale * factor;
    }
}
