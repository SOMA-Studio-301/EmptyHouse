using System.Collections.Generic;
using Border.Core;

namespace EmptyHouse.MapGen.Core
{
    /// <summary>
    /// 그레이박스 개발용 템플릿 세트(0절 — 아트 전 레이아웃 검증 재료).
    /// P5(RoomTemplateSO 세트) 전까지 테스트 픽스처와 기획자 툴(10절)이 같은 재료를 공유한다 —
    /// 재료가 갈라지면 로그 시드 재현(AC-22)이 성립하지 않는다.
    /// </summary>
    public static class DevTemplateSet
    {
        /// <summary>
        /// 개발용 기본 템플릿 세트를 생성한다 — 입구 앵커·복도 3종·전 마커 종류를 포함해 X4 사전 검증을 통과하는 구성.
        /// 호출마다 새 인스턴스를 반환한다 — 호출자(툴)의 오버라이드가 다른 소비자를 오염시키지 않는다.
        /// 소켓 정렬 불변식(G1 ②): 각 벽의 소켓 열 집합은 c ↔ L−1−c 자기 대칭이어야
        /// 90도 회전 후에도 인접 소켓이 마주보고 루프 간선 후보(3절 3)가 유지된다 —
        /// 홀수 변 = 중앙 셀(3의 배수 변은 3셀 블록 중심 1, 4, 7…), 짝수 변 = 쌍대칭 소켓 쌍.
        /// 위반 시 X4 는 통과하지만 루프 하한 미달로 리롤만 반복 실패한다. 폭 1~2 복도는 끝단 단일 열이라 안전.
        /// </summary>
        /// <returns>개발용 기본 템플릿 목록.</returns>
        public static List<RoomTemplateDef> Create()
        {
            Log.D("[DevTemplateSet] Create");
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
                    MaxCount = 40, // 방 전용 집계(복도·입구 제외)라 방 3종 MaxCount 합(40+24+2=66)이 상한(60)을 감당해야 한다 — 합 부족은 X4 가 걸러낸다
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
                    MaxCount = 24, // 상동 — 방 전용 집계 보충
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
                    MaxCount = 32, // 상동 — 60방·경유율 50% 기준 복도 여유분(예산 밖)
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
    }
}
