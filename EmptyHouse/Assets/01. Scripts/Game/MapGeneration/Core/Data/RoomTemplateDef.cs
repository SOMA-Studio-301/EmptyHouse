namespace EmptyHouse.MapGen.Core
{
    /// <summary>
    /// 방/복도 템플릿 서술자(2절 RoomTemplateSO의 순수 데이터 투영).
    /// 코어는 SO·프리팹을 모른다 — 어댑터가 RoomTemplateSO에서 추출해 전달한다(AC-03).
    /// </summary>
    public sealed class RoomTemplateDef
    {
        public string TemplateId; // 템플릿 ID — 어댑터의 SO·프리팹 매칭 키
        public int WidthCells; // 풋프린트 가로(셀 정수배)
        public int HeightCells; // 풋프린트 세로(셀 정수배)
        public FloorMask AllowedFloors; // 허용 층(v1은 F1만 사용)
        public RoomTagMask Tags; // 태그(어두움 등)
        public int MinCount; // 최소 등장 횟수
        public int MaxCount; // 최대 등장 횟수
        public bool IsCorridor; // 복도 템플릿 여부(직선/코너/T자)
        public bool IsEntranceAnchor; // 버스 입구 고정 모듈 여부(3절 — 레이아웃 시작 앵커, 입구=출구)
        public bool IsStairAnchor; // 계단실 여부(M8 SSA) — 시드 층에선 일반 방처럼 소켓 접합으로 심고, 그 좌표를 전 층이 공유한다. 전 층 계단 템플릿은 풋프린트·소켓 배열이 동일해야 한다(X4)
        public SocketDef[] Sockets; // 출입구 소켓 목록
        public MarkerDef[] Markers; // 스폰 마커 목록
    }
}
