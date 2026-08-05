using System.Collections.Generic;
using Border.Core;

namespace EmptyHouse.MapGen.Core
{
    /// <summary>
    /// 좀비·아이템 스폰 분배(5절) — 위험 등급 예산 분배 + 마커 채우기.
    /// 배치 가능 위치는 마커가 보장하므로(2절) 이 클래스는 마커 후보 중 선택만 한다.
    /// </summary>
    public sealed class SpawnDistributor
    {
        private MapGenParams paramsCache; // 진행 중 파라미터 — 프라이빗 단계 시그니처 유지용
        private bool failed; // A등급 보장 실패 플래그(void 단계 → TryDistribute 반환값)
        private readonly List<RoomTemplateDef> roomTemplates = new List<RoomTemplateDef>(); // 방 인덱스 → 템플릿
        private readonly List<int> herdRooms = new List<int>(); // HerdArea 마커 보유 방(위장 무대)
        private List<int>[] adjacency; // 방 인접 리스트(연결 간선, 간선 인덱스 순 구축)
        private int[] treeParent; // 입구 기준 BFS 트리 부모(백신 가지 판정)

        /// <summary>
        /// 좀비·아이템·설비 스폰을 분배해 blueprint 의 Spawns 를 채운다(열쇠는 LockKeyPlacer 소관).
        /// </summary>
        /// <param name="rng">단일 난수 스트림.</param>
        /// <param name="genParams">생성 파라미터.</param>
        /// <param name="blueprint">레이아웃·열쇠 배치가 끝난 블루프린트.</param>
        /// <param name="dangerDepths">방별 위험 깊이.</param>
        /// <param name="templates">템플릿 집합(마커 조회용).</param>
        /// <returns>분배 성공 여부 — 실패 시 호출자가 리롤.</returns>
        public bool TryDistribute(DeterministicRng rng, MapGenParams genParams, MapBlueprint blueprint, int[] dangerDepths, IReadOnlyList<RoomTemplateDef> templates)
        {
            Log.D("[SpawnDistributor] TryDistribute");
            paramsCache = genParams;
            failed = false;
            BuildCaches(blueprint, templates);

            DistributeZombies(rng, blueprint, dangerDepths, templates);
            if (!failed)
            {
                DistributeItems(rng, genParams, blueprint, dangerDepths, templates);
            }

            if (!failed)
            {
                DistributeFacilities(rng, blueprint, dangerDepths, templates);
            }

            return !failed;
        }

