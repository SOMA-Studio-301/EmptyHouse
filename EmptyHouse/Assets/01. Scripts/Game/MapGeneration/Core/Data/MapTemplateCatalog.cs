using System.Collections.Generic;
using Border.Core;
using EmptyHouse.MapGen.Core;

namespace EmptyHouse.MapGen.Runtime
{
    /// <summary>
    /// 런타임 템플릿 카탈로그 — 서버와 전 클라이언트가 같은 시드로 같은 블루프린트를 재생성할 때 쓰는
    /// 단일 템플릿 세트(8절 결정론 — 재료가 갈라지면 서버·클라 조립이 어긋나 AC-02 가 깨진다).
    /// P5 풀버전(RoomTemplateSO 세트) 전까지의 코드 카탈로그.
    /// 실측 근거·소켓 정렬 불변식(c ↔ L−1−c 자기 대칭)은 에디터 PrefabRoomTemplates 헤더 주석 참조 —
    /// 그쪽 Create() 는 이 메서드 위임으로 축소됐다(정의 단일 원천).
    /// 파일 위치는 Core/Data — 순수 데이터라 엔진 무의존이고, 에디터 asmdef 가 Assembly-CSharp(Adapter 폴더)를
    /// 참조할 수 없어 위임이 성립하려면 Core 어셈블리에 있어야 한다.
    /// </summary>
    public static class MapTemplateCatalog
    {
        public const float CellMeters = 4f; // 셀 실측(m) — Hall_Floor_4M 기준(G1). 인스턴스화 배율의 단일 원천

        /// <summary>
        /// 실측 프리팹 기반 템플릿 세트를 만든다 — 호출마다 새 인스턴스(호출자 오버라이드가 타 소비자를 오염시키지 않게).
        /// 입구 = 전용 프리팹(Rooms/Entrance-EmptyRoom-6x6, 남변은 버스 입구 벽이라 소켓 없음),
        /// 방 3종 MaxCount 합(40+24+2=66)이 예산 상한(60)을 감당한다. 복도는 예산 밖 — 개수 제한은 각자의 MaxCount 뿐.
        /// </summary>
        /// <returns>런타임 템플릿 목록.</returns>
        public static List<RoomTemplateDef> Create()
        {
            Log.D("[MapTemplateCatalog] Create");
            return new List<RoomTemplateDef>
            {
                new RoomTemplateDef
                {
                    // 입구 = DecoratedRooms/Entrance 전용 프리팹(벽 6m). 소켓 실측 규약(2026-08-10 갱신):
                    // 북 = 4번째 칸(3,5)의 기존 개구(중심 로컬 x=+0.4, 문틀 아트) — 의무 문(항상 방 직결 + 문),
                    // 동·서 = 북쪽 4셀(y 2~5)이 계단·2층 구조라 소켓 금지 → 하단 y=1 한 곳씩,
                    // 남 = 버스 입구 벽이라 소켓 없음(= 그 뒤는 집 밖. LayoutGenerator 확보 대역이 이 사실을 읽는다).
                    // 동·서가 1개씩이라 소켓 열 집합이 c ↔ L−1−c 자기 대칭을 못 이룬다 — 입구는 회전하지 않아
                    // 트리 연결에는 무해하고, 대신 입구 쪽 루프 간선 후보가 줄어드는 것을 감수한 결정이다
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
                        new SocketDef { Id = 0, LocalCell = new CellCoord(3, 5), Direction = SocketDirection.North, MandatoryDoor = true },
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
                        new MarkerDef { Id = 1, Kind = MarkerKind.ItemSpawn, LocalCell = new CellCoord(2, 2), ItemMask = ItemCategoryMask.Vaccine | ItemCategoryMask.Key | ItemCategoryMask.Fuel | ItemCategoryMask.Scrap | ItemCategoryMask.Throwable },
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
                        new MarkerDef { Id = 2, Kind = MarkerKind.ItemSpawn, LocalCell = new CellCoord(0, 5), ItemMask = ItemCategoryMask.Vaccine | ItemCategoryMask.Key | ItemCategoryMask.Fuel | ItemCategoryMask.Scrap | ItemCategoryMask.Throwable },
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
                    MaxCount = 44, // 방 60개·경유율 100% 수요(~30) + 사이클 복도 브리지 여유분(실측 평균 13.6쌍, 2026-08-06). 예산 밖 — 개수 제한은 이 값뿐
                    IsCorridor = true,
                    IsEntranceAnchor = false,
                    Sockets = new[]
                    {
                        new SocketDef { Id = 0, LocalCell = new CellCoord(0, 0), Direction = SocketDirection.South },
                        new SocketDef { Id = 1, LocalCell = new CellCoord(0, 1), Direction = SocketDirection.North },
                    },
                    Markers = new MarkerDef[0],
                },
                // hallway_x2(8×8m 광폭 복도)는 2026-08-10 카탈로그에서 제외했다. 단부가 소켓 2개(전폭 8m)라
                // 한쪽만 짝이 맺히면 나머지 반쪽이 봉인돼 통로 한가운데 가벽이 선다 —
                // 실측(58~60방 300시드) 맞은편이 차 있는 복도 봉인 2701건이 전부 x2 발이었고, 일반 복도는 0건이었다.
                // 빼고 나니 복도 봉인 자체가 0이 되고 사이클 소속 방 59.5% → 66.6%, 자물쇠 우회 위험 132 → 0.
                // 프리팹(Hallway x2)과 레지스트리 항목은 남겨 둔다 — 단부 소켓이 항상 쌍으로 맺히는 규칙이 생기면 되살린다.
            };
        }
    }
}
