using System.Collections.Generic;
using Border.Core;

namespace EmptyHouse.MapGen.Core
{
    /// <summary>
    /// 도달 가능성 BFS(4-3절 R 계산) — 열쇠 배치(LockKeyPlacer)와 검증(6·7절)이 같은 계산을 재사용한다.
    /// </summary>
    public static class ReachabilityAnalyzer
    {
        /// <summary>
        /// R_i 계산(4-3절) — 자물쇠 1~(lockNumber−1)은 열 수 있다 치고 lockNumber 번 이후는 잠긴 채로,
        /// 입구(방 0)에서 BFS 한 도달 가능 방 집합.
        /// </summary>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="lockNumber">기준 자물쇠 번호 i.</param>
        /// <returns>도달 가능한 방 인덱스 집합 R_i.</returns>
        public static HashSet<int> ComputeReachableRooms(MapBlueprint blueprint, int lockNumber)
        {
            // TODO(impl):
            Log.D($"[ReachabilityAnalyzer] ComputeReachableRooms i={lockNumber}");
            return default;
        }

        /// <summary>
        /// 특정 간선(관문)을 잠긴 문 취급하고 입구에서 BFS 한 도달 집합 — 파훼 쌍 판정용(6절).
        /// 그 집합 안에 파훼 수단이 존재하는지 호출자가 확인한다.
        /// </summary>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="blockedEdgeIndex">관문으로 취급할 간선 인덱스.</param>
        /// <returns>관문 미통과 도달 가능 방 인덱스 집합.</returns>
        public static HashSet<int> ComputeReachableWithEdgeBlocked(MapBlueprint blueprint, int blockedEdgeIndex)
        {
            // TODO(impl):
            Log.D($"[ReachabilityAnalyzer] ComputeReachableWithEdgeBlocked edge={blockedEdgeIndex}");
            return default;
        }
    }
}
