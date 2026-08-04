using System.Collections.Generic;
using Border.Core;

namespace EmptyHouse.MapGen.Core.Tests
{
    /// <summary>
    /// 테스트 공용 픽스처 — 손으로 짠 미니 그래프와 가짜 템플릿 세트.
    /// M1(그래프 유틸)의 기대값 검증 재료이자 M2~M5 생성기 구동 재료. 구현은 M1 세션 담당.
    /// </summary>
    public static class BlueprintFixtures
    {
        /// <summary>
        /// 손으로 짠 미니 블루프린트 — 방 6(0 = 입구), 트리 간선 5 + 루프 간선 1,
        /// 자물쇠 2(1번 = 루프 지름길, 2번 = 깊은 가지 관문), 봉인 간선(RoomB = -1) 1개 포함.
        ///
        /// 그래프(간선 인덱스 순):
        ///   0 ─e0(통로)─ 1 ─e1(문)─ 2 ─e2(잠금2)─ 3
        ///                           │             │
        ///                           e3(통로)      e5(잠금1 루프)
        ///                           │             │
        ///                           4 ─e4(문)──── 5 ─e6(봉인 RoomB=-1)
        ///
        /// 수기 기대값: 깊이 = [0,1,2,3,3,4] · R_1 = {0,1,2,4,5} · R_2 = 전체 · e4 차단 시 = {0,1,2,3,4}.
        /// </summary>
        /// <returns>기대값 검증용 블루프린트.</returns>
        public static MapBlueprint CreateMiniBlueprint()
        {
            Log.D("[BlueprintFixtures] CreateMiniBlueprint");
            var blueprint = new MapBlueprint();
            blueprint.Meta.Seed = 1;
            blueprint.Meta.GeneratorVersion = "fixture";

            blueprint.Rooms.Add(new BlueprintRoom { TemplateId = "entrance", Cell = new CellCoord(0, 0), Rotation = Rotation4.Deg0 });
            blueprint.Rooms.Add(new BlueprintRoom { TemplateId = "room_small", Cell = new CellCoord(0, 3), Rotation = Rotation4.Deg0 });
            blueprint.Rooms.Add(new BlueprintRoom { TemplateId = "room_small", Cell = new CellCoord(0, 6), Rotation = Rotation4.Deg0 });
            blueprint.Rooms.Add(new BlueprintRoom { TemplateId = "room_medium", Cell = new CellCoord(4, 6), Rotation = Rotation4.Deg0 });
            blueprint.Rooms.Add(new BlueprintRoom { TemplateId = "room_small", Cell = new CellCoord(4, 3), Rotation = Rotation4.Deg0 });
            blueprint.Rooms.Add(new BlueprintRoom { TemplateId = "room_small", Cell = new CellCoord(8, 3), Rotation = Rotation4.Deg0 });

            // 트리 간선 5
            blueprint.Edges.Add(new BlueprintEdge { RoomA = 0, SocketA = 0, RoomB = 1, SocketB = 0, State = EdgeState.OpenPassage, LockNumber = 0 });
            blueprint.Edges.Add(new BlueprintEdge { RoomA = 1, SocketA = 1, RoomB = 2, SocketB = 0, State = EdgeState.DoorOpen, LockNumber = 0 });
            blueprint.Edges.Add(new BlueprintEdge { RoomA = 2, SocketA = 1, RoomB = 3, SocketB = 0, State = EdgeState.DoorLocked, LockNumber = 2 });
            blueprint.Edges.Add(new BlueprintEdge { RoomA = 2, SocketA = 2, RoomB = 4, SocketB = 0, State = EdgeState.OpenPassage, LockNumber = 0 });
            blueprint.Edges.Add(new BlueprintEdge { RoomA = 4, SocketA = 1, RoomB = 5, SocketB = 0, State = EdgeState.DoorOpen, LockNumber = 0 });
            // 루프 간선 1 — 지름길 자물쇠 1번
            blueprint.Edges.Add(new BlueprintEdge { RoomA = 3, SocketA = 1, RoomB = 4, SocketB = 2, State = EdgeState.DoorLocked, LockNumber = 1 });
            // 봉인 간선 — 짝 없는 소켓의 막힌 벽(간선 아님)
            blueprint.Edges.Add(new BlueprintEdge { RoomA = 5, SocketA = 1, RoomB = -1, SocketB = -1, State = EdgeState.BlockedWall, LockNumber = 0 });

            return blueprint;
        }

