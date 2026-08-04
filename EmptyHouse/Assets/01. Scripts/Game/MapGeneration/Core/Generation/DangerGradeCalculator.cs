using System.Collections.Generic;
using Border.Core;

namespace EmptyHouse.MapGen.Core
{
    /// <summary>
    /// 방 위험 등급 산출(3절) — 단일 층에서는 입구(방 0)로부터의 그래프 깊이가 위험 등급이다.
    /// 열쇠·지름길·스폰 등 이후 모든 배치 규칙의 입력값. 층 가중치(B1 &gt; 2F &gt; 1F)는 다층 v2(G6).
    /// </summary>
    public static class DangerGradeCalculator
    {
        /// <summary>
        /// 방별 입구 깊이를 BFS 로 계산한다. 잠긴 문도 간선으로 취급한다(AC-06 과 같은 기준).
        /// </summary>
        /// <param name="blueprint">Rooms/Edges 가 채워진 블루프린트.</param>
        /// <returns>방 인덱스 → 입구로부터의 그래프 깊이. 미도달 방은 -1(고립 — AC-06 위반 신호).</returns>
        public static int[] ComputeDepths(MapBlueprint blueprint)
        {
            Log.D("[DangerGradeCalculator] ComputeDepths");
            int roomCount = blueprint.Rooms.Count;
            var depths = new int[roomCount];
            for (int i = 0; i < roomCount; i++)
            {
                depths[i] = -1;
            }

            if (roomCount == 0)
            {
                return depths;
            }

            var adjacency = new List<int>[roomCount];
            for (int i = 0; i < roomCount; i++)
            {
                adjacency[i] = new List<int>();
            }

            for (int e = 0; e < blueprint.Edges.Count; e++)
            {
                BlueprintEdge edge = blueprint.Edges[e];
                if (edge.RoomB < 0 || edge.State == EdgeState.BlockedWall)
                {
                    continue;
                }

                adjacency[edge.RoomA].Add(edge.RoomB);
                adjacency[edge.RoomB].Add(edge.RoomA);
            }

            var queue = new Queue<int>();
            depths[0] = 0;
            queue.Enqueue(0);
            while (queue.Count > 0)
            {
                int room = queue.Dequeue();
                List<int> neighbors = adjacency[room];
                for (int i = 0; i < neighbors.Count; i++)
                {
                    int next = neighbors[i];
                    if (depths[next] < 0)
                    {
                        depths[next] = depths[room] + 1;
                        queue.Enqueue(next);
                    }
                }
            }

            return depths;
        }
    }
}
