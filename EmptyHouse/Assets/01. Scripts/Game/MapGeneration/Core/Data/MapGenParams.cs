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
        public int ShortcutLockCountMin = 2; // 지름길 자물쇠 최소(레벨디자인 5절 🟢 2~3) — 가치 통과 후보가 부족하면 있는 만큼만 채택
        public int ShortcutLockCountMax = 3; // 지름길 자물쇠 최대(레벨디자인 5절 🟢)
        public int ItemDoorLockCount = 2; // 중요 물품 문 자물쇠 수(레벨디자인 5절 🟢)
        public int ZombieDensitySafeMin = 1; // 안전 등급 방 좀비 최소(레벨디자인 3-4 ⚪ 데모 수치)
        public int ZombieDensitySafeMax = 2; // 안전 등급 방 좀비 최대 ⚪
        public int ZombieDensityMidMin = 2; // 중간 등급 방 좀비 최소 ⚪
        public int ZombieDensityMidMax = 3; // 중간 등급 방 좀비 최대 ⚪
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
