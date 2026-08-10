using System.Collections.Generic;
using Border.Core;
using NUnit.Framework;

namespace EmptyHouse.MapGen.Core.Tests
{
    /// <summary>
    /// M3 — LockKeyPlacer 테스트. AC-08 이 이 시스템의 대표 자동화 검증이다.
    /// </summary>
    public sealed class LockKeyPlacerTests
    {
        /// <summary>시드 1,000개 자동 생성에서 열쇠_i ∉ R_i 위반 0건(AC-08 · 4-3절 절대 규칙).</summary>
        [Test]
        public void TryPlace_시드_1000개에서_R_불변식_위반이_없다()
        {
            for (int seed = 1; seed <= 1000; seed++)
            {
                MapBlueprint blueprint = Generate(seed, out _, out _);
                List<int> keyRooms = CollectKeyRooms(blueprint);
                for (int i = 1; i <= keyRooms.Count; i++)
                {
                    HashSet<int> reachable = ReachabilityAnalyzer.ComputeReachableRooms(blueprint, i);
                    Assert.That(reachable.Contains(keyRooms[i - 1]), Is.True,
                        $"시드 {seed}: 열쇠_{i} 가 R_{i} 밖(방 {keyRooms[i - 1]})에 배치됐다(AC-08 위반)");
                }
            }
        }

        /// <summary>입구에서 열쇠를 번호 순서대로 회수하는 시뮬레이션이 모든 자물쇠를 연다(AC-09 — 솔로 100% 클리어).</summary>
        [Test]
        public void TryPlace_번호순_회수_시뮬레이션이_모든_자물쇠를_연다()
        {
            for (int seed = 1; seed <= 100; seed++)
            {
                MapBlueprint blueprint = Generate(seed, out _, out _);
                List<int> keyRooms = CollectKeyRooms(blueprint);

                // 보유 열쇠 0에서 시작 — 도달 가능해진 다음 열쇠를 줍는 과정을 반복
                int haveKeys = 0;
                while (haveKeys < keyRooms.Count)
                {
                    HashSet<int> reachable = ReachabilityAnalyzer.ComputeReachableRooms(blueprint, haveKeys + 1);
                    if (!reachable.Contains(keyRooms[haveKeys]))
                    {
                        break;
                    }

                    haveKeys++;
                }

                Assert.That(haveKeys, Is.EqualTo(keyRooms.Count),
                    $"시드 {seed}: 열쇠_{haveKeys + 1} 을 주울 수 없어 자물쇠 {keyRooms.Count}개 중 {haveKeys}개만 열린다(AC-09)");
            }
        }

        /// <summary>채택된 지름길 전부 귀환 단축 이득 ≥ ShortcutValueMin(AC-10 · 4-2절).</summary>
        [Test]
        public void TryPlace_채택_지름길_가치가_임계_이상이다()
        {
            for (int seed = 1; seed <= 100; seed++)
            {
                MapBlueprint blueprint = Generate(seed, out MapGenParams genParams, out _);

                // 지름길 가치 척도는 방 단위 거리(복도 비용 0) — 배치기와 같은 척도여야 판정이 일치한다(2026-08-09)
                bool[] isCorridor = RoomHopMetric.CorridorFlags(blueprint, BlueprintFixtures.CreateFakeTemplates());
                int[] roomHops = RoomHopMetric.Distances(blueprint, isCorridor, 0);
                for (int e = 0; e < blueprint.Edges.Count; e++)
                {
                    BlueprintEdge edge = blueprint.Edges[e];
                    if (edge.State != EdgeState.DoorLocked)
                    {
                        continue;
                    }

                    // 지름길 자물쇠 = 루프(비브리지) 간선의 자물쇠 — 차단해도 전 방 도달이면 루프
                    if (ReachabilityAnalyzer.ComputeReachableWithEdgeBlocked(blueprint, e).Count != blueprint.Rooms.Count)
                    {
                        continue;
                    }

                    Assert.That(RoomHopMetric.ShortcutValue(blueprint, e, isCorridor, roomHops), Is.GreaterThanOrEqualTo(genParams.ShortcutValueMin),
                        $"시드 {seed}: 간선 {e} 지름길 자물쇠의 귀환 단축 이득이 임계 미만이다(AC-10)");
                }
            }
        }

