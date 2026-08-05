using System.Collections.Generic;
using Border.Core;
using EmptyHouse.MapGen.Core;

namespace EmptyHouse.MapGen.Editor
{
    /// <summary>
    /// 실제 방 프리팹(02. Prefab/Map/Rooms — 정제 세트) 실측 기반 템플릿 세트 — 예시 맵 씬 빌더 재료.
    /// 셀 1칸 = 4m(Hall_Floor_4M 실측): 3x3 = 12×12m, 6x6 = 24×24m, 6x9 = 24×36m, 전 프리팹 벽 6m 균일.
    /// 입구도 EmptyRoom-6x6 공용(구 Entrance 5x5·벽 8m 특수 케이스 폐기).
    /// 복도(실측): Hallway = 4×8m(1×2셀), Hallway x2 = 8×8m(2×2셀) — 둘 다 긴 옆면(동·서)은 벽·아치창으로
    /// 막혀 있고 <b>남·북 단부만 개구</b>다. 소켓은 반드시 개구 변에만 둔다(벽 뚫린 곳 = 연결 지점).
    /// 소켓 정렬 불변식: 각 벽의 소켓 열 집합은 c ↔ L−1−c 자기 대칭(홀수 변 = 중앙, 짝수 변 = 쌍대칭 —
    /// 6변 {1,4}·8변 {2,5}) — 기존 "모든 변 3의 배수" 규칙을 이 조건으로 일반화했다. 위반 시 루프 간선 후보가 사라진다.
    /// P5(RoomTemplateSO 세트) 전까지의 에디터 전용 브리지 — 게임 런타임 어댑터가 생기면 SO 로 대체한다.
    /// </summary>
    public static class PrefabRoomTemplates
    {
        public const float CellMeters = 4f; // 셀 실측(m) — Hall_Floor_4M 바닥 타일 기준

        /// <summary>TemplateId → 프리팹 경로 매핑. 빌더가 인스턴스화 시 조회한다.</summary>
        /// <remarks>
        /// 방·복도·문은 전부 정제된 Rooms/ 세트만 사용한다(2026-08-05 확정 — DecoratedRooms 구판 사용 금지).
        /// 입구도 Rooms/EmptyRoom-6x6 공용 — 8m 벽 특수 케이스(구 Entrance 5x5)가 사라졌다.
        /// 봉인 벽·이음 기둥(HorrorPack)은 방이 아니라 마감재라 예외.
        /// </remarks>
        public static readonly Dictionary<string, string> PrefabPaths = new Dictionary<string, string>
        {
            { "entrance_6x6", "Assets/02. Prefab/Map/Rooms/Entrance-EmptyRoom-6x6.prefab" },
            { "room_3x3", "Assets/02. Prefab/Map/Rooms/EmptyRoom-3x3.prefab" },
            { "room_6x6", "Assets/02. Prefab/Map/Rooms/EmptyRoom-6x6.prefab" },
            { "room_6x9", "Assets/02. Prefab/Map/Rooms/EmptyRoom-6x9.prefab" },
            { "hallway", "Assets/02. Prefab/Map/Rooms/Hallway.prefab" },
            { "hallway_x2", "Assets/02. Prefab/Map/Rooms/Hallway x2.prefab" },
        };

        public const string DoorPath = "Assets/02. Prefab/Map/Rooms/Door.prefab"; // 단일 문 프리팹(닫힌 버전, 4m 슬롯 전폭·전고 6m) — 열림/잠김 모두 이것 하나로 배치해 위치 계산 분기를 없앤다
        public const string SealWallPath = "Assets/04. Arts/Environment/HorrorPack/!Prefabs/Architectural/Hall_Props/Hall_Wall_6M_1Side.prefab"; // 복도 개구 봉인 벽(2×6m)
        public const string CornerColumnPath = "Assets/04. Arts/Environment/HorrorPack/!Prefabs/Architectural/Hall_Props/Hall_Clumn_Large_6M.prefab"; // 코너 이음 기둥(0.86×5.93m)

