using System.Collections.Generic;
using System.Text;
using Border.Core;
using EmptyHouse.MapGen.Core;
using EmptyHouse.MapGen.Runtime;
using UnityEditor;
using UnityEngine;

namespace EmptyHouse.EditorTools
{
    /// <summary>
    /// 자물쇠·지름길 지표 시드 스윕 — 정의 변경(복도 hop 제거·복도-방 문 허용) 전 기준선을 실측한다.
    /// 생성 파라미터는 런타임과 같은 에셋(SO_MapGenParams)을 쓴다. 블루프린트를 읽기만 하고 아무것도 바꾸지 않는다.
    /// 산출: 자물쇠 후보 수(현행/완화 가정), 채택 수, 지름길 이득 분포(현행 hop vs 방 단위 hop), 열쇠-자물쇠 거리.
    /// </summary>
    public static class MapGenLockSweep
    {
        private const string genParamsPath = "Assets/03. ScriptableObjects/MapGen/SO_MapGenParams.asset"; // 파라미터 단일 출처
        private const int seedCount = 100; // 스윕 시드 수
        private const int baseSeed = 101; // 시작 시드(프리뷰와 동일 기준)

        /// <summary>시드 스윕을 돌려 자물쇠·지름길 지표를 콘솔에 요약한다.</summary>
        [MenuItem("Tools/Map/LockSweepReport")]
        public static void Run()
        {
            Log.D(BuildReport());
        }

        /// <summary>시드 스윕 지표를 계산해 요약 문자열로 돌려준다(콘솔 의존 없이 호출자가 받아 쓰도록).</summary>
        /// <returns>요약 리포트.</returns>
        public static string BuildReport()
        {
            var paramsAsset = AssetDatabase.LoadAssetAtPath<MapGenParamsSO>(genParamsPath);
            var registry = AssetDatabase.LoadAssetAtPath<MapPrefabRegistrySO>("Assets/03. ScriptableObjects/MapGen/SO_MapPrefabRegistry.asset");
            List<RoomTemplateDef> templates = registry.CreateTemplates(); // 템플릿 단일 출처 = 레지스트리 SO(M9-3)
            var generator = new MapGenerator();

            int ok = 0;
            int fail = 0;
            int rerollTotal = 0;
            var roomCounts = new List<int>();
            var corridorCounts = new List<int>();
            var shortcutCandidates = new List<int>();
            var itemDoorCandidates = new List<int>();
            var relaxedCandidates = new List<int>();
            var adoptedShortcuts = new List<int>();
            var adoptedItemDoors = new List<int>();
            var gainHop = new List<int>();
            var gainRoomHop = new List<int>();
            var keyDistances = new List<int>();
            var candidateGainRoomHop = new List<int>();
            var relaxedGainRoomHop = new List<int>();
            var passingCandidates = new List<int>();
            int itemDoorEmptyBehind = 0;

            for (int i = 0; i < seedCount; i++)
            {
                MapGenParams genParams = JsonUtility.FromJson<MapGenParams>(JsonUtility.ToJson(paramsAsset.Params));
                genParams.Seed = baseSeed + i;
                MapGenResult result = generator.Generate(genParams, templates);
                if (!result.Success)
                {
                    fail++;
                    continue;
                }

                ok++;
                rerollTotal += result.RerollCount;
                Measure(result.Blueprint, templates, roomCounts, corridorCounts, shortcutCandidates, itemDoorCandidates,
                    relaxedCandidates, adoptedShortcuts, adoptedItemDoors, gainHop, gainRoomHop, keyDistances,
                    candidateGainRoomHop, relaxedGainRoomHop, passingCandidates, ref itemDoorEmptyBehind);
            }

            var report = new StringBuilder();
            report.AppendLine($"[MapGenLockSweep] 시드 {seedCount}개 — 성공 {ok} 실패 {fail} 리롤합 {rerollTotal}");
            report.AppendLine($"  방 {Summary(roomCounts)} · 복도 {Summary(corridorCounts)}");
            report.AppendLine($"  자물쇠 후보: 지름길 {Summary(shortcutCandidates)} · 중요물품 {Summary(itemDoorCandidates)}");
            report.AppendLine($"  후보(복도-방 문 허용 가정) {Summary(relaxedCandidates)}  ← 현행 대비 증가분이 곧 여유");
            report.AppendLine($"  채택: 지름길 {Summary(adoptedShortcuts)} · 중요물품 {Summary(adoptedItemDoors)}");
            report.AppendLine($"  지름길 이득(현행 전체 hop) {Summary(gainHop)}");
            report.AppendLine($"  지름길 이득(방 단위 hop) {Summary(gainRoomHop)}  ← 새 임계 튜닝 근거");
            report.AppendLine($"  열쇠-자물쇠 거리(방 hop) {Summary(keyDistances)}");
            report.AppendLine($"  중요물품 자물쇠인데 뒤 구역에 백신/기름 실제 배치 0개: {itemDoorEmptyBehind}건");
            report.AppendLine($"  [후보 전체] 방 hop 이득 — 현행 후보 {Summary(candidateGainRoomHop)} {Buckets(candidateGainRoomHop)}");
            report.AppendLine($"  가치 통과(≥2) 지름길 후보 시드당 {Summary(passingCandidates)}");
            report.AppendLine($"  [후보 전체] 방 hop 이득 — 복도-방 허용 {Summary(relaxedGainRoomHop)} {Buckets(relaxedGainRoomHop)}");
            return report.ToString();
        }

