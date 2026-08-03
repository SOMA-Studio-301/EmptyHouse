namespace EmptyHouse.MapGen.Core
{
    /// <summary>
    /// 생성 파라미터(9절). 수치는 ⚪ 초기값 — 인스펙터(SO) 노출은 어댑터 계층 소관.
    /// 수치 변경은 문서 버전과 무관, 규칙 변경만 버전을 올린다.
    /// </summary>
    public sealed class MapGenParams
    {
        public int Seed; // 확정 시드 — 0(랜덤)은 코어 진입 전에 서버가 실제 값으로 확정한다(X8)
        public int RoomsTotalMin = 30; // 총 방 수 하한(정식 확정 30~32) — v1 단일 층 테스트는 축소 운용 가능
        public int RoomsTotalMax = 32; // 총 방 수 상한
        public int LoopEdgeCountMin = 2; // 트리에 추가하는 루프 간선 최소(🟡 미확정 — 개발용 임시값)
        public int LoopEdgeCountMax = 4; // 트리에 추가하는 루프 간선 최대(🟡 미확정 — 개발용 임시값)
        public int ShortcutValueMin = 3; // 지름길 채택 최소 귀환 단축 이득(방 수, 4-2절)
        public int ListenerCounterDist = 2; // Listener 관문 앞 투척물 배치 보장 그래프 거리(방 수, 5절)
        public int RerollMax = 20; // 검증 실패 시 리롤 상한(X2)
    }
}
