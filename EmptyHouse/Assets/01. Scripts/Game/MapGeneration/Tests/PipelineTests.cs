using System.Collections.Generic;
using NUnit.Framework;

namespace EmptyHouse.MapGen.Core.Tests
{
    /// <summary>
    /// M5 — BlueprintValidator · MapGenerator(리롤 루프) 테스트.
    /// 손상 블루프린트는 픽스처를 복제 후 규칙 하나만 고의로 깨뜨려 만든다.
    /// </summary>
    public sealed class PipelineTests
    {
        private const int SeedProbeMax = 30; // 손상 전제(Listener·지름길 등)를 갖춘 시드 탐색 상한

        /// <summary>패스 1 — 필수 아이템(백신·기름·열쇠) 미도달 블루프린트를 검출한다(7절 1).</summary>
        [Test]
        public void Validate_필수_아이템_미도달을_검출한다()
        {
            MapGenResult result = GenerateSuccess(1, out MapGenParams genParams);
            MapBlueprint blueprint = result.Blueprint;

            // 백신 항원 방의 전 간선을 봉인해 미도달로 만든다
            int vaccineRoom = FindFirstSpawnRoom(blueprint, SpawnKind.VaccineAntigen);
            for (int e = 0; e < blueprint.Edges.Count; e++)
            {
                BlueprintEdge edge = blueprint.Edges[e];
                if (edge.RoomB >= 0 && (edge.RoomA == vaccineRoom || edge.RoomB == vaccineRoom))
                {
                    edge.State = EdgeState.BlockedWall;
                }
            }

            ValidationReport report = new BlueprintValidator().Validate(blueprint, genParams);
            Assert.That(report.EssentialsReachable, Is.False, "백신 방을 봉인했는데 패스 1이 통과했다");
            Assert.That(report.AllPassed, Is.False);
        }

        /// <summary>패스 2 — 열쇠_i ∉ R_i 위반 블루프린트를 검출한다(7절 2).</summary>
        [Test]
        public void Validate_열쇠_불변식_위반을_검출한다()
        {
            for (int seed = 1; seed <= SeedProbeMax; seed++)
            {
                MapGenResult result = GenerateSuccess(seed, out MapGenParams genParams);
                MapBlueprint blueprint = result.Blueprint;
                var keySpawnIndices = new List<int>();
                for (int s = 0; s < blueprint.Spawns.Count; s++)
                {
                    if (blueprint.Spawns[s].Kind == SpawnKind.Key)
                    {
                        keySpawnIndices.Add(s);
                    }
                }

                // R_i 가 전체 방보다 작은 자물쇠(브리지 자물쇠)를 찾아 열쇠를 R_i 밖으로 옮긴다
                for (int i = 1; i <= keySpawnIndices.Count; i++)
                {
                    HashSet<int> reachable = ReachabilityAnalyzer.ComputeReachableRooms(blueprint, i);
                    if (reachable.Count == blueprint.Rooms.Count)
                    {
                        continue;
                    }

                    int outsideRoom = -1;
                    for (int r = 0; r < blueprint.Rooms.Count && outsideRoom < 0; r++)
                    {
                        if (!reachable.Contains(r))
                        {
                            outsideRoom = r;
                        }
                    }

                    blueprint.Spawns[keySpawnIndices[i - 1]].RoomIndex = outsideRoom;
                    ValidationReport report = new BlueprintValidator().Validate(blueprint, genParams);
                    Assert.That(report.KeyInvariantHolds, Is.False,
                        $"시드 {seed}: 열쇠_{i} 를 R_{i} 밖(방 {outsideRoom})으로 옮겼는데 패스 2가 통과했다");
                    Assert.That(report.AllPassed, Is.False);
                    return;
                }
            }

            Assert.Fail($"시드 1~{SeedProbeMax} 안에 R_i ⊊ 전체인 자물쇠가 없어 손상 케이스를 만들지 못했다");
        }

