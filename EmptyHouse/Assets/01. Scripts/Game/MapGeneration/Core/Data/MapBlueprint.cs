using System.Collections.Generic;

namespace EmptyHouse.MapGen.Core
{
    /// <summary>생성 메타(1절 meta) — 시드·생성기 버전·파라미터 스냅샷. 시드가 곧 재현 키다(AC-17).</summary>
    public sealed class MapBlueprintMeta
    {
        public int Seed; // 확정 시드
        public string GeneratorVersion; // 생성기 버전 — "같은 빌드 + 같은 시드 = 같은 결과" 보장 범위(8절)
        public MapGenParams ParamsSnapshot; // 생성 시점 파라미터 스냅샷
        public float CycleRoomPercentAchieved; // 달성한 사이클 소속 방 비율 %(비복도 방 기준) — 목표(CycleRoomPercent) 미달 시 X6 경고 근거. 해시 미포함 진단값
    }

    /// <summary>
    /// 생성 알고리즘의 출력(1절) — 프리팹 인스턴스가 아닌 순수 데이터.
    /// 검증(7절)·기획자 툴(10절)·인스턴스화·상태 오브젝트 스폰이 전부 이 데이터만 소비한다(AC-03).
    /// </summary>
    public sealed class MapBlueprint
    {
        public MapBlueprintMeta Meta = new MapBlueprintMeta(); // 생성 메타
        public List<BlueprintRoom> Rooms = new List<BlueprintRoom>(); // 방 목록 — 인덱스 = 방 번호, 0 = 버스 입구 앵커
        public List<BlueprintEdge> Edges = new List<BlueprintEdge>(); // 간선 목록
        public List<BlueprintSpawn> Spawns = new List<BlueprintSpawn>(); // 스폰 목록
    }
}
