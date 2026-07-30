using UnityEngine;

/// <summary>
/// 투척 궤적의 해석식 포물선 계산. 조준 표시(<see cref="ThrowAimIndicator"/>)와
/// 실제 비행체(<see cref="ThrownProjectile"/>)가 같은 식을 공유하도록 한 곳에 둔다.
///
/// 이 예측이 실제 낙하와 어긋나면 궤적선이 거짓말을 하게 되므로, 투척체 Rigidbody 는
/// linearDamping = 0 · useGravity = true 여야 한다(그래야 p(t) = p0 + v0·t + ½g·t² 가 성립한다).
/// 첫 충돌 이후의 튕김은 예측하지 않는다 — 궤적선도 첫 충돌 지점에서 끊기므로 문제되지 않는다.
/// </summary>
public static class ThrowTrajectory
{
    /// <summary>
    /// 포물선을 일정 시간 간격으로 샘플링해 <paramref name="points"/> 에 채우고, 도중 첫 충돌을 찾는다.
    /// 샘플 사이를 SphereCast 로 이어 검사하므로 간격이 성겨도 벽을 그대로 통과하지 않는다.
    /// </summary>
    /// <param name="origin">발사 지점(월드).</param>
    /// <param name="velocity">발사 초속도(월드, m/s).</param>
    /// <param name="radius">비행체 반지름. 실제 콜라이더와 맞춰야 착지점이 맞는다.</param>
    /// <param name="hitMask">충돌로 칠 레이어. 실제 착지 판정과 같은 마스크를 넘긴다.</param>
    /// <param name="points">샘플을 채울 버퍼. 길이가 곧 최대 샘플 수다.</param>
    /// <param name="stepSeconds">샘플 간 시간 간격(초). 작을수록 곡선이 매끄럽고 비용이 늘어난다.</param>
    /// <param name="impact">첫 충돌 정보. 충돌이 없었다면 기본값이다.</param>
    /// <returns>실제로 채워진 점 개수. 충돌했다면 충돌 지점이 마지막 점이다.</returns>
    public static int Simulate(
        Vector3 origin,
        Vector3 velocity,
        float radius,
        LayerMask hitMask,
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

            // 트리거는 무시한다 — 상호작용 트리거 볼륨에 궤적이 걸려 끊기면 안 된다.
            if (Physics.SphereCast(points[i - 1], radius, segment / segmentLength, out impact,
                                   segmentLength, hitMask, QueryTriggerInteraction.Ignore))
            {
                points[i] = impact.point;
                return i + 1;
            }
        }

        return points.Length;
    }
}