        /// <summary>블루프린트 하나에서 지표를 뽑아 누적 목록에 담는다.</summary>
        /// <param name="blueprint">측정 대상.</param>
        /// <param name="templates">템플릿 집합.</param>
        /// <param name="roomCounts">방 수 누적.</param>
        /// <param name="corridorCounts">복도 수 누적.</param>
        /// <param name="shortcutCandidates">지름길 후보 수 누적.</param>
        /// <param name="itemDoorCandidates">중요물품 후보 수 누적.</param>
        /// <param name="relaxedCandidates">복도-방 문 허용 시 후보 수 누적.</param>
        /// <param name="adoptedShortcuts">채택 지름길 수 누적.</param>
        /// <param name="adoptedItemDoors">채택 중요물품 문 수 누적.</param>
        /// <param name="gainHop">현행 정의 이득 누적.</param>
        /// <param name="gainRoomHop">방 단위 정의 이득 누적.</param>
        /// <param name="keyDistances">열쇠-자물쇠 거리 누적.</param>
        /// <param name="itemDoorEmptyBehind">뒤 구역이 빈 중요물품 문 건수.</param>
        private static void Measure(MapBlueprint blueprint, List<RoomTemplateDef> templates,
            List<int> roomCounts, List<int> corridorCounts, List<int> shortcutCandidates, List<int> itemDoorCandidates,
            List<int> relaxedCandidates, List<int> adoptedShortcuts, List<int> adoptedItemDoors,
            List<int> gainHop, List<int> gainRoomHop, List<int> keyDistances,
            List<int> candidateGainRoomHop, List<int> relaxedGainRoomHop, List<int> passingCandidates, ref int itemDoorEmptyBehind)
        {
            int roomCount = blueprint.Rooms.Count;
            var isCorridor = new bool[roomCount];
            int corridors = 0;
            for (int r = 0; r < roomCount; r++)
            {
                isCorridor[r] = FindTemplate(templates, blueprint.Rooms[r].TemplateId).IsCorridor;
                if (isCorridor[r])
                {
                    corridors++;
                }
            }

            roomCounts.Add(roomCount - corridors);
            corridorCounts.Add(corridors);

            int[] hopDepths = DangerGradeCalculator.ComputeDepths(blueprint);
            int[] roomDepths = RoomHopDepths(blueprint, isCorridor);

            int shortcutCand = 0;
            int itemCand = 0;
            int relaxed = 0;
            int passing = 0;
            for (int e = 0; e < blueprint.Edges.Count; e++)
            {
                BlueprintEdge edge = blueprint.Edges[e];
                if (edge.RoomB < 0 || edge.State == EdgeState.BlockedWall)
                {
                    continue;
                }

                bool corridorA = isCorridor[edge.RoomA];
                bool corridorB = isCorridor[edge.RoomB];
                bool loop = ReachabilityAnalyzer.ComputeReachableWithEdgeBlocked(blueprint, e).Count == roomCount;
                // 현행 자격 = 복도-복도만 제외(2026-08-07 완화 반영)
                if (!(corridorA && corridorB))
                {
                    if (loop)
                    {
                        shortcutCand++;
                        int gain = MaxGain(blueprint, e, roomDepths, isCorridor);
                        candidateGainRoomHop.Add(gain);
                        if (gain >= 2)
                        {
                            passing++;
                        }
                    }
                    else
                    {
                        itemCand++;
                    }
                }

                // 완화 가정: 복도-복도만 제외(복도 한쪽만 낀 간선은 문 배치 가능)
                if (!(corridorA && corridorB))
                {
                    relaxed++;
                    if (loop)
                    {
                        relaxedGainRoomHop.Add(MaxGain(blueprint, e, roomDepths, isCorridor));
                    }
                }
            }

            shortcutCandidates.Add(shortcutCand);
            passingCandidates.Add(passing);
            itemDoorCandidates.Add(itemCand);
            relaxedCandidates.Add(relaxed);

            int shortcuts = 0;
            int itemDoors = 0;
            for (int e = 0; e < blueprint.Edges.Count; e++)
            {
                BlueprintEdge edge = blueprint.Edges[e];
                if (edge.State != EdgeState.DoorLocked)
                {
                    continue;
                }

                if (edge.LockKind == LockKind.Shortcut)
                {
                    shortcuts++;
                    gainHop.Add(MaxGain(blueprint, e, hopDepths, null));
                    gainRoomHop.Add(MaxGain(blueprint, e, roomDepths, isCorridor));
                }
                else
                {
                    itemDoors++;
                    if (!HasCriticalItemBehind(blueprint, e))
                    {
                        itemDoorEmptyBehind++;
                    }
                }

                int keyRoom = FindKeyRoom(blueprint, edge.LockNumber);
                if (keyRoom >= 0)
                {
                    int lockRoom = roomDepths[edge.RoomA] < roomDepths[edge.RoomB] ? edge.RoomA : edge.RoomB;
                    keyDistances.Add(RoomHopBetween(blueprint, isCorridor, keyRoom, lockRoom));
                }
            }

            adoptedShortcuts.Add(shortcuts);
            adoptedItemDoors.Add(itemDoors);
        }