        /// <summary>
        /// 좀비 예산을 위험 등급별 밀도로 분배한다(5절). 타입 규칙 — Watcher: 어두움 태그 방 +
        /// 길목 GeneratorSlot 세트 / Listener: 관문 앞 투척물 보장(6절 불변식과 연동) / 나머지 Walker.
        /// HerdArea 에는 Walker 단독 무리(Listener 미포함). 활성 타입은 EnabledZombieTypes 로 게이트.
        /// 등급 밴드는 최대 깊이 3등분(하위 안전 / 중위 중간 / 상위 위험)으로 해석한다.
        /// </summary>
        /// <param name="rng">단일 난수 스트림.</param>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="dangerDepths">방별 위험 깊이.</param>
        /// <param name="templates">템플릿 집합.</param>
        private void DistributeZombies(DeterministicRng rng, MapBlueprint blueprint, int[] dangerDepths, IReadOnlyList<RoomTemplateDef> templates)
        {
            Log.D("[SpawnDistributor] DistributeZombies");

            // 위장 무대 — HerdArea 마커 방마다 무대 등록 + Walker 단독 무리
            for (int r = 0; r < blueprint.Rooms.Count; r++)
            {
                MarkerDef[] markers = roomTemplates[r].Markers;
                for (int m = 0; m < markers.Length; m++)
                {
                    if (markers[m].Kind != MarkerKind.HerdArea)
                    {
                        continue;
                    }

                    // 성립 판정(6절 A등급 선제) — 앞 구역에 충전소 후보가 없으면 위장 무대로 쓰지 않는다.
                    // 마커는 "놓아도 되는 자리"(2절 — 후보)지 의무가 아니다. 스킵된 방은 일반 밀도로 채워진다.
                    if (!HasStationCandidateInFront(blueprint, r))
                    {
                        Log.D($"[SpawnDistributor] HerdArea 스킵: 방 {r} 앞 구역에 CorpseStationSlot 없음 — 일반 방으로 배치");
                        continue;
                    }

                    herdRooms.Add(r);
                    blueprint.Spawns.Add(new BlueprintSpawn { RoomIndex = r, MarkerId = markers[m].Id, Kind = SpawnKind.HerdArea, WanderRadiusCells = 0f });

                    if ((paramsCache.EnabledZombieTypes & ZombieTypeMask.Walker) == 0)
                    {
                        continue;
                    }

                    int herdCount = rng.Next(paramsCache.HerdZombieCountMin, paramsCache.HerdZombieCountMax + 1);
                    for (int k = 0; k < herdCount; k++)
                    {
                        int markerId = PickMarker(rng, r, MarkerKind.ZombieSpawn, out float wander, zombieMask: ZombieTypeMask.Walker);
                        if (markerId < 0)
                        {
                            break;
                        }

                        blueprint.Spawns.Add(new BlueprintSpawn { RoomIndex = r, MarkerId = markerId, Kind = SpawnKind.ZombieWalker, WanderRadiusCells = wander });
                    }
                }
            }

            // 일반 밀도 배치 — 입구(0)와 위장 무대 방 제외
            int maxDepth = 0;
            for (int r = 0; r < dangerDepths.Length; r++)
            {
                if (dangerDepths[r] > maxDepth)
                {
                    maxDepth = dangerDepths[r];
                }
            }

            for (int r = 1; r < blueprint.Rooms.Count; r++)
            {
                if (herdRooms.Contains(r))
                {
                    continue;
                }

                int min, max;
                if (dangerDepths[r] * 3 <= maxDepth)
                {
                    min = paramsCache.ZombieDensitySafeMin;
                    max = paramsCache.ZombieDensitySafeMax;
                }
                else if (dangerDepths[r] * 3 <= maxDepth * 2)
                {
                    min = paramsCache.ZombieDensityMidMin;
                    max = paramsCache.ZombieDensityMidMax;
                }
                else
                {
                    min = paramsCache.ZombieDensityDangerMin;
                    max = paramsCache.ZombieDensityDangerMax;
                }

                int count = rng.Next(min, max + 1);
                for (int k = 0; k < count; k++)
                {
                    int markerId = PickMarker(rng, r, MarkerKind.ZombieSpawn, out float wander, zombieMask: paramsCache.EnabledZombieTypes);
                    if (markerId < 0)
                    {
                        break;
                    }

                    ZombieTypeMask allowed = FindMarker(r, markerId).ZombieMask & paramsCache.EnabledZombieTypes;
                    SpawnKind kind;
                    if (roomTemplates[r].Tags.HasFlag(RoomTagMask.Dark) && (allowed & ZombieTypeMask.Watcher) != 0)
                    {
                        kind = SpawnKind.ZombieWatcher;
                    }
                    else if ((allowed & ZombieTypeMask.Listener) != 0 && rng.Next(100) < paramsCache.ListenerRatioPercent)
                    {
                        kind = SpawnKind.ZombieListener;
                    }
                    else if ((allowed & ZombieTypeMask.Walker) != 0)
                    {
                        kind = SpawnKind.ZombieWalker;
                    }
                    else if ((allowed & ZombieTypeMask.Listener) != 0)
                    {
                        kind = SpawnKind.ZombieListener;
                    }
                    else
                    {
                        kind = SpawnKind.ZombieWatcher;
                    }

                    blueprint.Spawns.Add(new BlueprintSpawn { RoomIndex = r, MarkerId = markerId, Kind = kind, WanderRadiusCells = wander });
                }
            }
        }

