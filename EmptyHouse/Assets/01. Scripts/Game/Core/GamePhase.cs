/// <summary>
/// 세션 국면 — 서버 권위 상태. ServerGameManager 가 NetworkVariable 로 들고 전 클라에 복제한다.
/// 클라 로컬 입력을 결정하는 GameState 와는 다른 축이다: 이쪽은 "세션이 어느 단계인가", 저쪽은 "지금 어떤 입력·커서인가".
/// </summary>
public enum GamePhase
{
    InProgress,       // 스폰 완료·플레이 중. 종료 조건(활성==0)을 매 변화마다 재판정한다.
    Result,           // 활성==0 도달. 결과 확정·결과창 표시. 판정은 정지(래치)한다.
    ReturningToLobby, // 호스트가 복귀를 발동, 세션 정리(NGO teardown) 진행 중. 중복 트리거 방지 래치.
}
