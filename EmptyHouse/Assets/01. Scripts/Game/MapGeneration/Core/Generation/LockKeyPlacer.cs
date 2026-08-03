using Border.Core;

namespace EmptyHouse.MapGen.Core
{
    /// <summary>
    /// 열쇠·자물쇠 배치(4절) — 자물쇠를 전부 먼저 건 뒤, 열쇠를 번호 순서대로 R 불변식(4-3) 아래 배치한다.
    /// 번갈아 배치하면 나중 자물쇠가 먼저 놓은 열쇠를 가두는 사고가 난다(4-1).
    /// </summary>
    public sealed class LockKeyPlacer
    {
        /// <summary>
        /// 자물쇠(중요 물품 문 + 지름길)를 걸고 열쇠를 배치해 Edges 의 잠금과 Spawns 의 열쇠를 채운다.
        /// </summary>
        /// <param name="rng">단일 난수 스트림.</param>
        /// <param name="genParams">생성 파라미터.</param>
        /// <param name="blueprint">레이아웃이 완성된 블루프린트.</param>
        /// <param name="dangerDepths">방별 위험 깊이(DangerGradeCalculator 출력).</param>
        /// <returns>배치 성공 여부 — 실패 시 호출자가 리롤.</returns>
        public bool TryPlace(DeterministicRng rng, MapGenParams genParams, MapBlueprint blueprint, int[] dangerDepths)
        {
            // TODO(impl): PlaceLocks → TryPlaceKeys 순서 고정(4-1)
            Log.D("[LockKeyPlacer] TryPlace");
            return default;
        }

        /// <summary>
        /// 자물쇠를 전부 먼저 건다(4-1) — 백신·기름 고위험 서브트리를 끊는 간선 +
        /// 가치 규칙(4-2)을 통과한 루프 간선(가중 랜덤, 가중치 ∝ 연결 구역 위험도).
        /// </summary>
        /// <param name="rng">단일 난수 스트림.</param>
        /// <param name="genParams">생성 파라미터(지름길 최소 가치).</param>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="dangerDepths">방별 위험 깊이.</param>
        private void PlaceLocks(DeterministicRng rng, MapGenParams genParams, MapBlueprint blueprint, int[] dangerDepths)
        {
            // TODO(impl):
            Log.D("[LockKeyPlacer] PlaceLocks");
        }

        /// <summary>
        /// 지름길 가치 = 그 문을 열었을 때 고위험 구역 → 버스(입구=출구) 귀환 최단 거리가 줄어드는 방 수(4-2).
        /// 두 방 사이만 가까워지고 귀환에 무의미한 간선은 이 정의로 자동 탈락한다.
        /// </summary>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="edgeIndex">평가할 루프 간선 인덱스.</param>
        /// <param name="dangerDepths">방별 위험 깊이.</param>
        /// <returns>귀환 단축 이득(방 수).</returns>
        private int ComputeShortcutValue(MapBlueprint blueprint, int edgeIndex, int[] dangerDepths)
        {
            // TODO(impl):
            Log.D("[LockKeyPlacer] ComputeShortcutValue");
            return default;
        }

        /// <summary>
        /// 열쇠_i 를 반드시 R_i 안에 배치한다(4-3 절대 규칙 — 예외 없음).
        /// 위험도 2단 폴백(1순위: 위험도 ≥ 자물쇠 방 / 폴백: R_i 내 최고 위험 방, 발동 시 X5 로그),
        /// 자물쇠 문 인접 방은 후보 우선순위 최하위(배제 아님).
        /// </summary>
        /// <param name="rng">단일 난수 스트림.</param>
        /// <param name="blueprint">자물쇠가 걸린 블루프린트.</param>
        /// <param name="dangerDepths">방별 위험 깊이.</param>
        /// <returns>전 열쇠 배치 성공 여부.</returns>
        private bool TryPlaceKeys(DeterministicRng rng, MapBlueprint blueprint, int[] dangerDepths)
        {
            // TODO(impl): ReachabilityAnalyzer.ComputeReachableRooms 로 R_i 산출 후 배치
            Log.D("[LockKeyPlacer] TryPlaceKeys");
            return default;
        }
    }
}