        /// <summary>
        /// 아이템을 분배한다(5절) — 백신 3종은 서로 다른 고위험 가지(서브트리)에 분산(AC-12),
        /// 투척물은 Listener 보장분 + 회피 예산분(D4 — 외출마다 재배치), 기름은 깊은 구역 집중, 스크랩은 깊이 비례.
        /// "다른 가지" = 어떤 백신 방도 다른 백신 방의 트리 조상(입구 경로 경유지)이 아니다.
        /// Listener 관문 = Listener 배치 방 — 그 방을 통과하지 않는 도달 구역 안, 보장 거리 이내에 투척물(A등급 — 불가 시 실패).
        /// </summary>
        /// <param name="rng">단일 난수 스트림.</param>
        /// <param name="genParams">생성 파라미터(Listener 보장 거리).</param>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="dangerDepths">방별 위험 깊이.</param>
        /// <param name="templates">템플릿 집합.</param>
        private void DistributeItems(DeterministicRng rng, MapGenParams genParams, MapBlueprint blueprint, int[] dangerDepths, IReadOnlyList<RoomTemplateDef> templates)
        {
            Log.D("[SpawnDistributor] DistributeItems");

            // 백신 3종 — 깊이 가중, 상호 트리 조상 금지
            var vaccineKinds = new[] { SpawnKind.VaccineAntigen, SpawnKind.VaccineSerum, SpawnKind.VaccineStabilizer };
            var chosenVaccineRooms = new List<int>();
            var pool = new List<(int room, int weight)>();
            for (int v = 0; v < vaccineKinds.Length; v++)
            {
                pool.Clear();
                for (int r = 1; r < blueprint.Rooms.Count; r++)
                {
                    if (chosenVaccineRooms.Contains(r) || !HasItemMarker(r, ItemCategoryMask.Vaccine))
                    {
                        continue;
                    }

                    bool nested = false;
                    for (int c = 0; c < chosenVaccineRooms.Count && !nested; c++)
                    {
                        nested = IsOnEntrancePath(r, chosenVaccineRooms[c]) || IsOnEntrancePath(chosenVaccineRooms[c], r);
                    }

                    if (!nested)
                    {
                        pool.Add((r, dangerDepths[r] < 1 ? 1 : dangerDepths[r]));
                    }
                }

                if (pool.Count == 0)
                {
                    failed = true;
                    return;
                }

                int room = PickWeighted(rng, pool);
                chosenVaccineRooms.Add(room);
                int markerId = PickMarker(rng, room, MarkerKind.ItemSpawn, out _, itemMask: ItemCategoryMask.Vaccine);
                blueprint.Spawns.Add(new BlueprintSpawn { RoomIndex = room, MarkerId = markerId, Kind = vaccineKinds[v], WanderRadiusCells = 0f });
            }

            // 투척물 — Listener 관문 보장분(A등급)
            var listenerRooms = new List<int>();
            var throwableRooms = new List<int>();
            for (int s = 0; s < blueprint.Spawns.Count; s++)
            {
                if (blueprint.Spawns[s].Kind == SpawnKind.ZombieListener && !listenerRooms.Contains(blueprint.Spawns[s].RoomIndex))
                {
                    listenerRooms.Add(blueprint.Spawns[s].RoomIndex);
                }
            }

            for (int l = 0; l < listenerRooms.Count; l++)
            {
                HashSet<int> front = ReachableExcludingRoom(blueprint, listenerRooms[l]);
                int[] dist = DistancesFrom(blueprint, listenerRooms[l]);

                bool satisfied = false;
                for (int t = 0; t < throwableRooms.Count && !satisfied; t++)
                {
                    satisfied = front.Contains(throwableRooms[t]) && dist[throwableRooms[t]] >= 0 && dist[throwableRooms[t]] <= genParams.ListenerCounterDist;
                }

                if (satisfied)
                {
                    continue;
                }

                var candidates = new List<int>();
                for (int r = 0; r < blueprint.Rooms.Count; r++)
                {
                    if (front.Contains(r) && dist[r] >= 0 && dist[r] <= genParams.ListenerCounterDist && HasItemMarker(r, ItemCategoryMask.Throwable))
                    {
                        candidates.Add(r);
                    }
                }

                if (candidates.Count == 0)
                {
                    // 재배치 시도(6절 A등급) — 투척물을 앞 구역에 둘 수 없는 관문이면 좀비 쪽을 재배치:
                    // 그 방의 Listener 를 Walker 로 강등한다(입구 인접 방 등 구조적 불가 케이스).
                    if (!TryDowngradeListeners(blueprint, listenerRooms[l]))
                    {
                        failed = true;
                        return;
                    }

                    Log.D($"[SpawnDistributor] Listener 재배치: 방 {listenerRooms[l]} 앞 구역 투척물 불가 → Walker 강등");
                    continue;
                }

                int room = candidates[rng.Next(candidates.Count)];
                int markerId = PickMarker(rng, room, MarkerKind.ItemSpawn, out _, itemMask: ItemCategoryMask.Throwable);
                blueprint.Spawns.Add(new BlueprintSpawn { RoomIndex = room, MarkerId = markerId, Kind = SpawnKind.Throwable, WanderRadiusCells = 0f });
                throwableRooms.Add(room);
            }

            // 투척물 — 회피 예산분(D4)
            var throwableCapable = new List<int>();
            for (int r = 1; r < blueprint.Rooms.Count; r++)
            {
                if (HasItemMarker(r, ItemCategoryMask.Throwable))
                {
                    throwableCapable.Add(r);
                }
            }

            for (int k = 0; k < genParams.ThrowableBudget && throwableCapable.Count > 0; k++)
            {
                int room = throwableCapable[rng.Next(throwableCapable.Count)];
                int markerId = PickMarker(rng, room, MarkerKind.ItemSpawn, out _, itemMask: ItemCategoryMask.Throwable);
                blueprint.Spawns.Add(new BlueprintSpawn { RoomIndex = room, MarkerId = markerId, Kind = SpawnKind.Throwable, WanderRadiusCells = 0f });
            }

            // 기름(필수 — 깊은 구역 집중) · 스크랩(깊이 비례 가치)
            if (!TryPlaceDepthWeighted(rng, blueprint, dangerDepths, ItemCategoryMask.Oil, SpawnKind.Fuel, genParams.OilCount))
            {
                failed = true;
                return;
            }

            TryPlaceDepthWeighted(rng, blueprint, dangerDepths, ItemCategoryMask.Scrap, SpawnKind.Scrap, genParams.ScrapCount);
        }

