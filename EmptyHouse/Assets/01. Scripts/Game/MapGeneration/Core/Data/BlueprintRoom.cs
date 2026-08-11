namespace EmptyHouse.MapGen.Core
{
    /// <summary>배치된 방 인스턴스(1절 rooms[]) — MapBlueprint.Rooms의 인덱스가 방 번호다.</summary>
    public sealed class BlueprintRoom
    {
        public string TemplateId; // 사용 템플릿 ID
        public CellCoord Cell; // 그리드 좌표(회전 적용 후 풋프린트 원점, 셀 단위 정수)
        public Rotation4 Rotation; // 회전
        public int FloorIndex; // 층 서수(부호) — B1 = -1 · 1F = 0 · 2F = +1. 층 1개 구성은 전부 0(v1 동일)
        public int TemplateIndex = -1; // MapGenPlan.FlatTemplates 인덱스 — TemplateId 문자열 선형 탐색 대체. -1 = 미배정(v1 경로)
    }
}
