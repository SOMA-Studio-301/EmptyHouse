using System.Collections.Generic;
using EmptyHouse.MapGen.Runtime;
using NUnit.Framework;

namespace EmptyHouse.MapGen.Core.Tests
{
    /// <summary>
    /// 계단 샤프트(M9-5 SSA) — 3층(B1·1F·2F) 블루프린트 생성이 성공하고,
    /// 샤프트 좌표가 전 층 일치하며, 이격 제약과 수직 간선 규약이 지켜지는지 검증한다.
    /// </summary>
    public sealed class StairShaftTests
    {
        /// <summary>3층 블루프린트 생성이 성공한다(M9-5 수용 기준) — 층 3개·전 층 방 보유·샤프트 ≥1.</summary>
        [Test]
        public void Generate_3층_블루프린트가_성공한다()
        {
            int success = 0;
            for (int seed = 1; seed <= 10; seed++)
            {
                MapGenResult result = new MapGenerator().Generate(MultiFloorFixtures.ThreeFloorPlan(seed));
                if (!result.Success)
                {
                    continue;
                }

                success++;
                MapBlueprint blueprint = result.Blueprint;
                Assert.That(blueprint.Floors.Count, Is.EqualTo(3), $"시드 {seed}: 층 3개가 아니다");
                Assert.That(blueprint.Shafts.Count, Is.GreaterThanOrEqualTo(1), $"시드 {seed}: 샤프트 0개");
                for (int f = 0; f < blueprint.Floors.Count; f++)
                {
                    Assert.That(blueprint.Floors[f].RoomCount, Is.GreaterThan(0), $"시드 {seed}: 층 {blueprint.Floors[f].FloorIndex} 방 0개");
                }
            }

            Assert.That(success, Is.GreaterThanOrEqualTo(8), $"3층 생성 성공률 미달 — {success}/10 (리롤 상한 내 성립이 안정적이어야 한다)");
        }

        /// <summary>같은 샤프트는 전 층에서 같은 좌표·회전의 계단실 방을 가진다(SSA — 복사 정합).</summary>
        [Test]
        public void Generate_샤프트_좌표가_전_층_일치한다()
        {
            MapGenResult result = FirstSuccess(out int seed);
            MapBlueprint blueprint = result.Blueprint;
            foreach (StairShaft shaft in blueprint.Shafts)
            {
                var matches = new List<BlueprintRoom>();
                for (int r = 0; r < blueprint.Rooms.Count; r++)
                {
                    BlueprintRoom room = blueprint.Rooms[r];
                    if (room.Cell.X == shaft.Cell.X && room.Cell.Y == shaft.Cell.Y && room.Rotation == shaft.Rotation
                        && FlatTemplate(blueprint, r).IsStairAnchor)
                    {
                        matches.Add(room);
                    }
                }

                Assert.That(matches.Count, Is.EqualTo(blueprint.Floors.Count),
                    $"시드 {seed}: 샤프트 {shaft.ShaftId} 좌표({shaft.Cell.X},{shaft.Cell.Y})의 계단실이 층 수({blueprint.Floors.Count})만큼 없다({matches.Count})");
                var seenFloors = new HashSet<int>();
                foreach (BlueprintRoom room in matches)
                {
                    Assert.That(seenFloors.Add(room.FloorIndex), Is.True, $"시드 {seed}: 같은 층에 같은 좌표 계단실 중복");
                }
            }
        }

        /// <summary>샤프트끼리 최소 이격(체비셰프)을 지킨다.</summary>
        [Test]
        public void Generate_샤프트_이격이_지켜진다()
        {
            MapGenResult result = FirstSuccess(out int seed);
            MapBlueprint blueprint = result.Blueprint;
            int minSep = new MapGenParams().ShaftMinSeparationCells;
            for (int a = 0; a < blueprint.Shafts.Count; a++)
            {
                for (int b = a + 1; b < blueprint.Shafts.Count; b++)
                {
                    int dx = System.Math.Abs(blueprint.Shafts[a].Cell.X - blueprint.Shafts[b].Cell.X);
                    int dy = System.Math.Abs(blueprint.Shafts[a].Cell.Y - blueprint.Shafts[b].Cell.Y);
                    Assert.That(System.Math.Max(dx, dy), Is.GreaterThanOrEqualTo(minSep),
                        $"시드 {seed}: 샤프트 {a}·{b} 이격 위반");
                }
            }
        }

