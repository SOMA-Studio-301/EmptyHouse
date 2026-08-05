using System.Collections.Generic;
using Border.Core;
using NUnit.Framework;

namespace EmptyHouse.MapGen.Core.Tests
{
    /// <summary>
    /// M4 — SpawnDistributor 테스트. 시드 반복 생성 후 배치 규칙 전수 검사.
    /// 타입 게이트 지침: 테스트는 3타입 전부 활성화해 타입 규칙을 검증한다(Walker 하드코딩 금지).
    /// </summary>
    public sealed class SpawnDistributorTests
    {
        private const int SeedCount = 100; // property 테스트 시드 수

        /// <summary>백신 3종이 서로 다른 가지(서브트리)에 있다(AC-12 · 5절).</summary>
        [Test]
        public void TryDistribute_백신_3종이_서로_다른_가지에_있다()
        {
            for (int seed = 1; seed <= SeedCount; seed++)
            {
                MapBlueprint blueprint = Generate(seed, out _, out _);
                var vaccineRooms = new List<int>();
                for (int s = 0; s < blueprint.Spawns.Count; s++)
                {
                    SpawnKind kind = blueprint.Spawns[s].Kind;
                    if (kind == SpawnKind.VaccineAntigen || kind == SpawnKind.VaccineSerum || kind == SpawnKind.VaccineStabilizer)
                    {
                        vaccineRooms.Add(blueprint.Spawns[s].RoomIndex);
                    }
                }

                Assert.That(vaccineRooms.Count, Is.EqualTo(3), $"시드 {seed}: 백신이 3종이 아니다");

                int[] parent = BuildTreeParents(blueprint);
                for (int a = 0; a < vaccineRooms.Count; a++)
                {
                    for (int b = 0; b < vaccineRooms.Count; b++)
                    {
                        if (a == b)
                        {
                            continue;
                        }

                        Assert.That(IsOnEntrancePath(parent, vaccineRooms[a], vaccineRooms[b]), Is.False,
                            $"시드 {seed}: 백신 방 {vaccineRooms[a]} 이 백신 방 {vaccineRooms[b]} 의 입구 경로 위에 있다(같은 가지, AC-12)");
                    }
                }
            }
        }

        /// <summary>모든 Listener 관문의 ListenerCounterDist 그래프 거리 안에 투척물이 있다(AC-13 · 5절·6절 A등급).</summary>
        [Test]
        public void TryDistribute_Listener_관문_보장_거리_안에_투척물이_있다()
        {
            for (int seed = 1; seed <= SeedCount; seed++)
            {
                MapBlueprint blueprint = Generate(seed, out MapGenParams genParams, out _);
                List<int> throwableRooms = CollectRooms(blueprint, SpawnKind.Throwable);
                List<int> listenerRooms = CollectRooms(blueprint, SpawnKind.ZombieListener);

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

                    Assert.That(satisfied, Is.True,
                        $"시드 {seed}: Listener 방 {listenerRooms[l]} 관문 앞 보장 거리({genParams.ListenerCounterDist}) 안에 투척물이 없다(AC-13)");
                }
            }
        }

        /// <summary>모든 HerdArea 앞 도달 가능 구역에 사체 충전소가 있다(AC-14 · 6절 A등급).</summary>
        [Test]
        public void TryDistribute_HerdArea_앞_도달_구역에_사체_충전소가_있다()
        {
            for (int seed = 1; seed <= SeedCount; seed++)
            {
                MapBlueprint blueprint = Generate(seed, out _, out _);
                List<int> herdRooms = CollectRooms(blueprint, SpawnKind.HerdArea);
                List<int> stationRooms = CollectRooms(blueprint, SpawnKind.CorpseStation);

                for (int h = 0; h < herdRooms.Count; h++)
                {
                    HashSet<int> front = ReachableExcludingRoom(blueprint, herdRooms[h]);
                    bool satisfied = false;
                    for (int t = 0; t < stationRooms.Count && !satisfied; t++)
                    {
                        satisfied = front.Contains(stationRooms[t]);
                    }

                    Assert.That(satisfied, Is.True,
                        $"시드 {seed}: HerdArea 방 {herdRooms[h]} 앞 도달 구역에 사체 충전소가 없다(AC-14)");
                }
            }
        }

