using System.Collections.Generic;
using Border.Core;
using EmptyHouse.MapGen.Core;
using EmptyHouse.MapGen.Runtime;

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
        public const float CellMeters = MapTemplateCatalog.CellMeters; // 셀 실측(m) — 런타임 카탈로그가 단일 원천

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
        /// 실측 프리팹 크기의 템플릿 세트를 만든다 — 정의 원천은 런타임 MapTemplateCatalog(AC-22 드리프트 방지 위임).
        /// </summary>
        /// <returns>씬 빌더용 템플릿 목록.</returns>
        public static List<RoomTemplateDef> Create()
        {
            Log.D("[PrefabRoomTemplates] Create");
            return MapTemplateCatalog.Create();
        }
    }
}
