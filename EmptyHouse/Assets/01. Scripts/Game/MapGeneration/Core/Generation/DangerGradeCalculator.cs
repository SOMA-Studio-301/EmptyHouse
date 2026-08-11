using System.Collections.Generic;

namespace EmptyHouse.MapGen.Core
{
    /// <summary>
    /// 방 위험 등급 산출(3절) — 입구(방 0)로부터의 그래프 홉 거리 + 층 가중(DangerBias, 다층 M9-6).
    /// 열쇠·지름길·스폰 등 이후 모든 배치 규칙의 입력값.
    /// **홉 거리를 가중 Dijkstra 로 바꾸지 않는다** — 이 배열은 거리 의미로도 소비돼(ShortcutValueMin·
    /// ListenerCounterDist 는 방 수 단위) 가중치를 섞으면 단위가 무너진다. 위험도는 별도 배열(ComputeDangerGrades)로 분리.
    /// </summary>
    public static class DangerGradeCalculator
    {
        /// <summary>
        /// 방별 입구 홉 거리를 BFS 로 계산한다(구 ComputeDepths — M9-4 개명, 무가중 유지).
        /// 잠긴 문도 간선으로 취급한다(AC-06 과 같은 기준).
        /// </summary>
        /// <param name="blueprint">Rooms/Edges 가 채워진 블루프린트.</param>
        /// <returns>방 인덱스 → 입구로부터의 그래프 홉 거리. 미도달 방은 -1(고립 — AC-06 위반 신호).</returns>
        public static int[] ComputeHopDistances(MapBlueprint blueprint)
        {
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

        /// <summary>
        /// 위험 등급 = 홉 거리 + 방이 속한 층의 가중(스펙 3절 v2 — B1 &gt; 2F &gt; 1F).
        /// DangerBias 가 전부 0이면 grades == hops 라 v1 하위호환이 자동이다. 미도달(-1)은 가중 없이 유지.
        /// </summary>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="hopDistances">ComputeHopDistances 결과.</param>
        /// <param name="plan">생성 계획(층 가중 조회).</param>
        /// <returns>방 인덱스 → 위험 등급.</returns>
        public static int[] ComputeDangerGrades(MapBlueprint blueprint, int[] hopDistances, MapGenPlan plan)
        {
            var grades = new int[hopDistances.Length];
            for (int r = 0; r < hopDistances.Length; r++)
            {
                grades[r] = hopDistances[r] < 0 ? -1 : hopDistances[r] + plan.DangerBiasOf(blueprint.Rooms[r].FloorIndex);
            }

            return grades;
        }
    }
}