        /// <summary>패스 3 — 파훼 쌍 A등급(투척물·사체 충전소) 위반을 검출한다(7절 3·6절).</summary>
        [Test]
        public void Validate_파훼_쌍_A등급_위반을_검출한다()
        {
            for (int seed = 1; seed <= SeedProbeMax; seed++)
            {
                MapGenResult result = GenerateSuccess(seed, out MapGenParams genParams);
                MapBlueprint blueprint = result.Blueprint;
                if (!HasSpawn(blueprint, SpawnKind.ZombieListener))
                {
                    continue;
                }

                RemoveSpawns(blueprint, SpawnKind.Throwable);
                ValidationReport report = new BlueprintValidator().Validate(blueprint, genParams);
                Assert.That(report.HardlockPairsHold, Is.False,
                    $"시드 {seed}: 투척물을 전부 제거했는데 A등급 파훼 쌍 검사가 통과했다");
                Assert.That(report.AllPassed, Is.False);
                return;
            }

            Assert.Fail($"시드 1~{SeedProbeMax} 안에 Listener 배치 시드가 없어 손상 케이스를 만들지 못했다");
        }

        /// <summary>패스 4 — 가치 미달 지름길을 검출한다(7절 4).</summary>
        [Test]
        public void Validate_지름길_가치_미달을_검출한다()
        {
            for (int seed = 1; seed <= SeedProbeMax; seed++)
            {
                MapGenResult result = GenerateSuccess(seed, out MapGenParams genParams);
                MapBlueprint blueprint = result.Blueprint;
                if (!HasShortcutLock(blueprint))
                {
                    continue;
                }

                // 임계만 올려 채택된 지름길 전부를 가치 미달로 만든다(블루프린트 자체는 무손상)
                genParams.ShortcutValueMin = int.MaxValue;
                ValidationReport report = new BlueprintValidator().Validate(blueprint, genParams);
                Assert.That(report.ShortcutValuesHold, Is.False,
                    $"시드 {seed}: 임계를 무한대로 올렸는데 패스 4가 통과했다");
                Assert.That(report.AllPassed, Is.False);
                return;
            }

            Assert.Fail($"시드 1~{SeedProbeMax} 안에 지름길 자물쇠 시드가 없어 손상 케이스를 만들지 못했다");
        }

        /// <summary>B등급 파훼 쌍(발전기) 미충족은 실패가 아니라 경고만 남긴다(AC-16 · X6).</summary>
        [Test]
        public void Validate_B등급_미충족은_경고만_남긴다()
        {
            for (int seed = 1; seed <= SeedProbeMax; seed++)
            {
                MapGenResult result = GenerateSuccess(seed, out MapGenParams genParams);
                MapBlueprint blueprint = result.Blueprint;
                if (!HasSpawn(blueprint, SpawnKind.ZombieWatcher) || !HasSpawn(blueprint, SpawnKind.Generator))
                {
                    continue;
                }

                RemoveSpawns(blueprint, SpawnKind.Generator);
                ValidationReport report = new BlueprintValidator().Validate(blueprint, genParams);
                Assert.That(report.Warnings.Count, Is.GreaterThan(0),
                    $"시드 {seed}: 발전기를 전부 제거했는데 X6 경고가 없다");
                Assert.That(report.HardlockPairsHold, Is.True,
                    $"시드 {seed}: B등급 미충족이 패스 3 실패로 처리됐다(X6 위반)");
                Assert.That(report.AllPassed, Is.True,
                    $"시드 {seed}: B등급 미충족이 전체 실패로 처리됐다(AC-16 위반)");
                return;
            }

            Assert.Fail($"시드 1~{SeedProbeMax} 안에 Watcher+발전기 배치 시드가 없어 손상 케이스를 만들지 못했다");
        }

