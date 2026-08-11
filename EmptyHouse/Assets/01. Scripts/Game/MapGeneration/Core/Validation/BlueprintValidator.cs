using System.Collections.Generic;
using Border.Core;

namespace EmptyHouse.MapGen.Core
{
    /// <summary>
    /// MapBlueprint 데이터 단계 검증(7절) — 하나라도 실패하면 호출자가 시드를 리롤한다(X1).
    /// 프리팹 인스턴스화 없이 실행된다(AC-03).
    /// </summary>
    public sealed class BlueprintValidator
    {
        private List<int>[] adjacency; // 방 인접 리스트(연결 간선, 간선 인덱스 순) — Validate 진입 시 재구축

        /// <summary>
        /// 4종 패스를 모두 실행해 리포트를 만든다.
        /// B등급 파훼 쌍(발전기) 미충족은 실패가 아니라 경고로 기록한다(6절·X6·AC-16).
        /// </summary>
        /// <param name="blueprint">검증할 블루프린트.</param>
        /// <param name="genParams">생성 파라미터(지름길 임계·Listener 보장 거리).</param>
        /// <param name="templates">템플릿 집합 — 지름길 가치의 방 단위 거리 계산에 복도 판별이 필요하다.</param>
        /// <returns>패스별 통과/실패와 사유를 담은 리포트.</returns>
        public ValidationReport Validate(MapBlueprint blueprint, MapGenParams genParams, IReadOnlyList<RoomTemplateDef> templates)
        {
            Log.D("[BlueprintValidator] Validate");
            BuildAdjacency(blueprint);
            var report = new ValidationReport();
            report.EssentialsReachable = CheckEssentialsReachable(blueprint, report);
            report.KeyInvariantHolds = CheckKeyInvariant(blueprint, report);
            report.HardlockPairsHold = CheckHardlockPairs(blueprint, genParams, report);
            report.ShortcutValuesHold = CheckShortcutValues(blueprint, genParams, templates, report);
            report.FloorReachabilityHolds = CheckFloorReachability(blueprint, report);

            // 사이클 소속 방 비율 목표 미달은 기하 후보 소진(베스트에포트) — 실패가 아니라 경고만(X6)
            if (blueprint.Meta.CycleRoomPercentAchieved + 0.5f < genParams.CycleRoomPercent)
            {
                report.Warnings.Add($"X6 경고: 사이클 소속 방 {blueprint.Meta.CycleRoomPercentAchieved:F1}% — 목표 {genParams.CycleRoomPercent}% 미달(기하 후보 소진, 베스트에포트)");
            }

            // 탈출문·벽장 미달도 경고(X6) — 잎 방·방 수가 모자란 배치 사정이지 그래프 결함이 아니다
            int returnExits = 0;
            for (int e = 0; e < blueprint.Edges.Count; e++)
            {
                if (blueprint.Edges[e].State == EdgeState.ReturnExit)
                {
                    returnExits++;
                }
            }

            if (returnExits < genParams.ReturnExitCount)
            {
                report.Warnings.Add($"X6 경고: 탈출문 {returnExits}/{genParams.ReturnExitCount} — 바깥 향 봉인 소켓을 가진 잎 방 부족");
            }

            int wardrobes = 0;
            for (int s = 0; s < blueprint.Spawns.Count; s++)
            {
                if (blueprint.Spawns[s].Kind == SpawnKind.Wardrobe)
                {
                    wardrobes++;
                }
            }

            if (wardrobes < genParams.WardrobeCount)
            {
                report.Warnings.Add($"X6 경고: 벽장 {wardrobes}/{genParams.WardrobeCount} — 배치 가능 방 부족");
            }

            report.AllPassed = report.EssentialsReachable && report.KeyInvariantHolds
                && report.HardlockPairsHold && report.ShortcutValuesHold && report.FloorReachabilityHolds;
            return report;
        }

