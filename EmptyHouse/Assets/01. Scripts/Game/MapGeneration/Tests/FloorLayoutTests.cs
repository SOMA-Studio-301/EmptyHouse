using System.Collections.Generic;
using EmptyHouse.MapGen.Runtime;
using NUnit.Framework;

namespace EmptyHouse.MapGen.Core.Tests
{
    /// <summary>
    /// 코어 층 구조(M9-4) — 층 1개 Plan 경로가 v1 레거시 경로와 완전 동일하고,
    /// 층 구간 메타(BlueprintFloor)가 방·간선 전체를 연속으로 덮는지 검증한다.
    /// 다층(계단 샤프트) 자체는 M9-5 의 StairShaftTests 소관.
    /// </summary>
    public sealed class FloorLayoutTests
    {
        /// <summary>층 1개 Plan 경로와 레거시 경로가 같은 시드에서 같은 블루프린트를 낸다(FromLegacy 등가성).</summary>
        [Test]
        public void Generate_Plan_경로는_레거시_경로와_동일하다()
        {
            for (int seed = 1; seed <= 10; seed++)
            {
                MapGenResult legacy = new MapGenerator().Generate(new MapGenParams { Seed = seed }, MapTemplateCatalog.Create());
                MapGenResult plan = new MapGenerator().Generate(MapGenPlan.FromLegacy(new MapGenParams { Seed = seed }, MapTemplateCatalog.Create()));
                Assert.That(legacy.Success && plan.Success, Is.True, $"시드 {seed}: 생성 실패");
                Assert.That(BlueprintDump.Dump(plan.Blueprint), Is.EqualTo(BlueprintDump.Dump(legacy.Blueprint)),
                    $"시드 {seed}: Plan 경로와 레거시 경로의 블루프린트가 다르다 — FromLegacy 합성이 등가가 아니다");
            }
        }

        /// <summary>층 구간 메타가 방·간선 전체를 빈틈·겹침 없이 연속으로 덮는다(층 국소 되감기의 전제 불변식).</summary>
        [Test]
        public void Generate_층_구간이_연속이다()
        {
            for (int seed = 1; seed <= 20; seed++)
            {
                MapGenResult result = new MapGenerator().Generate(new MapGenParams { Seed = seed }, MapTemplateCatalog.Create());
                Assert.That(result.Success, Is.True, $"시드 {seed}: 생성 실패");
                MapBlueprint blueprint = result.Blueprint;
                Assert.That(blueprint.Floors.Count, Is.EqualTo(1), $"시드 {seed}: 층 1개 구성의 Floors 는 1개여야 한다");

                int roomCursor = 0;
                int edgeCursor = 0;
                for (int f = 0; f < blueprint.Floors.Count; f++)
                {
                    BlueprintFloor floor = blueprint.Floors[f];
                    Assert.That(floor.RoomStart, Is.EqualTo(roomCursor), $"시드 {seed}: 층 {floor.FloorIndex} 방 구간이 불연속");
                    Assert.That(floor.EdgeStart, Is.EqualTo(edgeCursor), $"시드 {seed}: 층 {floor.FloorIndex} 간선 구간이 불연속");
                    roomCursor += floor.RoomCount;
                    edgeCursor += floor.EdgeCount;

                    for (int r = floor.RoomStart; r < floor.RoomStart + floor.RoomCount; r++)
                    {
                        Assert.That(blueprint.Rooms[r].FloorIndex, Is.EqualTo(floor.FloorIndex),
                            $"시드 {seed}: 방 {r} 의 층 서수가 구간 메타와 다르다");
                    }
                }

                Assert.That(roomCursor, Is.EqualTo(blueprint.Rooms.Count), $"시드 {seed}: 층 구간 방 합이 전체 방 수와 다르다");
                Assert.That(edgeCursor, Is.EqualTo(blueprint.Edges.Count), $"시드 {seed}: 층 구간 간선 합이 전체 간선 수와 다르다");
            }
        }