        /// <summary>RerollMax 초과 시 무한 루프 없이 명시적 실패를 반환한다(AC-18 · X2 — 폴백 맵 없음).</summary>
        [Test]
        public void Generate_리롤_상한_초과_시_명시적으로_실패한다()
        {
            // 백신 마커를 room_large(최대 2개)에만 남긴다 — 백신 3종 분산(AC-12)이 구조적으로 불가능
            MapGenParams genParams = CreateParams(1);
            MapGenResult result = new MapGenerator().Generate(genParams, CreateVaccineStarvedTemplates());
            Assert.That(result.Success, Is.False, "백신 방이 최대 2개뿐인데 생성이 성공했다");
            Assert.That(result.Blueprint, Is.Null, "실패인데 블루프린트가 남아 있다(X2 — 폴백 맵 없음)");
            Assert.That(result.RerollCount, Is.EqualTo(genParams.RerollMax), "소비한 리롤 횟수가 상한과 다르다");
            Assert.That(result.FailReasons.Count, Is.GreaterThan(0), "실패 사유가 비어 있다(AC-18)");
        }

        /// <summary>결과에 채택 시드·리롤 횟수·실패 사유가 보존된다(AC-17 — 버그 재현 키).</summary>
        [Test]
        public void Generate_결과에_시드와_리롤_횟수가_보존된다()
        {
            MapGenResult result = GenerateSuccess(7, out MapGenParams genParams);
            Assert.That(result.Blueprint.Meta.Seed, Is.EqualTo(7), "채택 시드가 보존되지 않았다(AC-17)");
            Assert.That(result.Blueprint.Meta.GeneratorVersion, Is.EqualTo(MapGenerator.GeneratorVersion), "생성기 버전 스냅샷 누락");
            Assert.That(result.Blueprint.Meta.ParamsSnapshot, Is.SameAs(genParams), "파라미터 스냅샷 누락");
            Assert.That(result.RerollCount, Is.InRange(0, genParams.RerollMax), "리롤 횟수가 범위를 벗어났다");
        }

        /// <summary>같은 파라미터·같은 시드로 2회 실행한 전체 파이프라인 결과가 완전 동일하다(AC-01).</summary>
        [Test]
        public void Generate_같은_입력은_같은_블루프린트를_만든다()
        {
            MapGenResult first = GenerateSuccess(5, out _);
            MapGenResult second = GenerateSuccess(5, out _);
            Assert.That(second.RerollCount, Is.EqualTo(first.RerollCount), "리롤 횟수가 다르다");
            AssertBlueprintEquals(first.Blueprint, second.Blueprint);
        }

        /// <summary>복도↔복도 연결 간선에는 문이 없다 — 복도 단부에 문틀이 없어 항상 개방 통로여야 한다.</summary>
        [Test]
        public void Generate_복도_사이_간선에는_문이_없다()
        {
            var corridorIds = new HashSet<string>();
            foreach (RoomTemplateDef template in DevTemplateSet.Create())
            {
                if (template.IsCorridor)
                {
                    corridorIds.Add(template.TemplateId);
                }
            }

            int corridorPairEdges = 0;
            for (int seed = 1; seed <= 20; seed++)
            {
                MapGenResult result = GenerateSuccess(seed, out _);
                MapBlueprint blueprint = result.Blueprint;
                for (int e = 0; e < blueprint.Edges.Count; e++)
                {
                    BlueprintEdge edge = blueprint.Edges[e];
                    if (edge.RoomB < 0
                        || !corridorIds.Contains(blueprint.Rooms[edge.RoomA].TemplateId)
                        || !corridorIds.Contains(blueprint.Rooms[edge.RoomB].TemplateId))
                    {
                        continue;
                    }

                    corridorPairEdges++;
                    Assert.That(edge.State, Is.EqualTo(EdgeState.OpenPassage),
                        $"시드 {seed}: 복도 {edge.RoomA}↔복도 {edge.RoomB} 간선이 {edge.State} — 복도 사이엔 문이 올 수 없다");
                }
            }

            Assert.That(corridorPairEdges, Is.GreaterThan(0), "시드 20개에서 복도↔복도 간선이 한 번도 안 나와 검증이 공허하다");
        }

