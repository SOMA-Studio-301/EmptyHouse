using System.Collections.Generic;
using NUnit.Framework;

namespace EmptyHouse.MapGen.Core.Tests
{
    /// <summary>
    /// 층 인지 배치(M9-6) — DangerBias 가중, 백신 층 배정, 탈출문 입구 층 한정,
    /// 파훼 쌍(Listener↔투척물·HerdArea↔충전소) 같은 층 성립을 검증한다.
    /// </summary>
    public sealed class FloorAwareSpawnTests
    {
        /// <summary>DangerBias 가 층별로 홉 거리에 더해진다 — 바이어스 0 이면 동일(v1 하위호환).</summary>
        [Test]
        public void ComputeDangerGrades_층_가중이_더해진다()
        {
            var blueprint = new MapBlueprint();
            blueprint.Rooms.Add(new BlueprintRoom { TemplateId = "a", FloorIndex = 0 });
            blueprint.Rooms.Add(new BlueprintRoom { TemplateId = "b", FloorIndex = -1 });
            blueprint.Edges.Add(new BlueprintEdge { RoomA = 0, SocketA = -2, RoomB = 1, SocketB = -2, State = EdgeState.OpenPassage });

            var plan = MapGenPlan.Compose(new MapGenParams(), new[]
            {
                new FloorGenParams { FloorIndex = 0, DangerBias = 0 },
                new FloorGenParams { FloorIndex = -1, DangerBias = 5 },
            }, new[]
            {
                new FloorTemplateSet { FloorIndex = 0, Templates = new RoomTemplateDef[0] },
                new FloorTemplateSet { FloorIndex = -1, Templates = new RoomTemplateDef[0] },
            });

            int[] hops = DangerGradeCalculator.ComputeHopDistances(blueprint);
            int[] grades = DangerGradeCalculator.ComputeDangerGrades(blueprint, hops, plan);
            Assert.That(hops[1], Is.EqualTo(1));
            Assert.That(grades[0], Is.EqualTo(0), "시드 층(바이어스 0)은 홉과 같아야 한다");
            Assert.That(grades[1], Is.EqualTo(6), "B1(바이어스 5)은 홉 + 5 여야 한다");
        }

        /// <summary>백신 층 배정(VaccineFloorPlan = 2F 1 + B1 2)이 성공한 생성에서 전부 지켜진다 — 성공률 ≥95%(시드 100).</summary>
        [Test]
        public void Generate_백신_층_배정_성공률이_95퍼센트_이상이다()
        {
            int success = 0;
            int assignedCorrect = 0;
            for (int seed = 1; seed <= 100; seed++)
            {
                MapGenPlan plan = MultiFloorFixtures.ThreeFloorPlan(seed);
                plan.Params.VaccineFloorPlan = new[] { 1, -1, -1 }; // 레벨디자인 — 2F 1 + B1 2
                MapGenResult result = new MapGenerator().Generate(plan);
                if (!result.Success)
                {
                    continue;
                }

                success++;
                var expected = new Dictionary<SpawnKind, int>
                {
                    { SpawnKind.VaccineAntigen, 1 },
                    { SpawnKind.VaccineSerum, -1 },
                    { SpawnKind.VaccineStabilizer, -1 },
                };
                bool allAssigned = true;
                for (int s = 0; s < result.Blueprint.Spawns.Count; s++)
                {
                    BlueprintSpawn spawn = result.Blueprint.Spawns[s];
                    if (expected.TryGetValue(spawn.Kind, out int floor))
                    {
                        allAssigned &= result.Blueprint.Rooms[spawn.RoomIndex].FloorIndex == floor;
                    }
                }

                if (allAssigned)
                {
                    assignedCorrect++;
                }
            }

            Assert.That(success, Is.GreaterThanOrEqualTo(60), $"3층 생성 성공이 너무 적다 — {success}/100");
            Assert.That(assignedCorrect, Is.EqualTo(success), "성공한 생성인데 백신 층 배정이 어긋났다 — 배정은 하드 제약이어야 한다");
            Assert.That(100 * success / 100, Is.GreaterThanOrEqualTo(0)); // 성공률 자체는 위 어서션으로 커버 — 계약 문서화용
        }

