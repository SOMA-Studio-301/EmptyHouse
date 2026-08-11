using System.Collections.Generic;
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
        /// <param name="templates">템플릿 집합(열쇠 마커·백신/기름 마커 조회용).</param>
        /// <returns>배치 성공 여부 — 실패 시 호출자가 리롤.</returns>
        public bool TryPlace(DeterministicRng rng, MapGenParams genParams, MapBlueprint blueprint, int[] dangerDepths, IReadOnlyList<RoomTemplateDef> templates)
        {
            Log.D("[LockKeyPlacer] TryPlace");
            PlaceLocks(rng, genParams, blueprint, dangerDepths, templates);
            return TryPlaceKeys(rng, genParams, blueprint, dangerDepths, templates);
        }

        /// <summary>
        /// 자물쇠를 전부 먼저 건다(4-1) — 백신·기름 마커 방이 있는 서브트리를 끊는 브리지 간선 +
        /// 가치 규칙(4-2)을 통과한 루프 간선(가중 랜덤, 가중치 ∝ 연결 구역 위험도).
        /// 번호는 선택 순서대로 1부터 부여한다.
        /// </summary>
        /// <param name="rng">단일 난수 스트림.</param>
        /// <param name="genParams">생성 파라미터(자물쇠 개수·지름길 최소 가치).</param>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="dangerDepths">방별 위험 깊이.</param>
        /// <param name="templates">템플릿 집합(백신/기름 마커 조회용).</param>
        private void PlaceLocks(DeterministicRng rng, MapGenParams genParams, MapBlueprint blueprint, int[] dangerDepths, IReadOnlyList<RoomTemplateDef> templates)
        {
            Log.D("[LockKeyPlacer] PlaceLocks");
            int roomCount = blueprint.Rooms.Count;
            bool[] isCorridor = RoomHopMetric.CorridorFlags(blueprint, templates);
            int[] roomHops = RoomHopMetric.Distances(blueprint, isCorridor, 0);

            // 연결 간선 분류: 브리지(차단 시 뒤 구역이 끊김) vs 루프(비브리지 — 지름길 후보)
            var itemDoorCandidates = new List<(int edge, int weight)>();
            var shortcutCandidates = new List<(int edge, int weight)>();
            for (int e = 0; e < blueprint.Edges.Count; e++)
            {
                BlueprintEdge edge = blueprint.Edges[e];
                if (edge.RoomB < 0 || edge.State == EdgeState.BlockedWall)
                {
                    continue;
                }

                // 복도-복도 간선만 자물쇠 부적격 — 양쪽 단부에 벽이 없어 문틀을 물릴 데가 없다.
                // 복도-방은 방 쪽 벽을 절단해 문을 세울 수 있어 후보로 인정한다(2026-08-07 완화 —
                // 방-방 직결만 인정하면 후보의 67%가 이득 0이라 지름길이 시드당 1개 남짓밖에 안 채택됐다)
                if (isCorridor[edge.RoomA] && isCorridor[edge.RoomB])
                {
                    continue;
                }

                // 수직 간선(계단)은 자물쇠 배제 — v2.0 전 수직 상시 개방(Q4). 소켓 없는 간선(-2)이라 문틀도 없다
                if (blueprint.IsVerticalEdge(edge))
                {
                    continue;
                }

                HashSet<int> front = ReachabilityAnalyzer.ComputeReachableWithEdgeBlocked(blueprint, e);
                if (front.Count == roomCount)
                {
                    // 루프 간선 — 지름길 가치 평가(4-2, 방 단위 거리 기준)
                    if (RoomHopMetric.ShortcutValue(blueprint, e, isCorridor, roomHops) >= genParams.ShortcutValueMin)
                    {
                        int weight = dangerDepths[edge.RoomA] > dangerDepths[edge.RoomB] ? dangerDepths[edge.RoomA] : dangerDepths[edge.RoomB];
                        shortcutCandidates.Add((e, weight < 1 ? 1 : weight));
                    }
                }
                else
                {
                    // 브리지 — 뒤 구역에 백신/기름 가능 마커 방이 있어야 중요 물품 문 후보(4-1)
                    bool hasCriticalItemRoom = false;
                    int behindMaxDepth = 0;
                    for (int r = 0; r < roomCount; r++)
                    {
                        if (front.Contains(r))
                        {
                            continue;
                        }

                        if (dangerDepths[r] > behindMaxDepth)
                        {
                            behindMaxDepth = dangerDepths[r];
                        }

                        if (!hasCriticalItemRoom && HasItemMarker(FindTemplate(templates, blueprint.Rooms[r].TemplateId), ItemCategoryMask.Vaccine | ItemCategoryMask.Fuel))
                        {
                            hasCriticalItemRoom = true;
                        }
                    }

                    if (hasCriticalItemRoom)
                    {
                        itemDoorCandidates.Add((e, behindMaxDepth < 1 ? 1 : behindMaxDepth));
                    }
                }
            }

            // 중요 물품 문 → 지름길 순으로 선택, 선택 순서 = 자물쇠 번호(1부터)
            var lockedEdges = new List<int>();
            PickWeighted(rng, itemDoorCandidates, genParams.ItemDoorLockCount, lockedEdges);
            int itemDoorCount = lockedEdges.Count;
            int shortcutTarget = rng.Next(genParams.ShortcutLockCountMin, genParams.ShortcutLockCountMax + 1);
            PickWeighted(rng, shortcutCandidates, shortcutTarget, lockedEdges);

            for (int n = 0; n < lockedEdges.Count; n++)
            {
                BlueprintEdge edge = blueprint.Edges[lockedEdges[n]];
                edge.State = EdgeState.DoorLocked;
                edge.LockNumber = n + 1;
                edge.LockKind = n < itemDoorCount ? LockKind.ItemDoor : LockKind.Shortcut;
            }
        }

        /// <summary>
        /// 열쇠_i 를 반드시 R_i 안에 배치한다(4-3 절대 규칙 — 예외 없음).
        /// 후보는 Key 가능 ItemSpawn 마커 보유 방 한정. 3단 폴백:
        /// 1순위 자물쇠에서 방 단위 거리 KeyDistanceMin~Max 창 안(너무 붙거나 정반대에 놓이는 것을 막는다 —
        /// 2026-08-07 실측: 거리 0~11 로 퍼져 있고 6건은 자물쇠와 같은 방),
        /// 2순위 창 밖 최근접, 3순위 기존 위험도 규칙(발동 시 X5 로그).
        /// 자물쇠 문 인접 방은 후보 우선순위 최하위(배제 아님).
        /// </summary>
        /// <param name="rng">단일 난수 스트림.</param>
        /// <param name="genParams">생성 파라미터(열쇠 거리 창).</param>
        /// <param name="blueprint">자물쇠가 걸린 블루프린트.</param>
        /// <param name="dangerDepths">방별 위험 깊이.</param>
        /// <param name="templates">템플릿 집합(열쇠 마커 조회용).</param>
        /// <returns>전 열쇠 배치 성공 여부.</returns>
        private bool TryPlaceKeys(DeterministicRng rng, MapGenParams genParams, MapBlueprint blueprint, int[] dangerDepths, IReadOnlyList<RoomTemplateDef> templates)
        {
            Log.D("[LockKeyPlacer] TryPlaceKeys");
            bool[] isCorridor = RoomHopMetric.CorridorFlags(blueprint, templates);
            int lockCount = 0;
            for (int e = 0; e < blueprint.Edges.Count; e++)
            {
                if (blueprint.Edges[e].LockNumber > lockCount)
                {
                    lockCount = blueprint.Edges[e].LockNumber;
                }
            }

            var tier1NonAdjacent = new List<int>();
            var tier1Adjacent = new List<int>();
            var fallbackNonAdjacent = new List<int>();
            var fallbackAdjacent = new List<int>();
            var windowed = new List<int>(); // 1순위 — 자물쇠에서 거리 창 안
            var nearest = new List<int>(); // 2순위 — 창 밖 최근접(동률 전부)
            for (int i = 1; i <= lockCount; i++)
            {
                BlueprintEdge lockEdge = FindLockEdge(blueprint, i);
                int lockDepth = dangerDepths[lockEdge.RoomA] > dangerDepths[lockEdge.RoomB] ? dangerDepths[lockEdge.RoomA] : dangerDepths[lockEdge.RoomB];
                HashSet<int> reachable = ReachabilityAnalyzer.ComputeReachableRooms(blueprint, i);

                // 자물쇠 기준점 = 문 양쪽 중 입구에 가까운 쪽(플레이어가 문 앞에 서는 자리)
                int[] fromEntrance = RoomHopMetric.Distances(blueprint, isCorridor, 0);
                int lockSide = fromEntrance[lockEdge.RoomA] <= fromEntrance[lockEdge.RoomB] ? lockEdge.RoomA : lockEdge.RoomB;
                int[] fromLock = RoomHopMetric.Distances(blueprint, isCorridor, lockSide);

                // 후보 분류 — 방 인덱스 오름차순(결정론)
                tier1NonAdjacent.Clear();
                tier1Adjacent.Clear();
                fallbackNonAdjacent.Clear();
                fallbackAdjacent.Clear();
                windowed.Clear();
                nearest.Clear();
                int nearestMiss = int.MaxValue;
                int maxDepth = -1;
                for (int r = 0; r < blueprint.Rooms.Count; r++)
                {
                    if (!reachable.Contains(r) || !HasItemMarker(FindTemplate(templates, blueprint.Rooms[r].TemplateId), ItemCategoryMask.Key))
                    {
                        continue;
                    }

                    if (dangerDepths[r] > maxDepth)
                    {
                        maxDepth = dangerDepths[r];
                    }

                    bool adjacent = r == lockEdge.RoomA || r == lockEdge.RoomB;
                    if (dangerDepths[r] >= lockDepth)
                    {
                        (adjacent ? tier1Adjacent : tier1NonAdjacent).Add(r);
                    }

                    // 거리 창 후보 — 자물쇠에서 방 단위로 KeyDistanceMin~Max 떨어진 방(인접 방은 창 하한에서 자연 배제)
                    int distance = fromLock[r];
                    if (distance >= genParams.KeyDistanceMin && distance <= genParams.KeyDistanceMax)
                    {
                        windowed.Add(r);
                    }
                    else if (distance >= 0)
                    {
                        int miss = distance < genParams.KeyDistanceMin ? genParams.KeyDistanceMin - distance : distance - genParams.KeyDistanceMax;
                        if (miss < nearestMiss)
                        {
                            nearestMiss = miss;
                            nearest.Clear();
                            nearest.Add(r);
                        }
                        else if (miss == nearestMiss)
                        {
                            nearest.Add(r);
                        }
                    }
                }

                List<int> pool;
                if (windowed.Count > 0)
                {
                    pool = windowed;
                }
                else if (nearest.Count > 0)
                {
                    Log.W($"[LockKeyPlacer] 열쇠_{i}: 거리 창({genParams.KeyDistanceMin}~{genParams.KeyDistanceMax}) 밖 최근접 방으로 폴백(초과 {nearestMiss})");
                    pool = nearest;
                }
                else if (tier1NonAdjacent.Count > 0)
                {
                    pool = tier1NonAdjacent;
                }
                else if (tier1Adjacent.Count > 0)
                {
                    pool = tier1Adjacent;
                }
                else
                {
                    // 폴백(X5) — R_i 내 최고 위험 방. 인접 방은 여기서도 최하위
                    if (maxDepth < 0)
                    {
                        return false;
                    }

                    for (int r = 0; r < blueprint.Rooms.Count; r++)
                    {
                        if (!reachable.Contains(r) || dangerDepths[r] != maxDepth
                            || !HasItemMarker(FindTemplate(templates, blueprint.Rooms[r].TemplateId), ItemCategoryMask.Key))
                        {
                            continue;
                        }

                        bool adjacent = r == lockEdge.RoomA || r == lockEdge.RoomB;
                        (adjacent ? fallbackAdjacent : fallbackNonAdjacent).Add(r);
                    }

                    pool = fallbackNonAdjacent.Count > 0 ? fallbackNonAdjacent : fallbackAdjacent;
                    Log.D($"[LockKeyPlacer] X5 폴백: 열쇠_{i} 1순위 후보 없음 → R_{i} 최고 위험(깊이 {maxDepth}) 배치");
                }

                if (pool.Count == 0)
                {
                    return false;
                }

                int keyRoom = pool[rng.Next(pool.Count)];
                int markerId = PickKeyMarker(rng, FindTemplate(templates, blueprint.Rooms[keyRoom].TemplateId));
                blueprint.Spawns.Add(new BlueprintSpawn
                {
                    RoomIndex = keyRoom,
                    MarkerId = markerId,
                    Kind = SpawnKind.Key,
                    WanderRadiusCells = 0f,
                    KeyNumber = i,
                });
            }

            return true;
        }

        /// <summary>가중 랜덤(가중치 비례)으로 후보에서 최대 count 개를 비복원 선택해 결과에 추가한다.</summary>
        /// <param name="rng">단일 난수 스트림.</param>
        /// <param name="candidates">(간선, 가중치) 후보 — 호출 후 선택분이 제거된다.</param>
        /// <param name="count">선택 목표 수(후보 부족 시 있는 만큼).</param>
        /// <param name="result">선택된 간선 인덱스 수집 목록.</param>
        private static void PickWeighted(DeterministicRng rng, List<(int edge, int weight)> candidates, int count, List<int> result)
        {
            for (int k = 0; k < count && candidates.Count > 0; k++)
            {
                int total = 0;
                for (int i = 0; i < candidates.Count; i++)
                {
                    total += candidates[i].weight;
                }

                int pick = rng.Next(total);
                int acc = 0;
                for (int i = 0; i < candidates.Count; i++)
                {
                    acc += candidates[i].weight;
                    if (pick < acc)
                    {
                        result.Add(candidates[i].edge);
                        candidates.RemoveAt(i);
                        break;
                    }
                }
            }
        }

        /// <summary>해당 번호의 자물쇠 간선을 찾는다(부여 직후라 반드시 존재).</summary>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="lockNumber">자물쇠 번호.</param>
        /// <returns>자물쇠 간선.</returns>
        private static BlueprintEdge FindLockEdge(MapBlueprint blueprint, int lockNumber)
        {
            for (int e = 0; e < blueprint.Edges.Count; e++)
            {
                if (blueprint.Edges[e].LockNumber == lockNumber)
                {
                    return blueprint.Edges[e];
                }
            }

            return null;
        }

        /// <summary>템플릿에 지정 카테고리를 허용하는 ItemSpawn 마커가 있는지 검사한다.</summary>
        /// <param name="template">검사할 템플릿.</param>
        /// <param name="categories">요구 카테고리 마스크(하나라도 겹치면 참).</param>
        /// <returns>보유 여부.</returns>
        private static bool HasItemMarker(RoomTemplateDef template, ItemCategoryMask categories)
        {
            for (int m = 0; m < template.Markers.Length; m++)
            {
                if (template.Markers[m].Kind == MarkerKind.ItemSpawn && (template.Markers[m].ItemMask & categories) != 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>방 템플릿의 Key 가능 ItemSpawn 마커 중 하나를 rng 로 고른다(후보 존재는 호출자가 보장).</summary>
        /// <param name="rng">단일 난수 스트림.</param>
        /// <param name="template">열쇠 방 템플릿.</param>
        /// <returns>선택된 마커 Id.</returns>
        private static int PickKeyMarker(DeterministicRng rng, RoomTemplateDef template)
        {
            var markerIds = new List<int>();
            for (int m = 0; m < template.Markers.Length; m++)
            {
                if (template.Markers[m].Kind == MarkerKind.ItemSpawn && (template.Markers[m].ItemMask & ItemCategoryMask.Key) != 0)
                {
                    markerIds.Add(template.Markers[m].Id);
                }
            }

            return markerIds[rng.Next(markerIds.Count)];
        }

        /// <summary>TemplateId 로 템플릿을 찾는다(어댑터가 유일성 보장 — 없으면 데이터 결함).</summary>
        /// <param name="templates">템플릿 집합.</param>
        /// <param name="templateId">찾을 ID.</param>
        /// <returns>일치 템플릿(없으면 null — NRE 로 즉시 표면화).</returns>
        private static RoomTemplateDef FindTemplate(IReadOnlyList<RoomTemplateDef> templates, string templateId)
        {
            for (int i = 0; i < templates.Count; i++)
            {
                if (templates[i].TemplateId == templateId)
                {
                    return templates[i];
                }
            }

            return null;
        }
    }
}