        /// <summary>마커 밖 좌표(마커 참조 없는 스폰)가 0개다(AC-15 · 2절).</summary>
        [Test]
        public void TryDistribute_마커_밖_스폰이_없다()
        {
            for (int seed = 1; seed <= SeedCount; seed++)
            {
                MapBlueprint blueprint = Generate(seed, out _, out IReadOnlyList<RoomTemplateDef> templates);
                for (int s = 0; s < blueprint.Spawns.Count; s++)
                {
                    BlueprintSpawn spawn = blueprint.Spawns[s];
                    MarkerDef marker = FindMarker(templates, blueprint, spawn.RoomIndex, spawn.MarkerId);
                    Assert.That(marker, Is.Not.Null,
                        $"시드 {seed}: 스폰 {s}({spawn.Kind}) 가 방 {spawn.RoomIndex} 에 없는 마커 {spawn.MarkerId} 를 참조한다(AC-15)");
                    Assert.That(IsKindCompatible(spawn.Kind, marker), Is.True,
                        $"시드 {seed}: 스폰 {s}({spawn.Kind}) 가 종류 불일치 마커({marker.Kind}) 에 놓였다(AC-15)");
                }
            }
        }

        /// <summary>
        /// 파이프라인 계약(X3)을 미러링한 생성 헬퍼 — 레이아웃+열쇠+스폰을 한 시도로 보고,
        /// 실패 시 리시드 없이 같은 rng 스트림으로 재시도한다. 3타입 전부 활성화.
        /// </summary>
        /// <param name="seed">확정 시드.</param>
        /// <param name="genParams">사용한 생성 파라미터(출력).</param>
        /// <param name="templates">사용한 템플릿 집합(출력).</param>
        /// <returns>스폰 분배까지 끝난 블루프린트.</returns>
        private static MapBlueprint Generate(int seed, out MapGenParams genParams, out IReadOnlyList<RoomTemplateDef> templates)
        {
            genParams = BlueprintFixtures.CreateTestParams(seed);
            genParams.EnabledZombieTypes = ZombieTypeMask.Walker | ZombieTypeMask.Listener | ZombieTypeMask.Watcher;
            templates = BlueprintFixtures.CreateFakeTemplates();
            var rng = new DeterministicRng();
            rng.Reseed(seed);
            var layoutGenerator = new LayoutGenerator();
            var lockKeyPlacer = new LockKeyPlacer();
            var spawnDistributor = new SpawnDistributor();

            for (int attempt = 1; attempt <= genParams.RerollMax + 1; attempt++)
            {
                var blueprint = new MapBlueprint();
                blueprint.Meta.Seed = seed;
                if (!layoutGenerator.TryGenerate(rng, genParams, templates, blueprint))
                {
                    continue;
                }

                int[] depths = DangerGradeCalculator.ComputeDepths(blueprint);
                if (!lockKeyPlacer.TryPlace(rng, genParams, blueprint, depths, templates))
                {
                    continue;
                }

                if (spawnDistributor.TryDistribute(rng, genParams, blueprint, depths, templates))
                {
                    return blueprint;
                }
            }

            Assert.Fail($"시드 {seed}: RerollMax({genParams.RerollMax}) 안에 스폰 분배까지 완료 실패(X3)");
            return null;
        }

        /// <summary>지정 종류 스폰의 방 목록(중복 제거)을 수집한다.</summary>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="kind">스폰 종류.</param>
        /// <returns>방 인덱스 목록.</returns>
        private static List<int> CollectRooms(MapBlueprint blueprint, SpawnKind kind)
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

        /// <summary>연결 간선으로 인접 리스트를 만든다(간선 인덱스 순).</summary>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <returns>방별 이웃 목록.</returns>
        private static List<int>[] BuildAdjacency(MapBlueprint blueprint)
        {
            var adjacency = new List<int>[blueprint.Rooms.Count];
            for (int r = 0; r < adjacency.Length; r++)
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

            return adjacency;
        }