        /// <summary>3타입 전부 활성화한 테스트 파라미터를 만든다.</summary>
        /// <param name="seed">확정 시드.</param>
        /// <returns>테스트용 생성 파라미터.</returns>
        private static MapGenParams CreateParams(int seed)
        {
            MapGenParams genParams = BlueprintFixtures.CreateTestParams(seed);
            genParams.EnabledZombieTypes = ZombieTypeMask.Walker | ZombieTypeMask.Listener | ZombieTypeMask.Watcher;
            return genParams;
        }

        /// <summary>파이프라인을 끝까지 돌려 성공 결과를 얻는다(실패 시 Assert 종료).</summary>
        /// <param name="seed">확정 시드.</param>
        /// <param name="genParams">사용한 생성 파라미터(출력).</param>
        /// <returns>성공한 생성 결과.</returns>
        private static MapGenResult GenerateSuccess(int seed, out MapGenParams genParams)
        {
            genParams = CreateParams(seed);
            MapGenResult result = new MapGenerator().Generate(genParams, BlueprintFixtures.CreateFakeTemplates());
            Assert.That(result.Success, Is.True,
                $"시드 {seed}: 파이프라인 생성 실패 — {string.Join(" / ", result.FailReasons)}");
            return result;
        }

        /// <summary>room_large 외 템플릿에서 Vaccine 마커 허용을 제거한 템플릿 세트를 만든다(리롤 소진 유도).</summary>
        /// <returns>백신 방이 최대 2개로 제한된 템플릿 목록.</returns>
        private static IReadOnlyList<RoomTemplateDef> CreateVaccineStarvedTemplates()
        {
            IReadOnlyList<RoomTemplateDef> templates = BlueprintFixtures.CreateFakeTemplates();
            for (int t = 0; t < templates.Count; t++)
            {
                if (templates[t].TemplateId == "room_large")
                {
                    continue;
                }

                MarkerDef[] markers = templates[t].Markers;
                for (int m = 0; m < markers.Length; m++)
                {
                    markers[m].ItemMask &= ~ItemCategoryMask.Vaccine;
                }
            }

            return templates;
        }

        /// <summary>지정 종류의 첫 스폰이 있는 방 인덱스를 찾는다(없으면 Assert 종료).</summary>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="kind">스폰 종류.</param>
        /// <returns>방 인덱스.</returns>
        private static int FindFirstSpawnRoom(MapBlueprint blueprint, SpawnKind kind)
        {
            for (int s = 0; s < blueprint.Spawns.Count; s++)
            {
                if (blueprint.Spawns[s].Kind == kind)
                {
                    return blueprint.Spawns[s].RoomIndex;
                }
            }

            Assert.Fail($"블루프린트에 {kind} 스폰이 없다");
            return -1;
        }