        /// <summary>간선을 막았을 때 방들의 깊이 증가 최대값(지름길 이득).</summary>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="edgeIndex">평가 간선.</param>
        /// <param name="baseDepths">기준 깊이.</param>
        /// <param name="isCorridor">복도 여부 배열(null 이면 전체 hop 방식).</param>
        /// <returns>최대 이득.</returns>
        private static int MaxGain(MapBlueprint blueprint, int edgeIndex, int[] baseDepths, bool[] isCorridor)
        {
            BlueprintEdge edge = blueprint.Edges[edgeIndex];
            EdgeState original = edge.State;
            edge.State = EdgeState.BlockedWall;
            int[] without = isCorridor == null ? DangerGradeCalculator.ComputeDepths(blueprint) : RoomHopDepths(blueprint, isCorridor);
            edge.State = original;

            int best = 0;
            for (int r = 0; r < without.Length; r++)
            {
                if (without[r] < 0 || baseDepths[r] < 0 || (isCorridor != null && isCorridor[r]))
                {
                    continue;
                }

                int gain = without[r] - baseDepths[r];
                if (gain > best)
                {
                    best = gain;
                }
            }

            return best;
        }

        /// <summary>입구에서의 방 단위 거리(복도 통과 비용 0) — 제안 정의의 비용 척도.</summary>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="isCorridor">복도 여부 배열.</param>
        /// <returns>방별 거리(미도달 -1).</returns>
        private static int[] RoomHopDepths(MapBlueprint blueprint, bool[] isCorridor)
        {
            return Dijkstra01(blueprint, isCorridor, 0);
        }

        /// <summary>두 방 사이 방 단위 거리.</summary>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="isCorridor">복도 여부 배열.</param>
        /// <param name="from">출발 방.</param>
        /// <param name="to">도착 방.</param>
        /// <returns>거리(미도달 -1).</returns>
        private static int RoomHopBetween(MapBlueprint blueprint, bool[] isCorridor, int from, int to)
        {
            return Dijkstra01(blueprint, isCorridor, from)[to];
        }

