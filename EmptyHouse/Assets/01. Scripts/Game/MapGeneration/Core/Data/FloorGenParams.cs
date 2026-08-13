namespace EmptyHouse.MapGen.Core
{
    /// <summary>
    /// 층별 생성 파라미터(M8 다층 v2). 전역 항목(시드·리롤·자물쇠 수·필수 아이템 수)은 <see cref="MapGenParams"/> 에 남고,
    /// 층 안에서만 의미를 갖는 예산·난이도·좀비 구성이 이리로 내려온다.
    /// 층 = 테마 격리 단위라 <see cref="ThemeId"/> 가 어댑터 프리팹 세트 조회 키를 겸한다.
    /// </summary>
    [System.Serializable]
    public sealed class FloorGenParams
    {
        public int FloorIndex; // 층 서수(부호) — B1 = -1, 1F = 0, 2F = +1. 0 = 입구 앵커가 놓이는 시드 층
        public string ThemeId; // 테마 키 — 어댑터 층 정의(FloorDefinitionSO) 대조 + 해시 폴딩 대상(클라 간 테마 불일치를 AC-02 가 잡는다)
        public int RoomsTotalMin = 10; // 층 방 예산 하한 — 방 전용 집계(복도·입구 앵커·계단실 제외)
        public int RoomsTotalMax = 11; // 층 방 예산 상한
        public int CycleRoomPercent = 40; // 층 사이클 소속 방 목표 비율 %
        public int CorridorLinkPercent = 100; // 층 복도 경유 연결 확률 %
        public int CorridorChainMax = 3; // 층 복도 연쇄 상한
        public int DangerBias; // 위험 등급 층 가중(스펙 3절 v2 — B1 > 2F > 1F). 0 이면 깊이만으로 산출(v1 동일)
        public int ZombieDensitySafeMin; // 안전 등급 방 좀비 최소 ⚪
        public int ZombieDensitySafeMax = 1; // 안전 등급 방 좀비 최대 ⚪
        public int ZombieDensityMidMin = 1; // 중간 등급 방 좀비 최소 ⚪
        public int ZombieDensityMidMax = 2; // 중간 등급 방 좀비 최대 ⚪
        public int ZombieDensityDangerMin = 2; // 위험 등급 방 좀비 최소 ⚪
        public int ZombieDensityDangerMax = 3; // 위험 등급 방 좀비 최대 ⚪
        public int ListenerRatioPercent = 25; // 비어둠 방에서 Listener 를 고를 확률 % ⚪
        public int HerdZombieCountMin = 3; // 위장 무대 Walker 무리 최소 ⚪
        public int HerdZombieCountMax = 5; // 위장 무대 Walker 무리 최대 ⚪
        public ZombieTypeMask EnabledZombieTypes = ZombieTypeMask.Walker; // 층별 배치 활성 좀비 타입 — 층마다 다른 압박을 주는 노브
        public int ReturnExitCount; // 층 탈출문 수(M10-1 — 구 MapGenParams.ReturnExitCount 에서 이관) — 비입구 층 기본 0, FromLegacy 가 v1 값을 시드 층에 매핑 ⚪
        public int WardrobeCount; // 층 벽장 수(M10-1 — 구 MapGenParams.WardrobeCount 에서 이관) ⚪
    }
}