        /// <summary>지정 종류 스폰 존재 여부를 검사한다.</summary>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="kind">스폰 종류.</param>
        /// <returns>존재 여부.</returns>
        private static bool HasSpawn(MapBlueprint blueprint, SpawnKind kind)
        {
            for (int s = 0; s < blueprint.Spawns.Count; s++)
            {
                if (blueprint.Spawns[s].Kind == kind)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>지정 종류 스폰을 전부 제거한다.</summary>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="kind">제거할 스폰 종류.</param>
        private static void RemoveSpawns(MapBlueprint blueprint, SpawnKind kind)
        {
            for (int s = blueprint.Spawns.Count - 1; s >= 0; s--)
            {
                if (blueprint.Spawns[s].Kind == kind)
                {
                    blueprint.Spawns.RemoveAt(s);
                }
            }
        }

        /// <summary>잠긴 루프 간선(차단해도 전 방 도달 = 지름길 자물쇠)이 있는지 검사한다.</summary>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <returns>존재 여부.</returns>
        private static bool HasShortcutLock(MapBlueprint blueprint)
        {
            for (int e = 0; e < blueprint.Edges.Count; e++)
            {
                BlueprintEdge edge = blueprint.Edges[e];
                if (edge.RoomB < 0 || edge.State != EdgeState.DoorLocked)
                {
                    continue;
                }

                if (ReachabilityAnalyzer.ComputeReachableWithEdgeBlocked(blueprint, e).Count == blueprint.Rooms.Count)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>두 블루프린트의 방·간선·스폰 전 필드를 비교한다(AC-01).</summary>
        /// <param name="expected">기준 블루프린트.</param>
        /// <param name="actual">비교 블루프린트.</param>
        private static void AssertBlueprintEquals(MapBlueprint expected, MapBlueprint actual)
        {
            Assert.That(actual.Rooms.Count, Is.EqualTo(expected.Rooms.Count), "방 수가 다르다");
            for (int r = 0; r < expected.Rooms.Count; r++)
            {
                Assert.That(actual.Rooms[r].TemplateId, Is.EqualTo(expected.Rooms[r].TemplateId), $"방 {r} 템플릿이 다르다");
                Assert.That(actual.Rooms[r].Cell.X, Is.EqualTo(expected.Rooms[r].Cell.X), $"방 {r} X 좌표가 다르다");
                Assert.That(actual.Rooms[r].Cell.Y, Is.EqualTo(expected.Rooms[r].Cell.Y), $"방 {r} Y 좌표가 다르다");
                Assert.That(actual.Rooms[r].Rotation, Is.EqualTo(expected.Rooms[r].Rotation), $"방 {r} 회전이 다르다");
            }

            Assert.That(actual.Edges.Count, Is.EqualTo(expected.Edges.Count), "간선 수가 다르다");
            for (int e = 0; e < expected.Edges.Count; e++)
            {
                Assert.That(actual.Edges[e].RoomA, Is.EqualTo(expected.Edges[e].RoomA), $"간선 {e} RoomA 가 다르다");
                Assert.That(actual.Edges[e].SocketA, Is.EqualTo(expected.Edges[e].SocketA), $"간선 {e} SocketA 가 다르다");
                Assert.That(actual.Edges[e].RoomB, Is.EqualTo(expected.Edges[e].RoomB), $"간선 {e} RoomB 가 다르다");
                Assert.That(actual.Edges[e].SocketB, Is.EqualTo(expected.Edges[e].SocketB), $"간선 {e} SocketB 가 다르다");
                Assert.That(actual.Edges[e].State, Is.EqualTo(expected.Edges[e].State), $"간선 {e} 상태가 다르다");
                Assert.That(actual.Edges[e].LockNumber, Is.EqualTo(expected.Edges[e].LockNumber), $"간선 {e} 자물쇠 번호가 다르다");
                Assert.That(actual.Edges[e].LockKind, Is.EqualTo(expected.Edges[e].LockKind), $"간선 {e} 자물쇠 용도가 다르다");
            }

            Assert.That(actual.Spawns.Count, Is.EqualTo(expected.Spawns.Count), "스폰 수가 다르다");
            for (int s = 0; s < expected.Spawns.Count; s++)
            {
                Assert.That(actual.Spawns[s].RoomIndex, Is.EqualTo(expected.Spawns[s].RoomIndex), $"스폰 {s} 방이 다르다");
                Assert.That(actual.Spawns[s].MarkerId, Is.EqualTo(expected.Spawns[s].MarkerId), $"스폰 {s} 마커가 다르다");
                Assert.That(actual.Spawns[s].Kind, Is.EqualTo(expected.Spawns[s].Kind), $"스폰 {s} 종류가 다르다");
                Assert.That(actual.Spawns[s].WanderRadiusCells, Is.EqualTo(expected.Spawns[s].WanderRadiusCells), $"스폰 {s} 배회 반경이 다르다");
                Assert.That(actual.Spawns[s].KeyNumber, Is.EqualTo(expected.Spawns[s].KeyNumber), $"스폰 {s} 열쇠 번호가 다르다");
            }
        }
    }
}
