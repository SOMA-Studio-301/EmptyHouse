using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 소유자 카메라 피벗의 "위치"를 머리 본 아래 눈 앵커에 추종시킨다.
/// 회전은 PlayerController(입력)가 소유하므로 건드리지 않는다 — 머리 본 회전을 상속하면
/// LookAt 이 돌린 고개가 시야를 함께 돌려 조준·시선이 순환 참조로 출렁이기 때문에 위치만 따라간다.
/// LateUpdate(애니메이션·IK 적용 후) 시점에 복사해, 고개가 돌아가도 카메라가 머리를 따라 이동해
/// 목·얼굴 메시가 시야에 걸리지 않는다.
/// 추종은 월드가 아닌 루트 로컬 좌표에서 저역통과한다 — 본체의 이동·회전은 루트에 하드 부착된 채 지연 0 으로
/// 통과하고(달리기·텔레포트에도 카메라가 처지지 않는다), 걸러지는 것은 몸 기준 머리 흔들림(걷기 밥·발디딤 충격)뿐이다.
/// </summary>
public sealed class PlayerHeadCameraFollow : NetworkBehaviour
{
    [SerializeField] private Transform cameraPivot; // 따라 움직일 카메라 피벗(루트 자식). 회전은 PlayerController 소유
    [SerializeField] private Transform eyeAnchor; // 머리 본 아래 눈 위치 앵커. 애니메이션이 움직인 최종 머리 위치를 대표한다
    [SerializeField] private Vector3 eyeOffset = Vector3.zero; // 앵커 기준 추가 오프셋(시야 기준: x=우, y=상, z=시선 전방). 실시간 튜닝 가능
    [SerializeField] private float followSpeed = 5f; // 루트 로컬 눈 위치 추종 속도(1/초). 낮출수록 머리 애니 진동이 더 걷히고, 앉기 같은 지속 포즈 반영은 느려진다
    [SerializeField] private float maxLag = 0.15f; // 추종이 뒤처질 수 있는 최대 거리(m). 급격한 포즈 전환에서도 시야가 눈 앵커에서 이 이상 벌어지지 않게 하는 상한

    private Vector3 baselineLocal; // 루트 로컬 기준 눈 위치의 저역통과 상태. 로컬이라 본체 이동·회전은 이 필터를 거치지 않는다
    private int sampledFrame = -1; // baselineLocal 을 갱신한 마지막 프레임. IK 패스와 LateUpdate 가 같은 값을 보도록 프레임당 1회로 제한한다
    private Transform anchorParent; // 눈 앵커의 부모(머리 본). 본 스케일과 무관하게 앵커 위치를 재구성하는 기준
    private Vector3 anchorOffset; // 머리 본 회전 기준 눈 오프셋(월드 단위). Awake 시점의 본 스케일을 구워 둔다

    /// <summary>붕괴 전 머리 본 스케일로 눈 앵커 오프셋을 구워 둔다(PlayerLocalHeadHider 의 Hide 는 스폰 이후라 여기서는 아직 원본 스케일이다).</summary>
    private void Awake()
    {
        anchorParent = eyeAnchor.parent;
        anchorOffset = Vector3.Scale(eyeAnchor.localPosition, anchorParent.lossyScale);
    }

    /// <summary>스폰 시점 머리 위치로 추종 상태를 맞춘다. 원격도 손 IK 가 뷰 원점을 읽으므로 컴포넌트를 끄지 않는다.</summary>
    public override void OnNetworkSpawn()
    {
        SnapToTarget();
    }

    /// <summary>
    /// 머리 본 스케일과 무관하게 눈 앵커의 월드 위치를 재구성한다.
    /// 소유자는 PlayerLocalHeadHider 가 머리 본을 붕괴시켜 앵커의 전방 오프셋까지 0 으로 뭉개지는데,
    /// 그대로 쓰면 카메라가 목 중심에 박혀 하향 시 몸통 안이 보인다(블랙스크린) — 원본 스케일 오프셋으로 되살린다.
    /// </summary>
    /// <returns>눈 앵커의 월드 좌표.</returns>
    private Vector3 GetAnchorPosition()
    {
        return anchorParent.position + anchorParent.rotation * anchorOffset;
    }

    /// <summary>애니메이션·IK 가 끝난 뒤 추종 상태를 갱신하고, 소유자에 한해 카메라 피벗을 뷰 원점으로 옮긴다.</summary>
    private void LateUpdate()
    {
        UpdateBaseline();

        if (!IsOwner) return;

        cameraPivot.position = GetViewOrigin(cameraPivot.rotation);
    }

    /// <summary>
    /// 루트 로컬 눈 위치를 프레임당 1회 저역통과해 baselineLocal 을 갱신한다.
    /// IK 패스(OnAnimatorIK)와 LateUpdate 중 먼저 오는 쪽이 샘플링하고 나머지는 같은 값을 재사용한다 —
    /// 한 프레임 안에서 손 IK 기준점과 시야 원점이 어긋나지 않게 하기 위함이다.
    /// </summary>
    private void UpdateBaseline()
    {
        if (sampledFrame == Time.frameCount) return;

        Vector3 local = transform.InverseTransformPoint(GetAnchorPosition());

        // 첫 샘플은 보간 없이 즉시 맞춘다. 감쇠 계수는 Exp 형태여야 프레임률과 무관하다 — k * dt 는 프레임률에 따라 감쇠량이 달라진다
        baselineLocal = sampledFrame < 0
            ? local
            : Vector3.Lerp(baselineLocal, local, 1f - Mathf.Exp(-followSpeed * Time.deltaTime));

        // 오차 상한. 필터가 뒤처지더라도 눈 앵커에서 maxLag 이상 벌어질 수 없다
        baselineLocal = local + Vector3.ClampMagnitude(baselineLocal - local, maxLag);

        sampledFrame = Time.frameCount;
    }

    /// <summary>추종 상태를 현재 머리 위치로 즉시 일치시킨다. 텔레포트·벽장 스냅처럼 포즈가 불연속으로 바뀔 때 호출한다.</summary>
    public void SnapToTarget()
    {
        baselineLocal = transform.InverseTransformPoint(GetAnchorPosition());
        sampledFrame = Time.frameCount;
    }

    /// <summary>
    /// 주어진 뷰 회전 기준의 뷰 원점(눈 앵커 + 오프셋) 월드 좌표를 돌려준다.
    /// 카메라 위치이자, 손 IK 등 뷰 공간 표현의 기준점이다 — 원격 인스턴스는 복제된 조준값으로 만든
    /// 뷰 회전을 넘겨 같은 원점을 재구성한다.
    /// </summary>
    /// <param name="viewRotation">뷰(시선) 회전.</param>
    /// <returns>뷰 원점 월드 좌표.</returns>
    public Vector3 GetViewOrigin(Quaternion viewRotation)
    {
        UpdateBaseline();

        return transform.TransformPoint(baselineLocal) + viewRotation * eyeOffset;
    }
}
