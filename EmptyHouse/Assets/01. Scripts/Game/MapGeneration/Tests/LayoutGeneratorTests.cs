using System.Collections.Generic;
using Border.Core;
using NUnit.Framework;

namespace EmptyHouse.MapGen.Core.Tests
{
    /// <summary>
    /// M2 — LayoutGenerator property 테스트. 시드 100개 반복 생성 후 규칙 전수 검사.
    /// 재료: BlueprintFixtures.CreateFakeTemplates · CreateTestParams.
    /// </summary>
    public sealed class LayoutGeneratorTests
    {
        private const int SeedCount = 100; // property 테스트 시드 수

        /// <summary>모든 방·복도가 격자 정수 좌표에 스냅되고 풋프린트가 겹치지 않는다(AC-04).</summary>
        [Test]
        public void TryGenerate_풋프린트가_겹치지_않고_정수_좌표에_스냅된다()
        {
            ForEachSeed((seed, blueprint, templates, genParams) =>
            {
                var occupied = new HashSet<long>();
                for (int r = 0; r < blueprint.Rooms.Count; r++)
                {
                    BlueprintRoom room = blueprint.Rooms[r];
                    RoomTemplateDef template = FindTemplate(templates, room.TemplateId);
                    bool swapped = room.Rotation == Rotation4.Deg90 || room.Rotation == Rotation4.Deg270;
                    int width = swapped ? template.HeightCells : template.WidthCells;
                    int height = swapped ? template.WidthCells : template.HeightCells;
                    for (int x = 0; x < width; x++)
                    {
                        for (int y = 0; y < height; y++)
                        {
                            long key = ((long)(room.Cell.X + x) << 32) ^ (uint)(room.Cell.Y + y);
                            Assert.That(occupied.Add(key), Is.True,
                                $"시드 {seed}: 방 {r}({room.TemplateId}) 풋프린트가 다른 방과 겹친다");
                        }
                    }
                }

                // 총 방 수 예산은 방 전용 집계 — 복도·입구 앵커는 제외
                int countableRooms = 0;
                for (int r = 0; r < blueprint.Rooms.Count; r++)
                {
                    RoomTemplateDef template = FindTemplate(templates, blueprint.Rooms[r].TemplateId);
                    if (!template.IsCorridor && !template.IsEntranceAnchor)
                    {
                        countableRooms++;
                    }
                }

                Assert.That(countableRooms, Is.InRange(genParams.RoomsTotalMin, genParams.RoomsTotalMax),
                    $"시드 {seed}: 총 방 수(방 전용 집계)가 예산 범위 밖이다");
            });
        }

        /// <summary>빈 소켓 0 — 전부 문/통로/막힌 벽 중 하나로 채워진다(AC-05).</summary>
        [Test]
        public void TryGenerate_빈_소켓이_없다()
        {
            ForEachSeed((seed, blueprint, templates, genParams) =>
            {
                // 간선에 쓰인 (방, 소켓) 수집 — 같은 소켓이 두 간선에 쓰이면 실패
                var used = new HashSet<long>();
                for (int e = 0; e < blueprint.Edges.Count; e++)
                {
                    BlueprintEdge edge = blueprint.Edges[e];
                    Assert.That(used.Add(((long)edge.RoomA << 32) | (uint)edge.SocketA), Is.True,
                        $"시드 {seed}: 방 {edge.RoomA} 소켓 {edge.SocketA} 이 두 간선에 쓰였다");
                    if (edge.RoomB >= 0)
                    {
                        Assert.That(used.Add(((long)edge.RoomB << 32) | (uint)edge.SocketB), Is.True,
                            $"시드 {seed}: 방 {edge.RoomB} 소켓 {edge.SocketB} 이 두 간선에 쓰였다");
                    }
                }

                // 배치된 모든 방의 모든 소켓이 정확히 한 번씩 쓰였는가
                for (int r = 0; r < blueprint.Rooms.Count; r++)
                {
                    RoomTemplateDef template = FindTemplate(templates, blueprint.Rooms[r].TemplateId);
                    for (int s = 0; s < template.Sockets.Length; s++)
                    {
                        Assert.That(used.Contains(((long)r << 32) | (uint)template.Sockets[s].Id), Is.True,
                            $"시드 {seed}: 방 {r}({template.TemplateId}) 소켓 {template.Sockets[s].Id} 이 비어 있다(AC-05)");
                    }
                }
            });
        }

