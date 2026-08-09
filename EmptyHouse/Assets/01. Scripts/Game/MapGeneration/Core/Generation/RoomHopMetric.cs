using System.Collections.Generic;

namespace EmptyHouse.MapGen.Core
{
    /// <summary>
    /// 방 단위 이동 비용 척도 — 복도 통과 비용 0, 방 진입 비용 1(0-1 BFS).
    /// 위험 등급용 <see cref="DangerGradeCalculator"/> 의 전체 노드 hop 과 다르다: 복도 경유율이 높으면
    /// 전체 hop 이 방 수의 2배 이상으로 부풀어, "복도만 우회하는 가짜 지름길"이 임계를 통과한다(2026-08-07 실측:
    /// 채택 지름길의 9.4%가 방 단위 이득 0, 후보 전체의 67%가 이득 0).
    /// 지름길 가치·열쇠 거리 판정이 이 척도를 공유한다.
    /// </summary>
    public static class RoomHopMetric
    {
        /// <summary>
        /// 출발 노드에서 각 노드까지의 방 단위 거리를 구한다. 막힌 벽만 통과 불가로 보고 잠긴 문은 통과로 친다
        /// (지름길 가치는 "그 문이 열렸을 때"의 이득이라 나머지 문은 열린 기준으로 재는 현행 규약 유지).
        /// </summary>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="isCorridor">노드별 복도 여부(비용 0 판정).</param>
        /// <param name="source">출발 노드 인덱스.</param>
        /// <returns>노드별 거리(미도달 -1).</returns>
        public static int[] Distances(MapBlueprint blueprint, bool[] isCorridor, int source)
        {
            int nodeCount = blueprint.Rooms.Count;
            var adjacency = new List<int>[nodeCount];
            for (int i = 0; i < nodeCount; i++)
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

            var distances = new int[nodeCount];
            for (int i = 0; i < nodeCount; i++)
            {
                distances[i] = -1;
            }

            // 0-1 BFS — 비용 0 간선은 앞에, 1은 뒤에 넣어 우선순위 큐 없이 최단거리를 확정한다
            var deque = new LinkedList<int>();
            distances[source] = 0;
            deque.AddFirst(source);
            while (deque.Count > 0)
            {
                int node = deque.First.Value;
                deque.RemoveFirst();
                List<int> neighbors = adjacency[node];
                for (int i = 0; i < neighbors.Count; i++)
                {
                    int next = neighbors[i];
                    int cost = isCorridor[next] ? 0 : 1;
                    int candidate = distances[node] + cost;
                    if (distances[next] >= 0 && distances[next] <= candidate)
                    {
                        continue;
                    }

                    distances[next] = candidate;
                    if (cost == 0)
                    {
                        deque.AddFirst(next);
                    }
                    else
                    {
                        deque.AddLast(next);
                    }
                }
            }

            return distances;
        }

        /// <summary>템플릿을 훑어 노드별 복도 여부 배열을 만든다.</summary>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="templates">템플릿 집합.</param>
        /// <returns>노드별 복도 여부.</returns>
        public static bool[] CorridorFlags(MapBlueprint blueprint, IReadOnlyList<RoomTemplateDef> templates)
        {
            var flags = new bool[blueprint.Rooms.Count];
            for (int r = 0; r < flags.Length; r++)
            {
                flags[r] = FindTemplate(templates, blueprint.Rooms[r].TemplateId).IsCorridor;
            }

            return flags;
        }

        /// <summary>
        /// 지름길 가치 = 그 간선을 막았을 때 입구까지의 방 단위 거리가 늘어나는 최대량(4-2).
        /// 복도는 비용 0이라 "복도만 우회하는" 간선은 이득 0으로 자동 탈락한다.
        /// 탈출구(ReturnExit)는 판정에 넣지 않는다 — 지름길은 입구 귀환 기준(2026-08-07 확정).
        /// </summary>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="edgeIndex">평가할 간선 인덱스.</param>
        /// <param name="isCorridor">노드별 복도 여부.</param>
        /// <param name="baseDistances">간선이 살아 있을 때의 입구 기준 거리.</param>
        /// <returns>귀환 단축 이득(방 수).</returns>
        public static int ShortcutValue(MapBlueprint blueprint, int edgeIndex, bool[] isCorridor, int[] baseDistances)
        {
            BlueprintEdge edge = blueprint.Edges[edgeIndex];
            EdgeState original = edge.State;
            edge.State = EdgeState.BlockedWall;
            int[] without = Distances(blueprint, isCorridor, 0);
            edge.State = original;

            int best = 0;
            for (int r = 0; r < without.Length; r++)
            {
                // 복도는 그 자체가 목적지가 아니다 — 방 기준 이득만 센다
                if (isCorridor[r] || without[r] < 0 || baseDistances[r] < 0)
                {
                    continue;
                }

                int gain = without[r] - baseDistances[r];
                if (gain > best)
                {
                    best = gain;
                }
            }

            return best;
        }

        /// <summary>TemplateId 로 템플릿을 찾는다(없으면 데이터 결함 — NRE 표면화).</summary>
        /// <param name="templates">템플릿 집합.</param>
        /// <param name="templateId">찾을 ID.</param>
        /// <returns>일치 템플릿.</returns>
        private static RoomTemplateDef FindTemplate(IReadOnlyList<RoomTemplateDef> templates, string templateId)
        {
            for (int t = 0; t < templates.Count; t++)
            {
                if (templates[t].TemplateId == templateId)
                {
                    return templates[t];
                }
            }

            return null;
        }
    }
}