        /// <summary>
        /// 패스 5(M9-5) — 층 도달성: 전 방이 입구에서 도달 가능하고(고립 0 — AC-06 기준),
        /// 비시드 층(층 구간 메타의 첫 층 이외)마다 그 층에 닿는 수직 간선이 1개 이상이어야 한다.
        /// </summary>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="report">결과를 기록할 리포트.</param>
        /// <returns>통과 여부.</returns>
        private bool CheckFloorReachability(MapBlueprint blueprint, ValidationReport report)
        {
            bool passed = true;
            int[] hops = DangerGradeCalculator.ComputeHopDistances(blueprint);
            for (int r = 0; r < hops.Length; r++)
            {
                if (hops[r] < 0)
                {
                    report.FailReasons.Add($"패스5: 방 {r}(층 {blueprint.Rooms[r].FloorIndex}) 이 입구에서 미도달 — 고립");
                    passed = false;
                }
            }

            for (int f = 0; f < blueprint.Floors.Count; f++)
            {
                BlueprintFloor floor = blueprint.Floors[f];
                if (floor.FloorIndex == blueprint.Rooms[0].FloorIndex)
                {
                    continue; // 시드 층(입구 보유)은 수직 간선 불필요
                }

                bool hasVertical = false;
                for (int e = 0; e < blueprint.Edges.Count && !hasVertical; e++)
                {
                    BlueprintEdge edge = blueprint.Edges[e];
                    hasVertical = blueprint.IsVerticalEdge(edge)
                        && (blueprint.Rooms[edge.RoomA].FloorIndex == floor.FloorIndex
                            || blueprint.Rooms[edge.RoomB].FloorIndex == floor.FloorIndex);
                }

                if (!hasVertical)
                {
                    report.FailReasons.Add($"패스5: 층 {floor.FloorIndex} 에 수직 간선이 없다 — 층 진입 불가");
                    passed = false;
                }
            }

            return passed;
        }

        /// <summary>패스 1 — 필수 아이템(백신·기름·모든 열쇠)이 전부 도달 가능한지 검사한다(7절 1).</summary>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="report">결과를 기록할 리포트.</param>
        /// <returns>통과 여부.</returns>
        private bool CheckEssentialsReachable(MapBlueprint blueprint, ValidationReport report)
        {
            Log.D("[BlueprintValidator] CheckEssentialsReachable");

            // 전 자물쇠를 번호순으로 열 수 있다는 전제의 도달 집합 — 순서 성립은 패스 2가 담당
            HashSet<int> reachable = ReachabilityAnalyzer.ComputeReachableRooms(blueprint, int.MaxValue);
            bool passed = true;
            bool hasAntigen = false, hasSerum = false, hasStabilizer = false, hasOil = false;
            for (int s = 0; s < blueprint.Spawns.Count; s++)
            {
                BlueprintSpawn spawn = blueprint.Spawns[s];
                switch (spawn.Kind)
                {
                    case SpawnKind.VaccineAntigen: hasAntigen = true; break;
                    case SpawnKind.VaccineSerum: hasSerum = true; break;
                    case SpawnKind.VaccineStabilizer: hasStabilizer = true; break;
                    case SpawnKind.Fuel: hasOil = true; break;
                    case SpawnKind.Key: break;
                    default: continue;
                }

                if (!reachable.Contains(spawn.RoomIndex))
                {
                    report.FailReasons.Add($"패스1: 필수 아이템({spawn.Kind}) 방 {spawn.RoomIndex} 미도달");
                    passed = false;
                }
            }

            // 부재 = 도달 불가와 동급 — 백신 3종·기름은 존재 자체가 전제다
            if (!hasAntigen) { report.FailReasons.Add("패스1: 백신 항원 부재"); passed = false; }
            if (!hasSerum) { report.FailReasons.Add("패스1: 백신 혈청 부재"); passed = false; }
            if (!hasStabilizer) { report.FailReasons.Add("패스1: 백신 안정제 부재"); passed = false; }
            if (!hasOil) { report.FailReasons.Add("패스1: 기름 부재"); passed = false; }
            return passed;
        }

        /// <summary>패스 2 — 열쇠_i ∈ R_i 전수 검사(7절 2·4-3절·AC-08).</summary>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="report">결과를 기록할 리포트.</param>
        /// <returns>통과 여부.</returns>
        private bool CheckKeyInvariant(MapBlueprint blueprint, ValidationReport report)
        {
            Log.D("[BlueprintValidator] CheckKeyInvariant");
            int lockCount = 0;
            for (int e = 0; e < blueprint.Edges.Count; e++)
            {
                if (blueprint.Edges[e].LockNumber > lockCount)
                {
                    lockCount = blueprint.Edges[e].LockNumber;
                }
            }

            // 열쇠 ↔ 자물쇠 페어링은 KeyNumber 가 명시한다(스폰 목록 순서에 의존하지 않는다)
            var keyRoomsByNumber = new Dictionary<int, int>();
            int keyCount = 0;
            for (int s = 0; s < blueprint.Spawns.Count; s++)
            {
                if (blueprint.Spawns[s].Kind != SpawnKind.Key)
                {
                    continue;
                }

                keyCount++;
                if (!keyRoomsByNumber.ContainsKey(blueprint.Spawns[s].KeyNumber))
                {
                    keyRoomsByNumber.Add(blueprint.Spawns[s].KeyNumber, blueprint.Spawns[s].RoomIndex);
                }
            }

            if (keyCount != lockCount)
            {
                report.FailReasons.Add($"패스2: 열쇠 수({keyCount}) ≠ 자물쇠 수({lockCount})");
                return false;
            }

            bool passed = true;
            for (int i = 1; i <= lockCount; i++)
            {
                if (!keyRoomsByNumber.TryGetValue(i, out int keyRoom))
                {
                    report.FailReasons.Add($"패스2: 열쇠_{i} 부재(KeyNumber 누락 또는 중복)");
                    passed = false;
                    continue;
                }

                HashSet<int> reachable = ReachabilityAnalyzer.ComputeReachableRooms(blueprint, i);
                if (!reachable.Contains(keyRoom))
                {
                    report.FailReasons.Add($"패스2: 열쇠_{i} 방 {keyRoom} ∉ R_{i}(AC-08)");
                    passed = false;
                }
            }

            return passed;
        }