        /// <summary>수직 간선 규약 — 소켓 -2·항상 개방·자물쇠 없음·RoomA 가 아래층. 층별 수직 연결 존재(패스5와 동일 기준).</summary>
        [Test]
        public void Generate_수직_간선_규약이_지켜진다()
        {
            MapGenResult result = FirstSuccess(out int seed);
            MapBlueprint blueprint = result.Blueprint;
            int verticalCount = 0;
            for (int e = 0; e < blueprint.Edges.Count; e++)
            {
                BlueprintEdge edge = blueprint.Edges[e];
                if (!blueprint.IsVerticalEdge(edge))
                {
                    continue;
                }

                verticalCount++;
                Assert.That(edge.SocketA, Is.EqualTo(-2), $"시드 {seed}: 수직 간선 e{e} SocketA != -2");
                Assert.That(edge.SocketB, Is.EqualTo(-2), $"시드 {seed}: 수직 간선 e{e} SocketB != -2");
                Assert.That(edge.State, Is.EqualTo(EdgeState.OpenPassage), $"시드 {seed}: 수직 간선 e{e} 가 개방이 아니다(Q4)");
                Assert.That(edge.LockNumber, Is.EqualTo(0), $"시드 {seed}: 수직 간선 e{e} 에 자물쇠");
                Assert.That(blueprint.Rooms[edge.RoomA].FloorIndex, Is.LessThan(blueprint.Rooms[edge.RoomB].FloorIndex),
                    $"시드 {seed}: 수직 간선 e{e} RoomA 가 아래층이 아니다");
                Assert.That(System.Math.Abs(blueprint.Rooms[edge.RoomA].FloorIndex - blueprint.Rooms[edge.RoomB].FloorIndex), Is.EqualTo(1),
                    $"시드 {seed}: 수직 간선 e{e} 가 인접 층을 건너뛴다");
            }

            // 샤프트 × (층 수 - 1) 개의 수직 간선 — 전 층 관통 규약(v2)
            Assert.That(verticalCount, Is.EqualTo(blueprint.Shafts.Count * (blueprint.Floors.Count - 1)),
                $"시드 {seed}: 수직 간선 수({verticalCount})가 샤프트({blueprint.Shafts.Count}) × 층간({blueprint.Floors.Count - 1})과 다르다");
        }

        /// <summary>
        /// 각 층의 방들은 수평 간선만으로 하나의 컴포넌트다 — 시드 층처럼 비시드 층도 층 안에서 전부 이어져야 한다.
        /// 샤프트마다 섬을 따로 키우던 시절엔 층이 2~3조각으로 갈라져(실측 시드 300개 중 287개) 계단실 사이를
        /// 다른 층으로 우회해야 했다. 수직 간선은 세지 않는다 — 층 내 보행 연결만 본다.
        /// </summary>
        [Test]
        public void Generate_각_층은_수평_간선만으로_하나로_연결된다()
        {
            for (int seed = 1; seed <= 30; seed++)
            {
                MapGenResult result = new MapGenerator().Generate(MultiFloorFixtures.ThreeFloorPlan(seed));
                Assert.That(result.Success, Is.True, $"시드 {seed}: 생성 실패 — {string.Join(" / ", result.FailReasons)}");
                MapBlueprint blueprint = result.Blueprint;

                var parent = new int[blueprint.Rooms.Count];
                for (int i = 0; i < parent.Length; i++)
                {
                    parent[i] = i;
                }

                int Find(int x)
                {
                    while (parent[x] != x)
                    {
                        parent[x] = parent[parent[x]];
                        x = parent[x];
                    }

                    return x;
                }

                for (int e = 0; e < blueprint.Edges.Count; e++)
                {
                    BlueprintEdge edge = blueprint.Edges[e];
                    if (edge.RoomB < 0 || edge.SocketA < 0 || edge.State == EdgeState.BlockedWall)
                    {
                        continue; // 봉인·수직 간선 제외 — 층 내 수평 연결만 본다
                    }

                    parent[Find(edge.RoomA)] = Find(edge.RoomB);
                }

                for (int f = 0; f < blueprint.Floors.Count; f++)
                {
                    BlueprintFloor floor = blueprint.Floors[f];
                    var roots = new HashSet<int>();
                    for (int r = floor.RoomStart; r < floor.RoomStart + floor.RoomCount; r++)
                    {
                        roots.Add(Find(r));
                    }

                    Assert.That(roots.Count, Is.EqualTo(1),
                        $"시드 {seed}: 층 {floor.FloorIndex} 이 수평 간선 기준 {roots.Count}개 컴포넌트로 갈라졌다 — 층 안에서 계단실로 걸어갈 수 없는 구역이 있다");
                }
            }
        }

        /// <summary>시드 1부터 첫 성공 결과를 가져온다(테스트 픽스처 공용).</summary>
        /// <param name="seed">성공한 시드(출력).</param>
        /// <returns>성공 결과.</returns>
        private static MapGenResult FirstSuccess(out int seed)
        {
            for (seed = 1; seed <= 10; seed++)
            {
                MapGenResult result = new MapGenerator().Generate(MultiFloorFixtures.ThreeFloorPlan(seed));
                if (result.Success)
                {
                    return result;
                }
            }

            Assert.Fail("시드 1~10 전부 생성 실패");
            return null;
        }

        /// <summary>방의 평탄화 템플릿을 TemplateIndex 로 역참조한다.</summary>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="room">방 인덱스.</param>
        /// <returns>템플릿 서술자.</returns>
        private static RoomTemplateDef FlatTemplate(MapBlueprint blueprint, int room)
        {
            // ThreeFloorPlan 과 같은 순서로 재구성 — TemplateIndex 는 평탄화 테이블 기준이라 Plan 재조립로 역참조한다
            MapGenPlan plan = MultiFloorFixtures.ThreeFloorPlan(blueprint.Meta.Seed);
            return plan.FlatTemplates[blueprint.Rooms[room].TemplateIndex];
        }
    }
}