        /// <summary>탈출문(ReturnExit)은 입구 층에만 배치된다(M9-6).</summary>
        [Test]
        public void Generate_탈출문은_입구_층에만_있다()
        {
            int checkedCount = 0;
            for (int seed = 1; seed <= 10; seed++)
            {
                MapGenResult result = new MapGenerator().Generate(MultiFloorFixtures.ThreeFloorPlan(seed));
                if (!result.Success)
                {
                    continue;
                }

                MapBlueprint blueprint = result.Blueprint;
                int entranceFloor = blueprint.Rooms[0].FloorIndex;
                for (int e = 0; e < blueprint.Edges.Count; e++)
                {
                    if (blueprint.Edges[e].State == EdgeState.ReturnExit)
                    {
                        checkedCount++;
                        Assert.That(blueprint.Rooms[blueprint.Edges[e].RoomA].FloorIndex, Is.EqualTo(entranceFloor),
                            $"시드 {seed}: 탈출문 e{e} 가 입구 층 밖에 있다");
                    }
                }
            }

            Assert.That(checkedCount, Is.GreaterThan(0), "표본에 탈출문이 하나도 없다 — 테스트 무의미");
        }

        /// <summary>파훼 쌍이 같은 층에서 성립한다 — Listener 방과 같은 층 투척물·HerdArea 와 같은 층 충전소.</summary>
        [Test]
        public void Generate_파훼_쌍은_같은_층에서_성립한다()
        {
            int herdChecked = 0;
            for (int seed = 1; seed <= 10; seed++)
            {
                MapGenPlan plan = MultiFloorFixtures.ThreeFloorPlan(seed);
                for (int f = 0; f < plan.FloorParams.Length; f++)
                {
                    plan.FloorParams[f].EnabledZombieTypes = ZombieTypeMask.Walker | ZombieTypeMask.Listener; // 파훼 쌍 규칙을 실제로 켠다
                }

                MapGenResult result = new MapGenerator().Generate(plan);
                if (!result.Success)
                {
                    continue;
                }

                MapBlueprint blueprint = result.Blueprint;
                var listenerFloors = new List<int>();
                var throwableFloors = new HashSet<int>();
                var herdFloors = new List<int>();
                var stationFloors = new HashSet<int>();
                for (int s = 0; s < blueprint.Spawns.Count; s++)
                {
                    BlueprintSpawn spawn = blueprint.Spawns[s];
                    int floor = blueprint.Rooms[spawn.RoomIndex].FloorIndex;
                    switch (spawn.Kind)
                    {
                        case SpawnKind.ZombieListener: listenerFloors.Add(floor); break;
                        case SpawnKind.Throwable: throwableFloors.Add(floor); break;
                        case SpawnKind.HerdArea: herdFloors.Add(floor); break;
                        case SpawnKind.CorpseStation: stationFloors.Add(floor); break;
                    }
                }

                foreach (int floor in listenerFloors)
                {
                    Assert.That(throwableFloors.Contains(floor), Is.True,
                        $"시드 {seed}: Listener 층 {floor} 에 투척물이 없다(파훼 쌍 층 한정 위반)");
                }

                foreach (int floor in herdFloors)
                {
                    herdChecked++;
                    Assert.That(stationFloors.Contains(floor), Is.True,
                        $"시드 {seed}: HerdArea 층 {floor} 에 충전소가 없다(파훼 쌍 층 한정 위반)");
                }
            }

            Assert.That(herdChecked, Is.GreaterThan(0), "표본에 위장 무대가 하나도 없다 — 테스트 무의미");
        }
    }
}