        /// <summary>
        /// 패스 3 — 파훼 쌍 검사(7절 3·6절). A등급(Listener 길목↔투척물, HerdArea↔사체 충전소)은 실패,
        /// B등급(Watcher 어둠 구간↔발전기)은 경고만 기록한다(X6).
        /// </summary>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="genParams">생성 파라미터(Listener 보장 거리).</param>
        /// <param name="report">결과를 기록할 리포트.</param>
        /// <returns>A등급 통과 여부.</returns>
        private bool CheckHardlockPairs(MapBlueprint blueprint, MapGenParams genParams, ValidationReport report)
        {
            Log.D("[BlueprintValidator] CheckHardlockPairs");

            // 관문 = 좀비 배치 "방" — 그 방을 통과하지 않는 도달 구역에 파훼 수단이 있어야 한다
            // (SpawnDistributor 의 배치 보장과 동일 규칙 — 분배기와 다른 잣대를 쓰면 보장분을 오검출한다)
            List<int> listenerRooms = CollectSpawnRooms(blueprint, SpawnKind.ZombieListener);
            List<int> throwableRooms = CollectSpawnRooms(blueprint, SpawnKind.Throwable);
            List<int> herdRooms = CollectSpawnRooms(blueprint, SpawnKind.HerdArea);
            List<int> stationRooms = CollectSpawnRooms(blueprint, SpawnKind.CorpseStation);
            List<int> watcherRooms = CollectSpawnRooms(blueprint, SpawnKind.ZombieWatcher);
            List<int> generatorRooms = CollectSpawnRooms(blueprint, SpawnKind.Generator);

            bool passed = true;
            for (int l = 0; l < listenerRooms.Count; l++)
            {
                HashSet<int> front = ReachableExcludingRoom(blueprint, listenerRooms[l]);
                int[] dist = DistancesFrom(blueprint, listenerRooms[l]);
                bool satisfied = false;
                for (int t = 0; t < throwableRooms.Count && !satisfied; t++)
                {
                    satisfied = front.Contains(throwableRooms[t]) && dist[throwableRooms[t]] >= 0
                        && dist[throwableRooms[t]] <= genParams.ListenerCounterDist;
                }

                if (!satisfied)
                {
                    report.FailReasons.Add($"패스3: Listener 방 {listenerRooms[l]} 관문 앞 보장 거리({genParams.ListenerCounterDist}) 안에 투척물 없음(A등급)");
                    passed = false;
                }
            }

            for (int h = 0; h < herdRooms.Count; h++)
            {
                HashSet<int> front = ReachableExcludingRoom(blueprint, herdRooms[h]);
                bool satisfied = false;
                for (int t = 0; t < stationRooms.Count && !satisfied; t++)
                {
                    satisfied = front.Contains(stationRooms[t]);
                }

                if (!satisfied)
                {
                    report.FailReasons.Add($"패스3: HerdArea 방 {herdRooms[h]} 앞 도달 구역에 사체 충전소 없음(A등급)");
                    passed = false;
                }
            }

            // B등급 — 발전기가 Watcher 방 자신 또는 인접 방에 없으면 경고만(X6)
            for (int w = 0; w < watcherRooms.Count; w++)
            {
                bool satisfied = generatorRooms.Contains(watcherRooms[w]);
                List<int> neighbors = adjacency[watcherRooms[w]];
                for (int n = 0; n < neighbors.Count && !satisfied; n++)
                {
                    satisfied = generatorRooms.Contains(neighbors[n]);
                }

                if (!satisfied)
                {
                    report.Warnings.Add($"X6 경고: Watcher 방 {watcherRooms[w]} 길목에 발전기 없음(B등급 미충족 허용)");
                }
            }

            return passed;
        }

