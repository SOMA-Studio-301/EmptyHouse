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
            Log.D($"[ReachabilityAnalyzer] ComputeReachableRooms i={lockNumber}");
            return BreadthFirstSearch(blueprint, blockedEdgeIndex: -1, openLockBelow: lockNumber);
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
            Log.D($"[ReachabilityAnalyzer] ComputeReachableWithEdgeBlocked edge={blockedEdgeIndex}");
            return BreadthFirstSearch(blueprint, blockedEdgeIndex, openLockBelow: int.MaxValue);
        }

        /// <summary>
        /// 입구(방 0)에서 BFS 한 도달 방 집합의 공통 구현.
        /// 통행 가능 = 봉인 아님(RoomB ≥ 0) · 막힌 벽 아님 · 차단 간선 아님 · 잠긴 문이면 자물쇠 번호 &lt; openLockBelow.
        /// 인접 리스트는 간선 인덱스 순서로만 구축한다(결정론 — 8절).
        /// </summary>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="blockedEdgeIndex">차단 간선 인덱스(-1 = 차단 없음).</param>
        /// <param name="openLockBelow">이 번호 미만 자물쇠는 열 수 있다고 취급(int.MaxValue = 전부 통행).</param>
        /// <returns>도달 가능한 방 인덱스 집합.</returns>
        private static HashSet<int> BreadthFirstSearch(MapBlueprint blueprint, int blockedEdgeIndex, int openLockBelow)
        {
            var reachable = new HashSet<int>();
            int roomCount = blueprint.Rooms.Count;
            if (roomCount == 0)
            {
                return reachable;
            }

            var adjacency = new List<int>[roomCount];
            for (int i = 0; i < roomCount; i++)
            {
                adjacency[i] = new List<int>();
            }

            for (int e = 0; e < blueprint.Edges.Count; e++)
            {
                if (e == blockedEdgeIndex)
                {
                    continue;
                }

                BlueprintEdge edge = blueprint.Edges[e];
                if (edge.RoomB < 0 || edge.State == EdgeState.BlockedWall)
                {
                    continue;
                }

                if (edge.State == EdgeState.DoorLocked && edge.LockNumber >= openLockBelow)
                {
                    continue;
                }

                adjacency[edge.RoomA].Add(edge.RoomB);
                adjacency[edge.RoomB].Add(edge.RoomA);
            }

            var queue = new Queue<int>();
            reachable.Add(0);
            queue.Enqueue(0);
            while (queue.Count > 0)
            {
                int room = queue.Dequeue();
                List<int> neighbors = adjacency[room];
                for (int i = 0; i < neighbors.Count; i++)
                {
                    if (reachable.Add(neighbors[i]))
                    {
                        queue.Enqueue(neighbors[i]);
                    }
                }
            }

            return reachable;
        }
    }
}