        /// <summary>방마다 평탄화 템플릿 인덱스가 올바르게 박힌다 — TemplateId 문자열 역참조와 일치.</summary>
        [Test]
        public void Generate_TemplateIndex_가_평탄화_테이블과_일치한다()
        {
            List<RoomTemplateDef> templates = MapTemplateCatalog.Create();
            MapGenResult result = new MapGenerator().Generate(new MapGenParams { Seed = 7 }, templates);
            Assert.That(result.Success, Is.True);
            for (int r = 0; r < result.Blueprint.Rooms.Count; r++)
            {
                BlueprintRoom room = result.Blueprint.Rooms[r];
                Assert.That(room.TemplateIndex, Is.GreaterThanOrEqualTo(0), $"방 {r}: TemplateIndex 미배정");
                Assert.That(templates[room.TemplateIndex].TemplateId, Is.EqualTo(room.TemplateId),
                    $"방 {r}: TemplateIndex({room.TemplateIndex})가 가리키는 템플릿이 TemplateId 와 다르다");
            }
        }

        /// <summary>FloorSequencer.Order — 시드 층 먼저, 나머지는 |서수| 오름차순·동률이면 위층 먼저. 난수 미소비.</summary>
        [Test]
        public void FloorSequencer_순서가_시드층_우선_절대값_오름차순이다()
        {
            var plan = MapGenPlan.Compose(new MapGenParams(), new[]
            {
                new FloorGenParams { FloorIndex = -1 },
                new FloorGenParams { FloorIndex = 1 },
                new FloorGenParams { FloorIndex = 0 },
            }, new[]
            {
                new FloorTemplateSet { FloorIndex = -1, Templates = new RoomTemplateDef[0] },
                new FloorTemplateSet { FloorIndex = 1, Templates = new RoomTemplateDef[0] },
                new FloorTemplateSet { FloorIndex = 0, Templates = new[] { new RoomTemplateDef { TemplateId = "e", IsEntranceAnchor = true, Sockets = new SocketDef[0], Markers = new MarkerDef[0] } } },
            });

            int[] order = FloorSequencer.Order(plan);
            Assert.That(order.Length, Is.EqualTo(3));
            Assert.That(plan.Floors[order[0]].FloorIndex, Is.EqualTo(0), "시드 층(입구 보유)이 먼저여야 한다");
            Assert.That(plan.Floors[order[1]].FloorIndex, Is.EqualTo(1), "|1| 동률에서 위층(+1)이 먼저여야 한다");
            Assert.That(plan.Floors[order[2]].FloorIndex, Is.EqualTo(-1));
        }

        /// <summary>계단실 없는 다층 Plan 은 X4 사전 검증에서 명시적으로 거부된다(③ — 조용한 단층 폴백 금지).</summary>
        [Test]
        public void ValidateInputs_계단실_없는_다층은_X4_로_거부된다()
        {
            List<RoomTemplateDef> catalog = MapTemplateCatalog.Create();
            var upperTemplates = new List<RoomTemplateDef>();
            for (int t = 0; t < catalog.Count; t++)
            {
                if (!catalog[t].IsEntranceAnchor)
                {
                    // 전 층 TemplateId 유일 제약(X4) — 위층 사본은 ID 에 접미사를 붙인다
                    upperTemplates.Add(new RoomTemplateDef
                    {
                        TemplateId = catalog[t].TemplateId + "_f1",
                        WidthCells = catalog[t].WidthCells,
                        HeightCells = catalog[t].HeightCells,
                        AllowedFloors = catalog[t].AllowedFloors,
                        Tags = catalog[t].Tags,
                        MinCount = 0,
                        MaxCount = catalog[t].MaxCount,
                        IsCorridor = catalog[t].IsCorridor,
                        Sockets = catalog[t].Sockets,
                        Markers = catalog[t].Markers,
                    });
                }
            }

            var plan = MapGenPlan.Compose(new MapGenParams { Seed = 5 }, new[]
            {
                new FloorGenParams { FloorIndex = 0 },
                new FloorGenParams { FloorIndex = 1, RoomsTotalMin = 2, RoomsTotalMax = 4 },
            }, new[]
            {
                new FloorTemplateSet { FloorIndex = 0, Templates = new List<RoomTemplateDef>(catalog).ToArray() },
                new FloorTemplateSet { FloorIndex = 1, Templates = upperTemplates.ToArray() },
            });

            var errors = new List<string>();
            bool valid = new MapGenerator().ValidateInputs(plan, errors);
            Assert.That(valid, Is.False, "계단실 없는 다층 Plan 은 X4 에서 거부돼야 한다");
            Assert.That(errors.Exists(e => e.Contains("계단실")), Is.True, $"계단실 사유가 없다: {string.Join(" / ", errors)}");
        }
    }
}