        /// <summary>패스 4 — 채택된 지름길 전부 귀환 단축 이득 ≥ ShortcutValueMin 검사(7절 4·AC-10). 척도는 배치와 동일한 방 단위 거리.</summary>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="genParams">생성 파라미터(지름길 임계).</param>
        /// <param name="templates">템플릿 집합(복도 판별).</param>
        /// <param name="report">결과를 기록할 리포트.</param>
        /// <returns>통과 여부.</returns>
        private bool CheckShortcutValues(MapBlueprint blueprint, MapGenParams genParams, IReadOnlyList<RoomTemplateDef> templates, ValidationReport report)
        {
            Log.D("[BlueprintValidator] CheckShortcutValues");
            bool[] isCorridor = RoomHopMetric.CorridorFlags(blueprint, templates);
            int[] baseDepths = RoomHopMetric.Distances(blueprint, isCorridor, 0);
            bool passed = true;
            for (int e = 0; e < blueprint.Edges.Count; e++)
            {
                BlueprintEdge edge = blueprint.Edges[e];
                if (edge.RoomB < 0 || edge.State != EdgeState.DoorLocked)
                {
                    continue;
                }

                // 루프 간선(차단해도 전 방 도달)만 지름길 — 브리지 자물쇠(중요 물품 문)는 가치 규칙 비대상
                HashSet<int> front = ReachabilityAnalyzer.ComputeReachableWithEdgeBlocked(blueprint, e);
                if (front.Count != blueprint.Rooms.Count)
                {
                    continue;
                }

                int gain = RoomHopMetric.ShortcutValue(blueprint, e, isCorridor, baseDepths);
                if (gain < genParams.ShortcutValueMin)
                {
                    report.FailReasons.Add($"패스4: 지름길 자물쇠_{edge.LockNumber} 귀환 단축 이득({gain}) < 임계({genParams.ShortcutValueMin})(AC-10)");
                    passed = false;
                }
            }

            return passed;
        }

        /// <summary>연결 간선(봉인·막힌 벽 제외)으로 인접 리스트를 재구축한다(간선 인덱스 순 — 결정론).</summary>
        /// <param name="blueprint">대상 블루프린트.</param>
        private void BuildAdjacency(MapBlueprint blueprint)
        {
            int roomCount = blueprint.Rooms.Count;
            adjacency = new List<int>[roomCount];
            for (int r = 0; r < roomCount; r++)
            {
                adjacency[r] = new List<int>();
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
        }

        /// <summary>지정 종류 스폰이 있는 방 목록(중복 제거)을 수집한다.</summary>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="kind">스폰 종류.</param>
        /// <returns>방 인덱스 목록.</returns>
        private static List<int> CollectSpawnRooms(MapBlueprint blueprint, SpawnKind kind)
        {
            var rooms = new List<int>();
            for (int s = 0; s < blueprint.Spawns.Count; s++)
            {
                if (blueprint.Spawns[s].Kind == kind && !rooms.Contains(blueprint.Spawns[s].RoomIndex))
                {
                    rooms.Add(blueprint.Spawns[s].RoomIndex);
                }
            }

            return rooms;
        }

        /// <summary>입구(방 0)에서 blocked 방을 통과하지 않는 도달 집합(관문 = 방 판정 — 분배기 미러).</summary>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="blocked">통과 금지 방.</param>
        /// <returns>도달 가능 방 집합(blocked 제외).</returns>
        private HashSet<int> ReachableExcludingRoom(MapBlueprint blueprint, int blocked)
        {
            var reachable = new HashSet<int>();
            if (blocked == 0)
            {
                return reachable;
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
                    if (neighbors[i] != blocked && reachable.Add(neighbors[i]))
                    {
                        queue.Enqueue(neighbors[i]);
                    }
                }
            }

            return reachable;
        }

        /// <summary>start 방 기준 그래프 거리(BFS, 미도달 = -1).</summary>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="start">시작 방.</param>
        /// <returns>방별 거리 배열.</returns>
        private int[] DistancesFrom(MapBlueprint blueprint, int start)
        {
            var dist = new int[blueprint.Rooms.Count];
            for (int r = 0; r < dist.Length; r++)
            {
                dist[r] = -1;
            }

            var queue = new Queue<int>();
            dist[start] = 0;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int room = queue.Dequeue();
                List<int> neighbors = adjacency[room];
                for (int i = 0; i < neighbors.Count; i++)
                {
                    if (dist[neighbors[i]] < 0)
                    {
                        dist[neighbors[i]] = dist[room] + 1;
                        queue.Enqueue(neighbors[i]);
                    }
                }
            }

            return dist;
        }
    }
}