        /// <summary>
        /// 실측 프리팹 크기의 템플릿 세트를 만든다 — 소켓은 c ↔ L−1−c 자기 대칭 위상,
        /// 마커 구성은 DevTemplateSet 과 같은 역할 분담(3x3 = 일반 / 6x6 = 어둠·발전기 / 6x9 = 위장 무대).
        /// </summary>
        /// <returns>씬 빌더용 템플릿 목록.</returns>
        public static List<RoomTemplateDef> Create()
        {
            Log.D("[PrefabRoomTemplates] Create");
            return new List<RoomTemplateDef>
            {
                new RoomTemplateDef
                {
                    // 입구 = Rooms/EmptyRoom-6x6 공용 프리팹(벽 6m) — 소켓은 {1,4} 격자 위상(3의 배수 규칙)
                    TemplateId = "entrance_6x6",
                    WidthCells = 6,
                    HeightCells = 6,
                    AllowedFloors = FloorMask.F1,
                    Tags = RoomTagMask.None,
                    MinCount = 1,
                    MaxCount = 1,
                    IsCorridor = false,
                    IsEntranceAnchor = true,
                    Sockets = new[]
                    {
                        new SocketDef { Id = 0, LocalCell = new CellCoord(1, 5), Direction = SocketDirection.North },
                        new SocketDef { Id = 1, LocalCell = new CellCoord(0, 1), Direction = SocketDirection.West },
                        new SocketDef { Id = 2, LocalCell = new CellCoord(5, 1), Direction = SocketDirection.East },
                    },
                    Markers = new MarkerDef[0],
                },
                new RoomTemplateDef
                {
                    TemplateId = "room_3x3",
                    WidthCells = 3,
                    HeightCells = 3,
                    AllowedFloors = FloorMask.F1,
                    Tags = RoomTagMask.None,
                    MinCount = 0,
                    MaxCount = 40, // 방 전용 집계 — 방 3종 MaxCount 합(40+24+2=66)이 예산 상한(60)을 여유 있게 감당해야 한다
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
                    // 3의 배수 재제작판 — 정사각이라 90도 회전군에서도 전 변 소켓 집합 {1,4} 로 격자 정렬 유지
                    TemplateId = "room_6x6",
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
                        new SocketDef { Id = 5, LocalCell = new CellCoord(0, 4), Direction = SocketDirection.West },
                        new SocketDef { Id = 6, LocalCell = new CellCoord(5, 1), Direction = SocketDirection.East },
                        new SocketDef { Id = 7, LocalCell = new CellCoord(5, 4), Direction = SocketDirection.East },
                    },
                    Markers = new[]
                    {
                        new MarkerDef { Id = 0, Kind = MarkerKind.ZombieSpawn, LocalCell = new CellCoord(3, 3), ZombieMask = ZombieTypeMask.Walker | ZombieTypeMask.Listener | ZombieTypeMask.Watcher, WanderRadiusCells = 2f },
                        new MarkerDef { Id = 1, Kind = MarkerKind.GeneratorSlot, LocalCell = new CellCoord(5, 5) },
                        new MarkerDef { Id = 2, Kind = MarkerKind.ItemSpawn, LocalCell = new CellCoord(0, 5), ItemMask = ItemCategoryMask.Vaccine | ItemCategoryMask.Key | ItemCategoryMask.Oil | ItemCategoryMask.Scrap | ItemCategoryMask.Throwable },
                        new MarkerDef { Id = 3, Kind = MarkerKind.CorpseStationSlot, LocalCell = new CellCoord(5, 0) },
                    },
                },
                new RoomTemplateDef
                {
                    // 3의 배수 재제작판 — 6변 {1,4} · 9변 {1,7} 전부 c ↔ L−1−c 자기 대칭
                    TemplateId = "room_6x9",
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
                        new SocketDef { Id = 6, LocalCell = new CellCoord(5, 1), Direction = SocketDirection.East },
                        new SocketDef { Id = 7, LocalCell = new CellCoord(5, 7), Direction = SocketDirection.East },
                    },
                    Markers = new[]
                    {
                        new MarkerDef { Id = 0, Kind = MarkerKind.HerdArea, LocalCell = new CellCoord(2, 4) },
                        new MarkerDef { Id = 1, Kind = MarkerKind.ZombieSpawn, LocalCell = new CellCoord(4, 7), ZombieMask = ZombieTypeMask.Walker, WanderRadiusCells = 3f },
                        new MarkerDef { Id = 2, Kind = MarkerKind.ItemSpawn, LocalCell = new CellCoord(5, 8), ItemMask = ItemCategoryMask.Vaccine | ItemCategoryMask.Key | ItemCategoryMask.Throwable | ItemCategoryMask.Scrap },
                        new MarkerDef { Id = 3, Kind = MarkerKind.CorpseStationSlot, LocalCell = new CellCoord(0, 8) },
                    },
                },
                new RoomTemplateDef
                {
                    // 실측: 긴 축 = 로컬 Z(북남), 개구 = 남(0,0)·북(0,1) 단부뿐 — 옆면 소켓 금지
                    TemplateId = "hallway",
                    WidthCells = 1,
                    HeightCells = 2,
                    AllowedFloors = FloorMask.F1,
                    Tags = RoomTagMask.None,
                    MinCount = 0,
                    MaxCount = 30, // 방 60개·경유율 50% 기준 복도 수요 감당(예산 밖 — 개수 제한은 이 값뿐)
                    IsCorridor = true,
                    IsEntranceAnchor = false,
                    Sockets = new[]
                    {
                        new SocketDef { Id = 0, LocalCell = new CellCoord(0, 0), Direction = SocketDirection.South },
                        new SocketDef { Id = 1, LocalCell = new CellCoord(0, 1), Direction = SocketDirection.North },
                    },
                    Markers = new MarkerDef[0],
                },
                new RoomTemplateDef
                {
                    // 실측: 8×8m 광폭 복도 — 남·북 전폭(2셀) 개구, 동·서는 벽·아치창
                    TemplateId = "hallway_x2",
                    WidthCells = 2,
                    HeightCells = 2,
                    AllowedFloors = FloorMask.F1,
                    Tags = RoomTagMask.None,
                    MinCount = 0,
                    MaxCount = 8, // 상동 — 60방 스케일 보충
                    IsCorridor = true,
                    IsEntranceAnchor = false,
                    Sockets = new[]
                    {
                        new SocketDef { Id = 0, LocalCell = new CellCoord(0, 0), Direction = SocketDirection.South },
                        new SocketDef { Id = 1, LocalCell = new CellCoord(1, 0), Direction = SocketDirection.South },
                        new SocketDef { Id = 2, LocalCell = new CellCoord(0, 1), Direction = SocketDirection.North },
                        new SocketDef { Id = 3, LocalCell = new CellCoord(1, 1), Direction = SocketDirection.North },
                    },
                    Markers = new MarkerDef[0],
                },
            };
        }
    }
}
