using System.Collections.Generic;

namespace EmptyHouse.MapGen.Core
{
    /// <summary>
    /// 사이클 지표 유틸(3절 3 루프) — 브리지 판별(low-link)로 "사이클에 속한 방" 비율을 계산한다.
    /// 생성기(CycleRoomPercent 목표 판정)·검증기(X6 경고)·미리보기 툴이 공유하는 순수 함수.
    /// </summary>
    public static class CycleMetrics
    {
        /// <summary>
        /// 연결 간선(비봉인)의 브리지 여부를 판별한다 — 반복 DFS(low-link). 봉인·미연결 간선은 항상 false.
        /// 브리지 = 제거하면 그래프가 끊기는 간선, 즉 사이클에 속하지 않는 간선.
        /// </summary>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <returns>간선 인덱스별 브리지 여부 배열(Edges 와 정렬).</returns>
        public static bool[] FindBridgeEdges(MapBlueprint blueprint)
        {
            int n = blueprint.Rooms.Count;
            var adjacency = new List<(int to, int edge)>[n];
            for (int i = 0; i < n; i++)
            {
                adjacency[i] = new List<(int, int)>();
            }

            for (int e = 0; e < blueprint.Edges.Count; e++)
            {
                BlueprintEdge edge = blueprint.Edges[e];
                if (edge.RoomB < 0 || edge.State == EdgeState.BlockedWall)
                {
                    continue;
                }

                adjacency[edge.RoomA].Add((edge.RoomB, e));
                adjacency[edge.RoomB].Add((edge.RoomA, e));
            }

            var disc = new int[n];
            var low = new int[n];
            for (int i = 0; i < n; i++)
            {
                disc[i] = -1;
            }

            var isBridge = new bool[blueprint.Edges.Count];
            int timer = 0;
            var stack = new Stack<(int v, int parentEdge, int iter)>();
            for (int root = 0; root < n; root++)
            {
                if (disc[root] != -1)
                {
                    continue;
                }

                stack.Push((root, -1, 0));
                while (stack.Count > 0)
                {
                    (int v, int parentEdge, int iter) = stack.Pop();
                    if (iter == 0)
                    {
                        disc[v] = low[v] = timer++;
                    }

                    if (iter < adjacency[v].Count)
                    {
                        stack.Push((v, parentEdge, iter + 1));
                        (int to, int e) = adjacency[v][iter];
                        if (e == parentEdge)
                        {
                            continue; // 트리 간선 역주행 금지 — 평행 간선은 간선 인덱스가 달라 후방 간선으로 취급된다
                        }

                        if (disc[to] == -1)
                        {
                            stack.Push((to, e, 0));
                        }
                        else if (low[v] > disc[to])
                        {
                            low[v] = disc[to];
                        }
                    }
                    else if (parentEdge != -1)
                    {
                        // v 의 자식 순회 완료 — low 를 부모로 전파하고 브리지 판정
                        BlueprintEdge pe = blueprint.Edges[parentEdge];
                        int parent = pe.RoomA == v ? pe.RoomB : pe.RoomA;
                        if (low[parent] > low[v])
                        {
                            low[parent] = low[v];
                        }

                        if (low[v] > disc[parent])
                        {
                            isBridge[parentEdge] = true;
                        }
                    }
                }
            }

            return isBridge;
        }

        /// <summary>
        /// 사이클 소속 방 비율 %를 계산한다 — 비브리지 연결 간선을 하나라도 가진 방 / 비복도 방(입구 포함).
        /// </summary>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="templates">템플릿 목록(복도 판정용).</param>
        /// <returns>0~100 비율.</returns>
        public static float ComputeCycleRoomPercent(MapBlueprint blueprint, IReadOnlyList<RoomTemplateDef> templates)
        {
            return ComputeCycleRoomPercent(blueprint, templates, 0);
        }

        /// <summary>
        /// roomStart 이후 방만 집계하는 층 스코프 비율(M9-5) — 브리지 판별은 전 그래프 기준(층간 루프도 사이클로 인정),
        /// 분모·분자만 현재 층 방으로 한정한다. roomStart 0 = 전역과 동일(v1 하위호환).
        /// </summary>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="templates">템플릿 목록(복도 판정용).</param>
        /// <param name="roomStart">집계 시작 방 인덱스.</param>
        /// <returns>0~100 비율.</returns>
        public static float ComputeCycleRoomPercent(MapBlueprint blueprint, IReadOnlyList<RoomTemplateDef> templates, int roomStart)
        {
            var corridorIds = new HashSet<string>();
            for (int t = 0; t < templates.Count; t++)
            {
                if (templates[t].IsCorridor)
                {
                    corridorIds.Add(templates[t].TemplateId);
                }
            }

            bool[] isBridge = FindBridgeEdges(blueprint);
            int n = blueprint.Rooms.Count;
            var onCycle = new bool[n];
            for (int e = 0; e < blueprint.Edges.Count; e++)
            {
                BlueprintEdge edge = blueprint.Edges[e];
                if (edge.RoomB < 0 || edge.State == EdgeState.BlockedWall || isBridge[e])
                {
                    continue;
                }

                onCycle[edge.RoomA] = true;
                onCycle[edge.RoomB] = true;
            }

            int rooms = 0;
            int cycleRooms = 0;
            for (int r = roomStart; r < n; r++)
            {
                if (corridorIds.Contains(blueprint.Rooms[r].TemplateId))
                {
                    continue;
                }

                rooms++;
                if (onCycle[r])
                {
                    cycleRooms++;
                }
            }

            return rooms == 0 ? 0f : 100f * cycleRooms / rooms;
        }
    }
}
