/// <summary>
/// 결과창 개인 칭호 식별자(기획: 결과창_개인칭호_임시.md). 네트워크 페이로드(PlayerTitle)에 실리므로 값을 안정적으로 유지한다.
/// 스텁 단계 — 기획 확정 시 긍정·중립·부정·폴백 전 종이 여기에 추가되고, 배정은 TitleAssigner(미작성)가 맡는다.
/// </summary>
public enum TitleId
{
    None = 0,         // 미배정. UIResult 가 DefaultTitleKey 폴백으로 표시한다.
    LazyTeammate = 1, // 게으른 팀원 — 스텁 단계 전원 고정 배정(기획서 F1 문구 차용).
}