        /// <summary>
        /// 설비를 분배한다(5절) — 사체 충전소는 CorpseStationSlot 에서 선정(최소 개수는 HerdArea 파훼 쌍이 강제, A등급),
        /// 발전기는 Watcher 어둠 구간 길목(자기 방·인접 방)의 GeneratorSlot(6절 B등급 — 미충족은 경고 로그만, X6).
        /// </summary>
        /// <param name="rng">단일 난수 스트림.</param>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="dangerDepths">방별 위험 깊이.</param>
        /// <param name="templates">템플릿 집합.</param>
        private void DistributeFacilities(DeterministicRng rng, MapBlueprint blueprint, int[] dangerDepths, IReadOnlyList<RoomTemplateDef> templates)
        {
            Log.D("[SpawnDistributor] DistributeFacilities");

            // 사체 충전소 — 모든 HerdArea 앞 도달 구역에 1개 이상(A등급)
            var stationRooms = new List<int>();
            for (int h = 0; h < herdRooms.Count; h++)
            {
                HashSet<int> front = ReachableExcludingRoom(blueprint, herdRooms[h]);

                bool satisfied = false;
                for (int t = 0; t < stationRooms.Count && !satisfied; t++)
                {
                    satisfied = front.Contains(stationRooms[t]);
                }

                if (satisfied)
                {
                    continue;
                }

                var candidates = new List<int>();
                for (int r = 0; r < blueprint.Rooms.Count; r++)
                {
                    if (r != herdRooms[h] && front.Contains(r) && HasMarker(r, MarkerKind.CorpseStationSlot))
                    {
                        candidates.Add(r);
                    }
                }

                if (candidates.Count == 0)
                {
                    failed = true;
                    return;
                }

                int room = candidates[rng.Next(candidates.Count)];
                int markerId = PickMarker(rng, room, MarkerKind.CorpseStationSlot, out _);
                blueprint.Spawns.Add(new BlueprintSpawn { RoomIndex = room, MarkerId = markerId, Kind = SpawnKind.CorpseStation, WanderRadiusCells = 0f });
                stationRooms.Add(room);
            }

            // 발전기 — Watcher 방 자신 → 인접 방 순으로 GeneratorSlot 탐색(B등급)
            var watcherRooms = new List<int>();
            for (int s = 0; s < blueprint.Spawns.Count; s++)
            {
                if (blueprint.Spawns[s].Kind == SpawnKind.ZombieWatcher && !watcherRooms.Contains(blueprint.Spawns[s].RoomIndex))
                {
                    watcherRooms.Add(blueprint.Spawns[s].RoomIndex);
                }
            }

            var generatorRooms = new List<int>();
            for (int w = 0; w < watcherRooms.Count; w++)
            {
                int target = -1;
                if (HasMarker(watcherRooms[w], MarkerKind.GeneratorSlot))
                {
                    target = watcherRooms[w];
                }
                else
                {
                    List<int> neighbors = adjacency[watcherRooms[w]];
                    for (int n = 0; n < neighbors.Count && target < 0; n++)
                    {
                        if (HasMarker(neighbors[n], MarkerKind.GeneratorSlot))
                        {
                            target = neighbors[n];
                        }
                    }
                }

                if (target < 0)
                {
                    Log.D($"[SpawnDistributor] X6 경고: Watcher 방 {watcherRooms[w]} 길목에 GeneratorSlot 없음 — B등급 미충족 허용");
                    continue;
                }

                if (generatorRooms.Contains(target))
                {
                    continue;
                }

                int markerId = PickMarker(rng, target, MarkerKind.GeneratorSlot, out _);
                blueprint.Spawns.Add(new BlueprintSpawn { RoomIndex = target, MarkerId = markerId, Kind = SpawnKind.Generator, WanderRadiusCells = 0f });
                generatorRooms.Add(target);
            }
        }

