using UnityEngine;

/// <summary>
/// 투척 궤적의 해석식 포물선 계산. 조준 표시(<see cref="ThrowAimIndicator"/>)와
/// 실제 비행체(<see cref="ThrownProjectile"/>)가 같은 식을 공유하도록 한 곳에 둔다.
///
/// 이 예측이 실제 낙하와 어긋나면 궤적선이 거짓말을 하게 되므로, 투척체 Rigidbody 는
/// linearDamping = 0 · useGravity = true 여야 한다(그래야 p(t) = p0 + v0·t + ½g·t² 가 성립한다).
/// 첫 충돌 이후의 튕김은 예측하지 않는다 — 궤적선도 첫 충돌 지점에서 끊기므로 문제되지 않는다.
///
/// 곡선 모양뿐 아니라 **무엇에 걸려 멈추는지**도 실제와 같아야 한다. <see cref="ThrownProjectile"/> 은
/// 레이어를 가리지 않고 첫 충돌에서 착지하므로, 여기서도 마스크로 대상을 좁히면 안 된다 —
/// 좁히면 좀비·픽업처럼 마스크 밖 물체 앞에서 실제 병만 멈추고 궤적선은 그 너머 바닥까지 뻗는다.
/// 유일한 예외가 던지는 사람 자신이며, 이는 <see cref="ThrownProjectile.ServerLaunch"/> 가
/// IgnoreCollision 으로 빼 두는 것과 같은 예외다 — 레이어가 아니라 계층으로 걸러야 한다(둘 다 Default).
/// </summary>
public static class ThrowTrajectory
{
    // 구간별 SphereCast 결과 버퍼. 궤적은 매 프레임 수십 구간을 훑으므로 구간마다 배열을 새로 잡지 않는다.
    private static readonly RaycastHit[] segmentHits = new RaycastHit[8];

    /// <summary>
    /// 포물선을 일정 시간 간격으로 샘플링해 <paramref name="points"/> 에 채우고, 도중 첫 충돌을 찾는다.
    /// 샘플 사이를 SphereCast 로 이어 검사하므로 간격이 성겨도 벽을 그대로 통과하지 않는다.
    /// </summary>
    /// <param name="origin">발사 지점(월드).</param>
    /// <param name="velocity">발사 초속도(월드, m/s).</param>
    /// <param name="radius">비행체 반지름. 실제 콜라이더와 맞춰야 착지점이 맞는다.</param>
    /// <param name="hitMask">충돌로 칠 레이어. 실제 비행체가 레이어를 가리지 않으므로 Everything 을 넘긴다.</param>
    /// <param name="ignoreRoot">궤적이 무시할 계층의 루트(던지는 사람). null 이면 아무것도 제외하지 않는다.</param>
    /// <param name="points">샘플을 채울 버퍼. 길이가 곧 최대 샘플 수다.</param>
    /// <param name="stepSeconds">샘플 간 시간 간격(초). 작을수록 곡선이 매끄럽고 비용이 늘어난다.</param>
    /// <param name="impact">첫 충돌 정보. 충돌이 없었다면 기본값이다.</param>
    /// <returns>실제로 채워진 점 개수. 충돌했다면 충돌 지점이 마지막 점이다.</returns>
    public static int Simulate(
        Vector3 origin,
        Vector3 velocity,
        float radius,
        LayerMask hitMask,
        Transform ignoreRoot,
        Vector3[] points,
        float stepSeconds,
        out RaycastHit impact)
    {
        impact = default;
        if (points == null || points.Length == 0) return 0;

        points[0] = origin;

        for (int i = 1; i < points.Length; i++)
        {
            float elapsed = stepSeconds * i;
            points[i] = origin + velocity * elapsed + 0.5f * Physics.gravity * (elapsed * elapsed);

            Vector3 segment = points[i] - points[i - 1];
            float segmentLength = segment.magnitude;
            if (segmentLength <= Mathf.Epsilon) continue;

            if (!TryFindImpact(points[i - 1], radius, segment / segmentLength, segmentLength, hitMask, ignoreRoot, out impact))
            {
                continue;
            }

            points[i] = impact.point;
            return i + 1;
        }

        return points.Length;
    }

    /// <summary>
    /// 한 구간을 훑어 <paramref name="ignoreRoot"/> 계층을 뺀 가장 가까운 충돌을 찾는다.
    /// 트리거는 무시한다 — 상호작용 트리거 볼륨에 궤적이 걸려 끊기면 안 된다.
    /// </summary>
    /// <param name="start">구간 시작점(월드).</param>
    /// <param name="radius">스윕 반지름.</param>
    /// <param name="direction">구간 진행 방향(정규화).</param>
    /// <param name="distance">구간 길이.</param>
    /// <param name="hitMask">충돌로 칠 레이어.</param>
    /// <param name="ignoreRoot">제외할 계층의 루트. null 이면 제외 없음.</param>
    /// <param name="impact">찾은 충돌 정보.</param>
    /// <returns>제외 대상이 아닌 충돌을 찾았는지 여부.</returns>
    private static bool TryFindImpact(
        Vector3 start,
        float radius,
        Vector3 direction,
        float distance,
        LayerMask hitMask,
        Transform ignoreRoot,
        out RaycastHit impact)
    {
        impact = default;

        int count = Physics.SphereCastNonAlloc(
            start, radius, direction, segmentHits, distance, hitMask, QueryTriggerInteraction.Ignore);

        float nearest = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            RaycastHit candidate = segmentHits[i];

            // 스윕 시작점이 이미 콜라이더 안이면 거리 0 에 point 가 의미 없는 값으로 온다 — 거기서 궤적을 접으면 발밑에서 끊긴다.
            if (candidate.distance <= 0f) continue;

            if (ignoreRoot != null && candidate.collider.transform.IsChildOf(ignoreRoot)) continue;

            if (candidate.distance >= nearest) continue;

            nearest = candidate.distance;
            impact = candidate;
        }

        return nearest < float.MaxValue;
    }
}