        /// <summary>입구 기준 BFS 트리 부모 배열(가지 판정 — SpawnDistributor 와 동일 규칙 미러).</summary>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <returns>방별 트리 부모(-1 = 입구/미도달).</returns>
        private static int[] BuildTreeParents(MapBlueprint blueprint)
        {
            List<int>[] adjacency = BuildAdjacency(blueprint);
            var parent = new int[blueprint.Rooms.Count];
            var visited = new bool[blueprint.Rooms.Count];
            for (int r = 0; r < parent.Length; r++)
            {
                parent[r] = -1;
            }

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
                        parent[neighbors[i]] = room;
                        queue.Enqueue(neighbors[i]);
                    }
                }
            }

            return parent;
        }

        /// <summary>ancestor 가 room 의 입구 경로(트리 조상 사슬) 위에 있는지 검사한다.</summary>
        /// <param name="parent">트리 부모 배열.</param>
        /// <param name="ancestor">조상 후보 방.</param>
        /// <param name="room">기준 방.</param>
        /// <returns>경로 위 여부.</returns>
        private static bool IsOnEntrancePath(int[] parent, int ancestor, int room)
        {
            for (int cur = room; cur >= 0; cur = parent[cur])
            {
                if (cur == ancestor)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>입구에서 blocked 방을 통과하지 않는 도달 집합(SpawnDistributor 관문 판정 미러).</summary>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="blocked">통과 금지 방.</param>
        /// <returns>도달 가능 방 집합.</returns>
        private static HashSet<int> ReachableExcludingRoom(MapBlueprint blueprint, int blocked)
        {
            List<int>[] adjacency = BuildAdjacency(blueprint);
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
        /// <returns>방별 거리.</returns>
        private static int[] DistancesFrom(MapBlueprint blueprint, int start)
        {
            List<int>[] adjacency = BuildAdjacency(blueprint);
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

        /// <summary>방 템플릿에서 마커 Id 로 마커를 찾는다(없으면 null).</summary>
        /// <param name="templates">템플릿 집합.</param>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="room">방 인덱스.</param>
        /// <param name="markerId">마커 Id.</param>
        /// <returns>마커 정의(없으면 null).</returns>
        private static MarkerDef FindMarker(IReadOnlyList<RoomTemplateDef> templates, MapBlueprint blueprint, int room, int markerId)
        {
            for (int t = 0; t < templates.Count; t++)
            {
                if (templates[t].TemplateId != blueprint.Rooms[room].TemplateId)
                {
                    continue;
                }

                for (int m = 0; m < templates[t].Markers.Length; m++)
                {
                    if (templates[t].Markers[m].Id == markerId)
                    {
                        return templates[t].Markers[m];
                    }
                }

                return null;
            }

            return null;
        }

        /// <summary>스폰 종류와 마커 종류·마스크의 정합을 검사한다(AC-15).</summary>
        /// <param name="kind">스폰 종류.</param>
        /// <param name="marker">참조 마커.</param>
        /// <returns>정합 여부.</returns>
        private static bool IsKindCompatible(SpawnKind kind, MarkerDef marker)
        {
            switch (kind)
            {
                case SpawnKind.ZombieWalker: return marker.Kind == MarkerKind.ZombieSpawn && (marker.ZombieMask & ZombieTypeMask.Walker) != 0;
                case SpawnKind.ZombieListener: return marker.Kind == MarkerKind.ZombieSpawn && (marker.ZombieMask & ZombieTypeMask.Listener) != 0;
                case SpawnKind.ZombieWatcher: return marker.Kind == MarkerKind.ZombieSpawn && (marker.ZombieMask & ZombieTypeMask.Watcher) != 0;
                case SpawnKind.VaccineAntigen:
                case SpawnKind.VaccineSerum:
                case SpawnKind.VaccineStabilizer: return marker.Kind == MarkerKind.ItemSpawn && (marker.ItemMask & ItemCategoryMask.Vaccine) != 0;
                case SpawnKind.Key: return marker.Kind == MarkerKind.ItemSpawn && (marker.ItemMask & ItemCategoryMask.Key) != 0;
                case SpawnKind.Fuel: return marker.Kind == MarkerKind.ItemSpawn && (marker.ItemMask & ItemCategoryMask.Fuel) != 0;
                case SpawnKind.Scrap: return marker.Kind == MarkerKind.ItemSpawn && (marker.ItemMask & ItemCategoryMask.Scrap) != 0;
                case SpawnKind.Throwable: return marker.Kind == MarkerKind.ItemSpawn && (marker.ItemMask & ItemCategoryMask.Throwable) != 0;
                case SpawnKind.CorpseStation: return marker.Kind == MarkerKind.CorpseStationSlot;
                case SpawnKind.Generator: return marker.Kind == MarkerKind.GeneratorSlot;
                case SpawnKind.HerdArea: return marker.Kind == MarkerKind.HerdArea;
                default: return false;
            }
        }
    }
}
