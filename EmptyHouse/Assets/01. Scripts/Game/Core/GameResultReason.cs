/// <summary>
/// 세션 종료 결과 — 서버가 판정해 복제한다. Result 국면에서 어느 결과창을 띄울지 결정한다(세션루프.md 5장).
/// 활성==0 에 도달한 경로로 갈린다(D35): 귀환자 0(전멸/전원 포기) → GameOver, 귀환자 ≥ 1 → Settlement.
/// 완수(메타 승리)는 post-MVP(D38)라 여기 없다. None 은 진행 중 기본값(미확정)이다.
/// </summary>
public enum GameResultReason
{
    None,       // 진행 중(미확정). NetworkVariable 초기값.
    GameOver,   // 활성 0 && 귀환자 0(전멸 또는 전원 포기) → 게임오버 결과창. "이 외출은 실패했다"(D35).
    Settlement, // 활성 0 && 귀환자 ≥ 1 → 귀환 정산 요약창. 승패 아닌 중립 정산(D35).
}