        /// <summary>폴백(X5) 미발동 시 자물쇠 인접 방에 열쇠가 배치된 사례 0건(AC-11 · 4-3절 우선순위 최하위).</summary>
        [Test]
        public void TryPlace_폴백_없이_자물쇠_인접_방에_열쇠가_없다()
        {
            IReadOnlyList<RoomTemplateDef> templates = BlueprintFixtures.CreateFakeTemplates();
            for (int seed = 1; seed <= 100; seed++)
            {
                MapBlueprint blueprint = Generate(seed, out _, out _);
                int[] depths = DangerGradeCalculator.ComputeDepths(blueprint);
                List<int> keyRooms = CollectKeyRooms(blueprint);
                for (int i = 1; i <= keyRooms.Count; i++)
                {
                    BlueprintEdge lockEdge = FindLockEdge(blueprint, i);
                    int lockDepth = depths[lockEdge.RoomA] > depths[lockEdge.RoomB] ? depths[lockEdge.RoomA] : depths[lockEdge.RoomB];
                    HashSet<int> reachable = ReachabilityAnalyzer.ComputeReachableRooms(blueprint, i);

                    // 1순위(비폴백) 비인접 후보가 존재했는지 재현 — 존재했다면 인접 방 배치는 규칙 위반
                    bool tier1NonAdjacentExists = false;
                    for (int r = 0; r < blueprint.Rooms.Count; r++)
                    {
                        if (reachable.Contains(r) && depths[r] >= lockDepth && r != lockEdge.RoomA && r != lockEdge.RoomB
                            && HasKeyMarker(templates, blueprint.Rooms[r].TemplateId))
                        {
                            tier1NonAdjacentExists = true;
                            break;
                        }
                    }

                    if (tier1NonAdjacentExists)
                    {
                        Assert.That(keyRooms[i - 1] == lockEdge.RoomA || keyRooms[i - 1] == lockEdge.RoomB, Is.False,
                            $"시드 {seed}: 비폴백 상황에서 열쇠_{i} 가 자물쇠 인접 방 {keyRooms[i - 1]} 에 배치됐다(AC-11)");
                    }
                }
            }
        }

        /// <summary>
        /// 파이프라인 계약(X3)을 미러링한 생성 헬퍼 — 레이아웃+열쇠 배치를 한 시도로 보고,
        /// 실패 시 리시드 없이 같은 rng 스트림으로 재시도한다. 상한 초과는 테스트 실패.
        /// </summary>
        /// <param name="seed">확정 시드.</param>
        /// <param name="genParams">사용한 생성 파라미터(출력).</param>
        /// <param name="attempts">소모한 시도 횟수(출력).</param>
        /// <returns>열쇠 배치까지 끝난 블루프린트.</returns>
        private static MapBlueprint Generate(int seed, out MapGenParams genParams, out int attempts)
        {
            genParams = BlueprintFixtures.CreateTestParams(seed);
            IReadOnlyList<RoomTemplateDef> templates = BlueprintFixtures.CreateFakeTemplates();
            var rng = new DeterministicRng();
            rng.Reseed(seed);
            var layoutGenerator = new LayoutGenerator();
            var lockKeyPlacer = new LockKeyPlacer();

            for (attempts = 1; attempts <= genParams.RerollMax + 1; attempts++)
            {
                var blueprint = new MapBlueprint();
                blueprint.Meta.Seed = seed;
                if (!layoutGenerator.TryGenerate(rng, genParams, templates, blueprint))
                {
                    continue;
                }

                int[] depths = DangerGradeCalculator.ComputeDepths(blueprint);
                if (lockKeyPlacer.TryPlace(rng, genParams, blueprint, depths, templates))
                {
                    return blueprint;
                }
            }

            Assert.Fail($"시드 {seed}: RerollMax({genParams.RerollMax}) 안에 레이아웃+열쇠 배치 실패(X3)");
            return null;
        }

        /// <summary>Spawns 에서 열쇠 방을 자물쇠 번호 순서(추가 순서)로 수집한다.</summary>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <returns>열쇠_1..N 의 방 인덱스 목록.</returns>
        private static List<int> CollectKeyRooms(MapBlueprint blueprint)
        {
            var keyRooms = new List<int>();
            for (int s = 0; s < blueprint.Spawns.Count; s++)
            {
                if (blueprint.Spawns[s].Kind == SpawnKind.Key)
                {
                    keyRooms.Add(blueprint.Spawns[s].RoomIndex);
                }
            }

            return keyRooms;
        }

        /// <summary>해당 번호의 자물쇠 간선을 찾는다(없으면 테스트 실패).</summary>
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

            Assert.Fail($"자물쇠_{lockNumber} 간선이 없다");
            return null;
        }


        /// <summary>방 템플릿에 Key 가능 ItemSpawn 마커가 있는지 검사한다.</summary>
        /// <param name="templates">템플릿 집합.</param>
        /// <param name="templateId">방 템플릿 ID.</param>
        /// <returns>보유 여부.</returns>
        private static bool HasKeyMarker(IReadOnlyList<RoomTemplateDef> templates, string templateId)
        {
            for (int i = 0; i < templates.Count; i++)
            {
                if (templates[i].TemplateId != templateId)
                {
                    continue;
                }

                for (int m = 0; m < templates[i].Markers.Length; m++)
                {
                    if (templates[i].Markers[m].Kind == MarkerKind.ItemSpawn
                        && (templates[i].Markers[m].ItemMask & ItemCategoryMask.Key) != 0)
                    {
                        return true;
                    }
                }

                return false;
            }

            return false;
        }
    }
}