        /// <summary>
        /// 생성기 구동용 가짜 템플릿 세트 — 입구 앵커 1종 + 방 3종 + 복도 1종.
        /// 크기 규칙(G1 ② 제안): 모든 변 = 3의 배수, 문 소켓 = 벽의 3셀 블록 중심(1, 4, 7…).
        /// 이 규칙은 90도 회전에도 격자 위상이 유지돼(중심 c ↔ L−1−c 가 다시 중심) 인접 소켓이 마주보고,
        /// 루프 간선 후보(3절 3)가 안정적으로 생긴다. 짝수 격자는 회전 시 홀짝이 뒤집혀 불가.
        /// 셀 = 홀 바닥 타일 1칸 = 실측 4m. 폭 1 복도는 소켓이 단일 열이라 격자 안전.
        /// 마커는 ZombieSpawn·ItemSpawn·GeneratorSlot·CorpseStationSlot·HerdArea 전 종류를 최소 1개씩 포함.
        /// </summary>
        /// <returns>M2~M5 property 테스트용 템플릿 목록.</returns>
        public static IReadOnlyList<RoomTemplateDef> CreateFakeTemplates()
        {
            Log.D("[BlueprintFixtures] CreateFakeTemplates");
            return new List<RoomTemplateDef>
            {
                new RoomTemplateDef
                {
                    TemplateId = "entrance",
                    WidthCells = 3,
                    HeightCells = 3,
                    AllowedFloors = FloorMask.F1,
                    Tags = RoomTagMask.None,
                    MinCount = 1,
                    MaxCount = 1,
                    IsCorridor = false,
                    IsEntranceAnchor = true,
                    Sockets = new[]
                    {
                        new SocketDef { Id = 0, LocalCell = new CellCoord(1, 2), Direction = SocketDirection.North },
                    },
                    Markers = new MarkerDef[0],
                },
                new RoomTemplateDef
                {
                    TemplateId = "room_small",
                    WidthCells = 3,
                    HeightCells = 3,
                    AllowedFloors = FloorMask.F1,
                    Tags = RoomTagMask.None,
                    MinCount = 0,
                    MaxCount = 10,
                    IsCorridor = false,
                    IsEntranceAnchor = false,
                    Sockets = new[]
                    {
                        new SocketDef { Id = 0, LocalCell = new CellCoord(1, 0), Direction = SocketDirection.South },
                        new SocketDef { Id = 1, LocalCell = new CellCoord(1, 2), Direction = SocketDirection.North },
                        new SocketDef { Id = 2, LocalCell = new CellCoord(0, 1), Direction = SocketDirection.West },
                        new SocketDef { Id = 3, LocalCell = new CellCoord(2, 1), Direction = SocketDirection.East },
                    },
                    Markers = new[]
                    {
                        new MarkerDef { Id = 0, Kind = MarkerKind.ZombieSpawn, LocalCell = new CellCoord(1, 1), ZombieMask = ZombieTypeMask.Walker | ZombieTypeMask.Listener, WanderRadiusCells = 2f },
                        new MarkerDef { Id = 1, Kind = MarkerKind.ItemSpawn, LocalCell = new CellCoord(2, 2), ItemMask = ItemCategoryMask.Vaccine | ItemCategoryMask.Key | ItemCategoryMask.Oil | ItemCategoryMask.Scrap | ItemCategoryMask.Throwable },
                        new MarkerDef { Id = 2, Kind = MarkerKind.CorpseStationSlot, LocalCell = new CellCoord(0, 2) },
                    },
                },
                new RoomTemplateDef
                {
                    TemplateId = "room_medium",
                    WidthCells = 6,
                    HeightCells = 6,
                    AllowedFloors = FloorMask.F1,
                    Tags = RoomTagMask.Dark,
                    MinCount = 0,
                    MaxCount = 4,
                    IsCorridor = false,
                    IsEntranceAnchor = false,
                    Sockets = new[]
                    {
                        new SocketDef { Id = 0, LocalCell = new CellCoord(1, 0), Direction = SocketDirection.South },
                        new SocketDef { Id = 1, LocalCell = new CellCoord(4, 0), Direction = SocketDirection.South },
                        new SocketDef { Id = 2, LocalCell = new CellCoord(1, 5), Direction = SocketDirection.North },
                        new SocketDef { Id = 3, LocalCell = new CellCoord(4, 5), Direction = SocketDirection.North },
                        new SocketDef { Id = 4, LocalCell = new CellCoord(0, 1), Direction = SocketDirection.West },
                        new SocketDef { Id = 5, LocalCell = new CellCoord(5, 4), Direction = SocketDirection.East },
                    },
                    Markers = new[]
                    {
                        new MarkerDef { Id = 0, Kind = MarkerKind.ZombieSpawn, LocalCell = new CellCoord(3, 3), ZombieMask = ZombieTypeMask.Watcher, WanderRadiusCells = 2f },
                        new MarkerDef { Id = 1, Kind = MarkerKind.GeneratorSlot, LocalCell = new CellCoord(5, 5) },
                        new MarkerDef { Id = 2, Kind = MarkerKind.ItemSpawn, LocalCell = new CellCoord(0, 5), ItemMask = ItemCategoryMask.Vaccine | ItemCategoryMask.Key | ItemCategoryMask.Oil | ItemCategoryMask.Scrap | ItemCategoryMask.Throwable },
                        new MarkerDef { Id = 3, Kind = MarkerKind.CorpseStationSlot, LocalCell = new CellCoord(5, 0) },
                    },
                },
                new RoomTemplateDef
                {
                    TemplateId = "room_large",
                    WidthCells = 6,
                    HeightCells = 9,
                    AllowedFloors = FloorMask.F1,
                    Tags = RoomTagMask.None,
                    MinCount = 1,
                    MaxCount = 2,
                    IsCorridor = false,
                    IsEntranceAnchor = false,
                    Sockets = new[]
                    {
                        new SocketDef { Id = 0, LocalCell = new CellCoord(1, 0), Direction = SocketDirection.South },
                        new SocketDef { Id = 1, LocalCell = new CellCoord(4, 0), Direction = SocketDirection.South },
                        new SocketDef { Id = 2, LocalCell = new CellCoord(1, 8), Direction = SocketDirection.North },
                        new SocketDef { Id = 3, LocalCell = new CellCoord(4, 8), Direction = SocketDirection.North },
                        new SocketDef { Id = 4, LocalCell = new CellCoord(0, 1), Direction = SocketDirection.West },
                        new SocketDef { Id = 5, LocalCell = new CellCoord(0, 7), Direction = SocketDirection.West },
                        new SocketDef { Id = 6, LocalCell = new CellCoord(5, 4), Direction = SocketDirection.East },
                        new SocketDef { Id = 7, LocalCell = new CellCoord(5, 7), Direction = SocketDirection.East },
                    },
                    Markers = new[]
                    {
                        new MarkerDef { Id = 0, Kind = MarkerKind.HerdArea, LocalCell = new CellCoord(2, 4) },
                        new MarkerDef { Id = 1, Kind = MarkerKind.ZombieSpawn, LocalCell = new CellCoord(4, 6), ZombieMask = ZombieTypeMask.Walker, WanderRadiusCells = 3f },
                        new MarkerDef { Id = 2, Kind = MarkerKind.ItemSpawn, LocalCell = new CellCoord(5, 8), ItemMask = ItemCategoryMask.Vaccine | ItemCategoryMask.Key | ItemCategoryMask.Throwable | ItemCategoryMask.Scrap },
                        new MarkerDef { Id = 3, Kind = MarkerKind.CorpseStationSlot, LocalCell = new CellCoord(0, 8) },
                    },
                },
                new RoomTemplateDef
                {
                    TemplateId = "corridor",
                    WidthCells = 1,
                    HeightCells = 3,
                    AllowedFloors = FloorMask.F1,
                    Tags = RoomTagMask.None,
                    MinCount = 0,
                    MaxCount = 12,
                    IsCorridor = true,
                    IsEntranceAnchor = false,
                    Sockets = new[]
                    {
                        new SocketDef { Id = 0, LocalCell = new CellCoord(0, 0), Direction = SocketDirection.South },
                        new SocketDef { Id = 1, LocalCell = new CellCoord(0, 2), Direction = SocketDirection.North },
                    },
                    Markers = new MarkerDef[0],
                },
            };
        }

        /// <summary>
        /// 테스트 스케일 파라미터 — 총 방 수 축소(10~12) 운용, 나머지는 9절 기본값.
        /// </summary>
        /// <param name="seed">확정 시드(0 금지 — X8).</param>
        /// <returns>테스트용 생성 파라미터.</returns>
        public static MapGenParams CreateTestParams(int seed)
        {
            Log.D($"[BlueprintFixtures] CreateTestParams 시드={seed}");
            return new MapGenParams
            {
                Seed = seed,
                RoomsTotalMin = 10,
                RoomsTotalMax = 12,
            };
        }
    }
}
