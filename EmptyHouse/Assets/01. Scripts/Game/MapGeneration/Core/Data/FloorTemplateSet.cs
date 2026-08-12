namespace EmptyHouse.MapGen.Core
{
    /// <summary>
    /// 층 전용 템플릿 세트(M8) — 테마 격리의 실체.
    /// 층마다 배열을 따로 두어 **다른 층 템플릿이 후보 리스트에 들어갈 경로 자체를 없앤다**
    /// (마스크 게이트 방식이었다면 후보 수가 달라져 v1 하위호환 rng 소비량이 어긋난다).
    /// </summary>
    public sealed class FloorTemplateSet
    {
        public int FloorIndex; // 층 서수 — FloorGenParams.FloorIndex 와 일치해야 한다(X4 검사)
        public string ThemeId; // 테마 키 — FloorGenParams.ThemeId 와 대조(드리프트 표면화)
        public RoomTemplateDef[] Templates; // 이 층에서만 쓰이는 템플릿 목록(입구 앵커·계단실·방·복도)
    }
}
