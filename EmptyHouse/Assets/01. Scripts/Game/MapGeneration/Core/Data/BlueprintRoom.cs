namespace EmptyHouse.MapGen.Core
{
    /// <summary>배치된 방 인스턴스(1절 rooms[]) — MapBlueprint.Rooms의 인덱스가 방 번호다.</summary>
    public sealed class BlueprintRoom
    {
        public string TemplateId; // 사용 템플릿 ID
        public CellCoord Cell; // 그리드 좌표(회전 적용 후 풋프린트 원점, 셀 단위 정수)
        public Rotation4 Rotation; // 회전
    }
}