        /// <summary>입구에서 모든 방까지 경로가 존재한다 — 잠긴 문도 간선으로 친다(AC-06).</summary>
        [Test]
        public void TryGenerate_고립_방이_없다()
        {
            ForEachSeed((seed, blueprint, templates, genParams) =>
            {
                int[] depths = DangerGradeCalculator.ComputeDepths(blueprint);
                for (int r = 0; r < depths.Length; r++)
                {
                    Assert.That(depths[r], Is.GreaterThanOrEqualTo(0),
                        $"시드 {seed}: 방 {r} 이 입구에서 도달 불가(고립)다");
                }
            });
        }

        /// <summary>
        /// 사이클 비율 0% 는 순수 트리 — 방↔방 루프 간선이 없다(의무 연결(복도 개구)은 목표와 무관하게 수행된다).
        /// 연결 간선은 트리(방-1개) → 루프 순으로 추가된다(생성 순서 계약).
        /// </summary>
        [Test]
        public void TryGenerate_사이클_비율_0은_방방_루프를_만들지_않는다()
        {
            IReadOnlyList<RoomTemplateDef> templates = BlueprintFixtures.CreateFakeTemplates();
            for (int seed = 1; seed <= 30; seed++)
            {
                MapBlueprint blueprint = GenerateWithReroll(seed, out _, p => p.CycleRoomPercent = 0);
                int treeCount = blueprint.Rooms.Count - 1;
                int connectingSeen = 0;
                for (int e = 0; e < blueprint.Edges.Count; e++)
                {
                    BlueprintEdge edge = blueprint.Edges[e];
                    if (edge.RoomB < 0 || edge.State == EdgeState.BlockedWall)
                    {
                        continue;
                    }

                    connectingSeen++;
                    if (connectingSeen <= treeCount)
                    {
                        continue; // 트리 간선
                    }

                    bool corridorInvolved = FindTemplate(templates, blueprint.Rooms[edge.RoomA].TemplateId).IsCorridor
                        || FindTemplate(templates, blueprint.Rooms[edge.RoomB].TemplateId).IsCorridor;
                    Assert.That(corridorInvolved, Is.True,
                        $"시드 {seed}: 비율 0% 인데 방↔방 루프 간선 {edge.RoomA}↔{edge.RoomB} 이 채택됐다");
                }
            }
        }

        /// <summary>
        /// 사이클 목표 비율에 달성치가 단조 반응한다 — 같은 시드에서 100% 달성치 ≥ 0% 달성치,
        /// 전체적으로는 반드시 상승. Meta 달성치와 CycleMetrics 재계산이 일치해야 한다.
        /// </summary>
        [Test]
        public void TryGenerate_사이클_비율은_목표에_단조_반응한다()
        {
            IReadOnlyList<RoomTemplateDef> templates = BlueprintFixtures.CreateFakeTemplates();
            float sumLow = 0f;
            float sumHigh = 0f;
            for (int seed = 1; seed <= 30; seed++)
            {
                MapBlueprint lowBlueprint = GenerateWithReroll(seed, out _, p => p.CycleRoomPercent = 0);
                MapBlueprint highBlueprint = GenerateWithReroll(seed, out _, p => p.CycleRoomPercent = 100);

                float low = lowBlueprint.Meta.CycleRoomPercentAchieved;
                float high = highBlueprint.Meta.CycleRoomPercentAchieved;
                Assert.That(high, Is.GreaterThanOrEqualTo(low),
                    $"시드 {seed}: 목표 100% 달성치({high:F1})가 0% 달성치({low:F1})보다 낮다");
                Assert.That(CycleMetrics.ComputeCycleRoomPercent(highBlueprint, templates), Is.EqualTo(high).Within(0.01f),
                    $"시드 {seed}: Meta 달성치와 재계산이 불일치");

                sumLow += low;
                sumHigh += high;
            }

            Assert.That(sumHigh, Is.GreaterThan(sumLow), "시드 30개 전체에서 목표 100% 가 0% 보다 사이클을 늘리지 못했다");
        }

