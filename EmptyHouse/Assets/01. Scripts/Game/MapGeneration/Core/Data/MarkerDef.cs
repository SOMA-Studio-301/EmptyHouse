namespace EmptyHouse.MapGen.Core
{
    /// <summary>
    /// 스폰 마커 정의(2절) — 배치 가능 위치 보장은 알고리즘이 아니라 사람이 검증한 마커가 담당한다.
    /// </summary>
    public sealed class MarkerDef
    {
        public int Id; // 템플릿 내 마커 번호
        public MarkerKind Kind; // 마커 종류
        public CellCoord LocalCell; // 템플릿 로컬 셀 좌표
        public ZombieTypeMask ZombieMask; // ZombieSpawn 전용 — 허용 좀비 타입
        public ItemCategoryMask ItemMask; // ItemSpawn 전용 — 허용 아이템 카테고리
        public float WanderRadiusCells; // 좀비 배회 반경 기본값(셀) — 검증 툴에서 편집(10절)
    }
}
