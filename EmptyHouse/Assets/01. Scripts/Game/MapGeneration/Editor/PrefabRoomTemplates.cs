using System.Collections.Generic;
using Border.Core;
using EmptyHouse.MapGen.Core;

namespace EmptyHouse.MapGen.Editor
{
    /// <summary>
    /// 실제 방 프리팹(02. Prefab/Map) 실측 기반 템플릿 세트 — 예시 맵 씬 빌더(MapGenSceneBuilder) 재료.
    /// 셀 1칸 = 4m(Hall_Floor_4M 실측): 3x3 = 12×12m 방, 5x5 = 20×20m 방, 6x8 = 24×32m 방.
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
        public static readonly Dictionary<string, string> PrefabPaths = new Dictionary<string, string>
        {
            { "entrance_5x5", "Assets/02. Prefab/Map/DecoratedRooms/Entrance-EmptyRoom-5x5.prefab" },
            { "room_3x3", "Assets/02. Prefab/Map/EmptyRoom-3x3.prefab" },
            { "room_5x5", "Assets/02. Prefab/Map/EmptyRoom-5x5.prefab" },
            { "room_6x8", "Assets/02. Prefab/Map/EmptyRoom-6x8.prefab" },
            { "hallway", "Assets/02. Prefab/Map/Hallway.prefab" },
            { "hallway_x2", "Assets/02. Prefab/Map/Hallway x2.prefab" },
        };

        public const string DoorOpenedPath = "Assets/02. Prefab/Map/Door-Opened.prefab"; // 열린 문 프리팹
        public const string DoorClosedPath = "Assets/02. Prefab/Map/Door-Closed.prefab"; // 닫힌(잠긴) 문 프리팹

        /// <summary>
        /// 실측 프리팹 크기의 템플릿 세트를 만든다 — 소켓은 각 변 중앙 셀(짝수 변은 중앙 근사 2개),
        /// 마커 구성은 DevTemplateSet 과 같은 역할 분담(3x3 = 일반 / 5x5 = 어둠·발전기 / 6x8 = 위장 무대).
        /// </summary>
        /// <returns>씬 빌더용 템플릿 목록.</returns>
        public static List<RoomTemplateDef> Create()
        {
            Log.D("[PrefabRoomTemplates] Create");
            return new List<RoomTemplateDef>
            {
                new RoomTemplateDef
                {
                    TemplateId = "entrance_5x5",
                    WidthCells = 5,
                    HeightCells = 5,
                    AllowedFloors = FloorMask.F1,
                    Tags = RoomTagMask.None,
                    MinCount = 1,
                    MaxCount = 1,
                    IsCorridor = false,
                    IsEntranceAnchor = true,
                    Sockets = new[]
                    {
                        new SocketDef { Id = 0, LocalCell = new CellCoord(2, 4), Direction = SocketDirection.North },
                        new SocketDef { Id = 1, LocalCell = new CellCoord(0, 2), Direction = SocketDirection.West },
                        new SocketDef { Id = 2, LocalCell = new CellCoord(4, 2), Direction = SocketDirection.East },
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
                    MaxCount = 14,
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
                    TemplateId = "room_5x5",
                    WidthCells = 5,
                    HeightCells = 5,
                    AllowedFloors = FloorMask.F1,
                    Tags = RoomTagMask.Dark,
                    MinCount = 0,
                    MaxCount = 6,
                    IsCorridor = false,
                    IsEntranceAnchor = false,
                    Sockets = new[]
                    {
                        new SocketDef { Id = 0, LocalCell = new CellCoord(2, 0), Direction = SocketDirection.South },
                        new SocketDef { Id = 1, LocalCell = new CellCoord(2, 4), Direction = SocketDirection.North },
                        new SocketDef { Id = 2, LocalCell = new CellCoord(0, 2), Direction = SocketDirection.West },
                        new SocketDef { Id = 3, LocalCell = new CellCoord(4, 2), Direction = SocketDirection.East },
                    },
                    Markers = new[]
                    {
                        new MarkerDef { Id = 0, Kind = MarkerKind.ZombieSpawn, LocalCell = new CellCoord(2, 2), ZombieMask = ZombieTypeMask.Walker | ZombieTypeMask.Listener | ZombieTypeMask.Watcher, WanderRadiusCells = 2f },
                        new MarkerDef { Id = 1, Kind = MarkerKind.GeneratorSlot, LocalCell = new CellCoord(4, 4) },
                        new MarkerDef { Id = 2, Kind = MarkerKind.ItemSpawn, LocalCell = new CellCoord(0, 4), ItemMask = ItemCategoryMask.Vaccine | ItemCategoryMask.Key | ItemCategoryMask.Oil | ItemCategoryMask.Scrap | ItemCategoryMask.Throwable },
                        new MarkerDef { Id = 3, Kind = MarkerKind.CorpseStationSlot, LocalCell = new CellCoord(4, 0) },
                    },
                },
                new RoomTemplateDef
                {
                    TemplateId = "room_6x8",
                    WidthCells = 6,
                    HeightCells = 8,
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
                        new SocketDef { Id = 2, LocalCell = new CellCoord(1, 7), Direction = SocketDirection.North },
                        new SocketDef { Id = 3, LocalCell = new CellCoord(4, 7), Direction = SocketDirection.North },
                        new SocketDef { Id = 4, LocalCell = new CellCoord(0, 2), Direction = SocketDirection.West },
                        new SocketDef { Id = 5, LocalCell = new CellCoord(0, 5), Direction = SocketDirection.West },
                        new SocketDef { Id = 6, LocalCell = new CellCoord(5, 2), Direction = SocketDirection.East },
                        new SocketDef { Id = 7, LocalCell = new CellCoord(5, 5), Direction = SocketDirection.East },
                    },
                    Markers = new[]
                    {
                        new MarkerDef { Id = 0, Kind = MarkerKind.HerdArea, LocalCell = new CellCoord(2, 4) },
                        new MarkerDef { Id = 1, Kind = MarkerKind.ZombieSpawn, LocalCell = new CellCoord(4, 6), ZombieMask = ZombieTypeMask.Walker, WanderRadiusCells = 3f },
                        new MarkerDef { Id = 2, Kind = MarkerKind.ItemSpawn, LocalCell = new CellCoord(5, 7), ItemMask = ItemCategoryMask.Vaccine | ItemCategoryMask.Key | ItemCategoryMask.Throwable | ItemCategoryMask.Scrap },
                        new MarkerDef { Id = 3, Kind = MarkerKind.CorpseStationSlot, LocalCell = new CellCoord(0, 7) },
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
                    MaxCount = 14,
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
                    MaxCount = 4,
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