        /// <summary>위장 무대 후보 방의 앞 구역(그 방 미통과 도달 집합)에 충전소 후보 방이 있는지 검사한다.</summary>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="herdRoom">위장 무대 후보 방.</param>
        /// <returns>충전소 후보 존재 여부.</returns>
        private bool HasStationCandidateInFront(MapBlueprint blueprint, int herdRoom)
        {
            HashSet<int> front = ReachableExcludingRoom(blueprint, herdRoom);
            for (int r = 0; r < blueprint.Rooms.Count; r++)
            {
                if (r != herdRoom && front.Contains(r) && HasMarker(r, MarkerKind.CorpseStationSlot))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 해당 방의 ZombieListener 스폰 전부를 Walker 로 강등한다(A등급 재배치 — 6절).
        /// 마커가 Walker 를 허용하지 않거나 Walker 가 비활성이면 false(리롤).
        /// </summary>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="room">강등 대상 방.</param>
        /// <returns>강등 성공 여부.</returns>
        private bool TryDowngradeListeners(MapBlueprint blueprint, int room)
        {
            if ((paramsCache.EnabledZombieTypes & ZombieTypeMask.Walker) == 0)
            {
                return false;
            }

            for (int s = 0; s < blueprint.Spawns.Count; s++)
            {
                BlueprintSpawn spawn = blueprint.Spawns[s];
                if (spawn.Kind != SpawnKind.ZombieListener || spawn.RoomIndex != room)
                {
                    continue;
                }

                if ((FindMarker(room, spawn.MarkerId).ZombieMask & ZombieTypeMask.Walker) == 0)
                {
                    return false;
                }

                spawn.Kind = SpawnKind.ZombieWalker;
            }

            return true;
        }

        /// <summary>방별 템플릿·인접 리스트·BFS 트리 부모를 이번 분배용으로 재구축한다.</summary>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="templates">템플릿 집합.</param>
        private void BuildCaches(MapBlueprint blueprint, IReadOnlyList<RoomTemplateDef> templates)
        {
            int roomCount = blueprint.Rooms.Count;
            roomTemplates.Clear();
            herdRooms.Clear();
            for (int r = 0; r < roomCount; r++)
            {
                RoomTemplateDef found = null;
                for (int t = 0; t < templates.Count; t++)
                {
                    if (templates[t].TemplateId == blueprint.Rooms[r].TemplateId)
                    {
                        found = templates[t];
                        break;
                    }
                }

                roomTemplates.Add(found);
            }

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

            treeParent = new int[roomCount];
            for (int r = 0; r < roomCount; r++)
            {
                treeParent[r] = -1;
            }

            var visited = new bool[roomCount];
            var queue = new Queue<int>();
            visited[0] = true;
            queue.Enqueue(0);
            while (queue.Count > 0)
            {
                int room = queue.Dequeue();
                List<int> neighbors = adjacency[room];
                for (int i = 0; i < neighbors.Count; i++)
                {
                    if (!visited[neighbors[i]])
                    {
                        visited[neighbors[i]] = true;
                        treeParent[neighbors[i]] = room;
                        queue.Enqueue(neighbors[i]);
                    }
                }
            }
        }

        /// <summary>ancestor 가 room 의 입구 경로(트리 조상 사슬, 자기 자신 포함) 위에 있는지 검사한다.</summary>
        /// <param name="ancestor">조상 후보 방.</param>
        /// <param name="room">기준 방.</param>
        /// <returns>경로 위 여부.</returns>
        private bool IsOnEntrancePath(int ancestor, int room)
        {
            for (int cur = room; cur >= 0; cur = treeParent[cur])
            {
                if (cur == ancestor)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>입구(방 0)에서 blocked 방을 통과하지 않고 도달 가능한 방 집합(관문 = 방 판정용).</summary>
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

        /// <summary>start 방에서 각 방까지 그래프 거리(BFS, 미도달 = -1).</summary>
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

        /// <summary>지정 카테고리 아이템을 깊이 가중 랜덤으로 count 개 배치한다(후보 없으면 false).</summary>
        /// <param name="rng">단일 난수 스트림.</param>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="dangerDepths">방별 위험 깊이.</param>
        /// <param name="category">아이템 카테고리.</param>
        /// <param name="kind">스폰 종류.</param>
        /// <param name="count">배치 수.</param>
        /// <returns>1개 이상 배치 가능 여부(count 0 이면 true).</returns>
        private bool TryPlaceDepthWeighted(DeterministicRng rng, MapBlueprint blueprint, int[] dangerDepths, ItemCategoryMask category, SpawnKind kind, int count)
        {
            if (count <= 0)
            {
                return true;
            }

            var pool = new List<(int room, int weight)>();
            for (int r = 1; r < blueprint.Rooms.Count; r++)
            {
                if (HasItemMarker(r, category))
                {
                    pool.Add((r, dangerDepths[r] < 1 ? 1 : dangerDepths[r]));
                }
            }

            if (pool.Count == 0)
            {
                return false;
            }

            for (int k = 0; k < count; k++)
            {
                int room = PickWeighted(rng, pool);
                int markerId = PickMarker(rng, room, MarkerKind.ItemSpawn, out _, itemMask: category);
                blueprint.Spawns.Add(new BlueprintSpawn { RoomIndex = room, MarkerId = markerId, Kind = kind, WanderRadiusCells = 0f });
            }

            return true;
        }

        /// <summary>가중 랜덤(가중치 비례)으로 방 하나를 고른다(복원 추출 — 풀 유지).</summary>
        /// <param name="rng">단일 난수 스트림.</param>
        /// <param name="pool">(방, 가중치) 후보.</param>
        /// <returns>선택된 방 인덱스.</returns>
        private static int PickWeighted(DeterministicRng rng, List<(int room, int weight)> pool)
        {
            int total = 0;
            for (int i = 0; i < pool.Count; i++)
            {
                total += pool[i].weight;
            }

            int pick = rng.Next(total);
            int acc = 0;
            for (int i = 0; i < pool.Count; i++)
            {
                acc += pool[i].weight;
                if (pick < acc)
                {
                    return pool[i].room;
                }
            }

            return pool[pool.Count - 1].room;
        }

        /// <summary>방에 지정 종류(및 아이템/좀비 마스크 교차) 마커가 있는지 검사한다.</summary>
        /// <param name="room">방 인덱스.</param>
        /// <param name="kind">마커 종류.</param>
        /// <returns>보유 여부.</returns>
        private bool HasMarker(int room, MarkerKind kind)
        {
            MarkerDef[] markers = roomTemplates[room].Markers;
            for (int m = 0; m < markers.Length; m++)
            {
                if (markers[m].Kind == kind)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>방에 지정 카테고리를 허용하는 ItemSpawn 마커가 있는지 검사한다.</summary>
        /// <param name="room">방 인덱스.</param>
        /// <param name="category">요구 카테고리(하나라도 겹치면 참).</param>
        /// <returns>보유 여부.</returns>
        private bool HasItemMarker(int room, ItemCategoryMask category)
        {
            MarkerDef[] markers = roomTemplates[room].Markers;
            for (int m = 0; m < markers.Length; m++)
            {
                if (markers[m].Kind == MarkerKind.ItemSpawn && (markers[m].ItemMask & category) != 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>방에서 조건(종류 + 마스크 교차)에 맞는 마커를 rng 로 고른다.</summary>
        /// <param name="rng">단일 난수 스트림.</param>
        /// <param name="room">방 인덱스.</param>
        /// <param name="kind">마커 종류.</param>
        /// <param name="wander">선택 마커의 배회 반경(출력).</param>
        /// <param name="itemMask">ItemSpawn 요구 카테고리(None = 무시).</param>
        /// <param name="zombieMask">ZombieSpawn 요구 타입(None = 무시).</param>
        /// <returns>선택 마커 Id(-1 = 후보 없음).</returns>
        private int PickMarker(DeterministicRng rng, int room, MarkerKind kind, out float wander, ItemCategoryMask itemMask = ItemCategoryMask.None, ZombieTypeMask zombieMask = ZombieTypeMask.None)
        {
            MarkerDef[] markers = roomTemplates[room].Markers;
            var candidates = new List<int>();
            for (int m = 0; m < markers.Length; m++)
            {
                if (markers[m].Kind != kind)
                {
                    continue;
                }

                if (itemMask != ItemCategoryMask.None && (markers[m].ItemMask & itemMask) == 0)
                {
                    continue;
                }

                if (zombieMask != ZombieTypeMask.None && (markers[m].ZombieMask & zombieMask) == 0)
                {
                    continue;
                }

                candidates.Add(m);
            }

            if (candidates.Count == 0)
            {
                wander = 0f;
                return -1;
            }

            MarkerDef chosen = markers[candidates[rng.Next(candidates.Count)]];
            wander = chosen.WanderRadiusCells;
            return chosen.Id;
        }

        /// <summary>방 템플릿에서 마커 Id 로 마커를 찾는다(존재는 호출자가 보장).</summary>
        /// <param name="room">방 인덱스.</param>
        /// <param name="markerId">마커 Id.</param>
        /// <returns>마커 정의.</returns>
        private MarkerDef FindMarker(int room, int markerId)
        {
            MarkerDef[] markers = roomTemplates[room].Markers;
            for (int m = 0; m < markers.Length; m++)
            {
                if (markers[m].Id == markerId)
                {
                    return markers[m];
                }
            }

            return null;
        }
    }
}