        /// <summary>
        /// 복도 개구는 마주보는 미사용 소켓을 남기지 않는다 — 봉인된 소켓끼리(복도 포함) 마주보면
        /// 물리 씬의 뚫린 복도 끝이 그래프에 없는 연결처럼 보이게 된다(의무 연결 규칙 검증).
        /// </summary>
        [Test]
        public void TryGenerate_복도_개구는_마주보는_소켓과_반드시_연결된다()
        {
            ForEachSeed((seed, blueprint, templates, genParams) =>
            {
                // 봉인 간선(RoomB=-1)의 소켓 전역 위치·방향 수집
                var sealedSockets = new List<(int room, CellCoord cell, SocketDirection dir, bool corridor)>();
                for (int e = 0; e < blueprint.Edges.Count; e++)
                {
                    BlueprintEdge edge = blueprint.Edges[e];
                    if (edge.RoomB >= 0)
                    {
                        continue;
                    }

                    RoomTemplateDef template = FindTemplate(templates, blueprint.Rooms[edge.RoomA].TemplateId);
                    SocketDef socket = null;
                    for (int s = 0; s < template.Sockets.Length; s++)
                    {
                        if (template.Sockets[s].Id == edge.SocketA)
                        {
                            socket = template.Sockets[s];
                        }
                    }

                    sealedSockets.Add((
                        edge.RoomA,
                        CellMath.WorldCell(blueprint.Rooms[edge.RoomA], template, socket.LocalCell),
                        CellMath.RotateDirection(socket.Direction, blueprint.Rooms[edge.RoomA].Rotation),
                        template.IsCorridor));
                }

                for (int a = 0; a < sealedSockets.Count; a++)
                {
                    for (int b = 0; b < sealedSockets.Count; b++)
                    {
                        if (a == b || (!sealedSockets[a].corridor && !sealedSockets[b].corridor))
                        {
                            continue;
                        }

                        (int dx, int dy) = Step(sealedSockets[a].dir);
                        bool facing = sealedSockets[b].cell.X == sealedSockets[a].cell.X + dx
                            && sealedSockets[b].cell.Y == sealedSockets[a].cell.Y + dy
                            && (int)sealedSockets[b].dir == ((int)sealedSockets[a].dir + 2) % 4;
                        Assert.That(facing, Is.False,
                            $"시드 {seed}: 복도 포함 봉인 소켓 쌍(방 {sealedSockets[a].room} ↔ 방 {sealedSockets[b].room})이 마주보고 있다 — 의무 연결 누락");
                    }
                }
            });
        }

        /// <summary>
        /// 복도는 막다른 끝이 없다 — 모든 복도의 개구 변(월드 방향 그룹)마다 연결 간선이 최소 1개 있다.
        /// 복도가 리프(끝 봉인)로 남으면 실패 — 복도+끝방 원자 배치 규칙 검증.
        /// </summary>
        [Test]
        public void TryGenerate_복도는_막다른_끝이_없다()
        {
            ForEachSeed((seed, blueprint, templates, genParams) =>
            {
                // 연결 간선에 쓰인 (방, 소켓) 집합
                var connected = new HashSet<long>();
                for (int e = 0; e < blueprint.Edges.Count; e++)
                {
                    BlueprintEdge edge = blueprint.Edges[e];
                    if (edge.RoomB < 0 || edge.State == EdgeState.BlockedWall)
                    {
                        continue;
                    }

                    connected.Add(((long)edge.RoomA << 32) | (uint)edge.SocketA);
                    connected.Add(((long)edge.RoomB << 32) | (uint)edge.SocketB);
                }

                for (int r = 0; r < blueprint.Rooms.Count; r++)
                {
                    RoomTemplateDef template = FindTemplate(templates, blueprint.Rooms[r].TemplateId);
                    if (!template.IsCorridor)
                    {
                        continue;
                    }

                    // 개구 변별(회전 적용 월드 방향) 연결 여부 집계
                    var sideHasLink = new Dictionary<SocketDirection, bool>();
                    for (int s = 0; s < template.Sockets.Length; s++)
                    {
                        SocketDirection worldDir = CellMath.RotateDirection(template.Sockets[s].Direction, blueprint.Rooms[r].Rotation);
                        bool linked = connected.Contains(((long)r << 32) | (uint)template.Sockets[s].Id);
                        sideHasLink[worldDir] = (sideHasLink.TryGetValue(worldDir, out bool prev) && prev) || linked;
                    }

                    foreach (KeyValuePair<SocketDirection, bool> side in sideHasLink)
                    {
                        Assert.That(side.Value, Is.True,
                            $"시드 {seed}: 복도 방 {r}({template.TemplateId}) 의 {side.Key} 개구 변이 전부 봉인됐다 — 막다른 복도");
                    }
                }
            });
        }