        /// <summary>0-1 BFS — 복도 진입 비용 0, 방 진입 비용 1. 잠긴 문은 통과 가능(현행 규약 유지).</summary>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="isCorridor">복도 여부 배열.</param>
        /// <param name="source">출발 노드.</param>
        /// <returns>노드별 거리(미도달 -1).</returns>
        private static int[] Dijkstra01(MapBlueprint blueprint, bool[] isCorridor, int source)
        {
            int n = blueprint.Rooms.Count;
            var adjacency = new List<int>[n];
            for (int i = 0; i < n; i++)
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

            var dist = new int[n];
            for (int i = 0; i < n; i++)
            {
                dist[i] = -1;
            }

            var deque = new LinkedList<int>();
            dist[source] = 0;
            deque.AddFirst(source);
            while (deque.Count > 0)
            {
                int node = deque.First.Value;
                deque.RemoveFirst();
                foreach (int next in adjacency[node])
                {
                    int cost = isCorridor[next] ? 0 : 1;
                    int candidate = dist[node] + cost;
                    if (dist[next] >= 0 && dist[next] <= candidate)
                    {
                        continue;
                    }

                    dist[next] = candidate;
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

            return dist;
        }

        /// <summary>자물쇠 뒤 구역(간선 차단 시 끊기는 쪽)에 백신·기름 스폰이 실제로 있는지.</summary>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="edgeIndex">자물쇠 간선.</param>
        /// <returns>희귀 아이템 존재 여부.</returns>
        private static bool HasCriticalItemBehind(MapBlueprint blueprint, int edgeIndex)
        {
            HashSet<int> front = ReachabilityAnalyzer.ComputeReachableWithEdgeBlocked(blueprint, edgeIndex);
            for (int s = 0; s < blueprint.Spawns.Count; s++)
            {
                BlueprintSpawn spawn = blueprint.Spawns[s];
                bool critical = spawn.Kind == SpawnKind.VaccineAntigen || spawn.Kind == SpawnKind.VaccineSerum
                    || spawn.Kind == SpawnKind.VaccineStabilizer || spawn.Kind == SpawnKind.Fuel;
                if (critical && !front.Contains(spawn.RoomIndex))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>지정 번호 열쇠가 놓인 방을 찾는다.</summary>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="lockNumber">자물쇠 번호.</param>
        /// <returns>방 인덱스(없으면 -1).</returns>
        private static int FindKeyRoom(MapBlueprint blueprint, int lockNumber)
        {
            for (int s = 0; s < blueprint.Spawns.Count; s++)
            {
                if (blueprint.Spawns[s].Kind == SpawnKind.Key && blueprint.Spawns[s].KeyNumber == lockNumber)
                {
                    return blueprint.Spawns[s].RoomIndex;
                }
            }

            return -1;
        }

        /// <summary>TemplateId 로 템플릿을 찾는다.</summary>
        /// <param name="templates">템플릿 집합.</param>
        /// <param name="templateId">찾을 ID.</param>
        /// <returns>템플릿.</returns>
        private static RoomTemplateDef FindTemplate(List<RoomTemplateDef> templates, string templateId)
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

        /// <summary>이득 값별 누적 통과 수를 임계 후보(0~4)로 나열한다 — 임계를 정하는 근거표.</summary>
        /// <param name="values">이득 표본.</param>
        /// <returns>"≥1: n, ≥2: n ..." 형태 문자열.</returns>
        private static string Buckets(List<int> values)
        {
            var text = new StringBuilder("[");
            for (int threshold = 1; threshold <= 4; threshold++)
            {
                int pass = 0;
                for (int i = 0; i < values.Count; i++)
                {
                    if (values[i] >= threshold)
                    {
                        pass++;
                    }
                }

                text.Append($"≥{threshold}:{pass} ");
            }

            text.Append($"/ 총{values.Count}]");
            return text.ToString();
        }

        /// <summary>표본 목록을 "평균 최소~최대 (0인 표본 수)" 형태로 요약한다.</summary>
        /// <param name="values">표본.</param>
        /// <returns>요약 문자열.</returns>
        private static string Summary(List<int> values)
        {
            if (values.Count == 0)
            {
                return "표본 없음";
            }

            int min = int.MaxValue;
            int max = int.MinValue;
            int sum = 0;
            int zeros = 0;
            for (int i = 0; i < values.Count; i++)
            {
                sum += values[i];
                min = Mathf.Min(min, values[i]);
                max = Mathf.Max(max, values[i]);
                if (values[i] == 0)
                {
                    zeros++;
                }
            }

            return $"평균 {(float)sum / values.Count:F2} 범위 {min}~{max} (0인 표본 {zeros}/{values.Count})";
        }
    }
}
