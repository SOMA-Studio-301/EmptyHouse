namespace EmptyHouse.MapGen.Core
{
    /// <summary>
    /// 생성 파라미터(9절). 수치는 ⚪ 초기값 — 인스펙터(SO) 노출은 어댑터 계층 소관.
    /// 수치 변경은 문서 버전과 무관, 규칙 변경만 버전을 올린다.
    /// </summary>
    [System.Serializable] // 에디터 툴(10절) 파라미터 오버라이드 보존·향후 SO 어댑터 인스펙터 노출용
    public sealed class MapGenParams
    {
        public int Seed; // 확정 시드 — 0(랜덤)은 코어 진입 전에 서버가 실제 값으로 확정한다(X8)
        public int RoomsTotalMin = 58; // 총 방 수 하한 — 방 전용 집계(복도·입구 앵커 제외), 미리보기 튜닝 확정(2026-08-06)
        public int RoomsTotalMax = 60; // 총 방 수 상한 — 방 전용 집계
        public int CycleRoomPercent = 40; // 사이클 소속 방 목표 비율 %(0 = 순수 트리) — 인접쌍 개방+복도 브리지로 목표까지 채택, 기하 상한(~67, 2026-08-06 실측) 초과분은 베스트에포트+X6 경고
        public int CorridorLinkPercent = 100; // 방 확장 시 복도 경유 연결 확률 %(0=전부 직결) — 복도는 방+복도 원자 배치라 막다른 끝이 생기지 않는다. 미리보기 튜닝 확정(2026-08-06, 복도 MaxCount 소진 시 직결 폴백이 혼합을 만든다)
        public int CorridorChainMax = 3; // 복도 연쇄 최대 세그먼트 수(1 = 연쇄 없음) — 경유 연결마다 1~Max 균등 롤, 먼 방 연결용. 체인 전체가 원자 트랜잭션이라 막다른 끝 불변
        public int ShortcutValueMin = 3; // 지름길 채택 최소 귀환 단축 이득(방 수, 4-2절)
        public int ListenerCounterDist = 2; // Listener 관문 앞 투척물 배치 보장 그래프 거리(방 수, 5절)
        public int RerollMax = 20; // 검증 실패 시 리롤 상한(X2)
        public int ShortcutLockCountMin = 2; // 지름길 자물쇠 최소(레벨디자인 5절 🟢 2~3) — 가치 통과 후보가 부족하면 있는 만큼만 채택
        public int ShortcutLockCountMax = 3; // 지름길 자물쇠 최대(레벨디자인 5절 🟢)
        public int ItemDoorLockCount = 2; // 중요 물품 문 자물쇠 수(레벨디자인 5절 🟢)
        public int ZombieDensitySafeMin = 0; // 안전 등급 방 좀비 최소(미리보기 튜닝 확정 2026-08-06 — 앞 구역 무좀비)
        public int ZombieDensitySafeMax = 0; // 안전 등급 방 좀비 최대 ⚪
        public int ZombieDensityMidMin = 0; // 중간 등급 방 좀비 최소 ⚪
        public int ZombieDensityMidMax = 2; // 중간 등급 방 좀비 최대 ⚪
        public int ZombieDensityDangerMin = 3; // 위험 등급 방 좀비 최소 ⚪
        public int ZombieDensityDangerMax = 5; // 위험 등급 방 좀비 최대 ⚪
        public int ListenerRatioPercent = 25; // 비어둠 방에서 Listener 를 고를 확률 %(⚪ — 타입 게이트로 Listener 꺼지면 무시)
        public int ThrowableBudget = 4; // 회피 예산분 투척물 개수(D4 — 외출마다 재배치 ⚪)
        public int OilCount = 3; // 기름 배치 수(⚪ — 깊은 구역 집중, G2)
        public int ScrapCount = 6; // 스크랩 배치 수(⚪ — 깊이 비례, G2)
        public int HerdZombieCountMin = 3; // 위장 무대 Walker 무리 최소(⚪ — 레벨디자인 4절)
        public int HerdZombieCountMax = 5; // 위장 무대 Walker 무리 최대 ⚪
        public ZombieTypeMask EnabledZombieTypes = ZombieTypeMask.Walker; // 배치 활성 좀비 타입 — 현재 Walker만 게임에 구현되어 기본값 Walker. Watcher·Listener 는 구현 후 활성화
    }
}