        /// <summary>방향의 단위 셀 오프셋(+X=East, +Y=North).</summary>
        /// <param name="dir">소켓 방향.</param>
        /// <returns>(dx, dy).</returns>
        private static (int, int) Step(SocketDirection dir)
        {
            switch (dir)
            {
                case SocketDirection.North: return (0, 1);
                case SocketDirection.East: return (1, 0);
                case SocketDirection.South: return (0, -1);
                default: return (-1, 0);
            }
        }

        /// <summary>같은 시드 2회 생성 결과가 BlueprintDump 기준 완전 동일하다(AC-01).</summary>
        [Test]
        public void TryGenerate_같은_시드는_같은_레이아웃을_만든다()
        {
            for (int seed = 1; seed <= SeedCount; seed++)
            {
                MapBlueprint first = GenerateWithReroll(seed, out _);
                MapBlueprint second = GenerateWithReroll(seed, out _);
                Assert.That(BlueprintDump.Dump(second), Is.EqualTo(BlueprintDump.Dump(first)),
                    $"시드 {seed}: 2회 생성 결과가 다르다(AC-01)");
            }
        }

        /// <summary>시드 1~SeedCount 전부에 대해 생성 후 검사를 실행한다.</summary>
        /// <param name="assertion">시드·블루프린트·템플릿·파라미터를 받는 검사.</param>
        private static void ForEachSeed(System.Action<int, MapBlueprint, IReadOnlyList<RoomTemplateDef>, MapGenParams> assertion)
        {
            IReadOnlyList<RoomTemplateDef> templates = BlueprintFixtures.CreateFakeTemplates();
            for (int seed = 1; seed <= SeedCount; seed++)
            {
                MapBlueprint blueprint = GenerateWithReroll(seed, out MapGenParams genParams);
                assertion(seed, blueprint, templates, genParams);
            }
        }

        /// <summary>
        /// 파이프라인 계약(X3)을 미러링한 생성 헬퍼 — 실패 시 리시드 없이 같은 rng 스트림으로
        /// 새 블루프린트에 재시도한다(리롤 상한 = RerollMax). 상한 초과는 테스트 실패.
        /// </summary>
        /// <param name="seed">확정 시드.</param>
        /// <param name="genParams">사용한 생성 파라미터(출력).</param>
        /// <param name="configure">파라미터 오버라이드(선택 — 사이클 비율 등 개별 테스트 조정용).</param>
        /// <returns>레이아웃이 완성된 블루프린트.</returns>
        private static MapBlueprint GenerateWithReroll(int seed, out MapGenParams genParams, System.Action<MapGenParams> configure = null)
        {
            genParams = BlueprintFixtures.CreateTestParams(seed);
            configure?.Invoke(genParams);
            IReadOnlyList<RoomTemplateDef> templates = BlueprintFixtures.CreateFakeTemplates();
            var rng = new DeterministicRng();
            rng.Reseed(seed);
            var generator = new LayoutGenerator();

            for (int attempt = 0; attempt <= genParams.RerollMax; attempt++)
            {
                var blueprint = new MapBlueprint();
                blueprint.Meta.Seed = seed;
                if (generator.TryGenerate(rng, genParams, templates, blueprint))
                {
                    return blueprint;
                }
            }

            Assert.Fail($"시드 {seed}: RerollMax({genParams.RerollMax}) 안에 레이아웃 생성 실패(X3)");
            return null;
        }

        /// <summary>TemplateId 로 템플릿을 찾는다(픽스처 한정 — 없으면 테스트 실패).</summary>
        /// <param name="templates">템플릿 집합.</param>
        /// <param name="templateId">찾을 ID.</param>
        /// <returns>일치 템플릿.</returns>
        private static RoomTemplateDef FindTemplate(IReadOnlyList<RoomTemplateDef> templates, string templateId)
        {
            for (int i = 0; i < templates.Count; i++)
            {
                if (templates[i].TemplateId == templateId)
                {
                    return templates[i];
                }
            }

            Assert.Fail($"픽스처에 없는 템플릿 ID: {templateId}");
            return null;
        }
    }
}
