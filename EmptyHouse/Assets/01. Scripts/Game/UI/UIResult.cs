using Border.Core;
using UnityEngine;

/// <summary>
/// 세션 결과창 — 서버가 확정한 결과를 표시하는 클라 UI(세션루프.md 5장). 자기 가시성은 UIManager 소유다:
/// UIManager 가 결과 채널을 받아 이 패널을 켜고 확정 사유를 넘겨주며, 이쪽은 넘겨받은 사유에 맞는
/// 게임오버 / 정산 요약 패널 중 하나만 띄운다 — UIPause·UISettings 와 같은 수동적 화면 컨트롤러다.
/// 폰이 죽어도 살아남아야 하는 세션 스코프 UI라 Player 자식이 아니라 클라 UI 계층에 둔다(전멸 시 내 폰 despawn 에도 표시).
/// 버튼은 없다 — 로비 복귀는 8초(⚪) 후 서버가 자동 전이하므로(D35, 5-1), 이 창은 카운트다운만 표시한다.
/// </summary>
public class UIResult : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private GameObject gameOverPanel;   // GameOver → "이 외출은 실패했다"(전멸/전원 포기)
    [SerializeField] private GameObject settlementPanel;  // Settlement → 귀환 정산 요약(회수물·백신 달성 여부·사망자 드롭)

    /// <summary>
    /// UIManager 가 넘긴 확정 결과에 맞는 패널을 켜고 로비 복귀 카운트다운을 표시한다.
    /// GameOver → 게임오버 패널 / Settlement → 정산 요약 패널. None 은 UIManager 가 걸러 도달하지 않는다.
    /// 확인 버튼은 없다 — 복귀는 서버 타이머가 자동으로 하고 여기서는 카운트다운만 보여준다(5-1).
    /// </summary>
    /// <param name="reason">서버가 확정한 종료 결과.</param>
    public void Show(GameResultReason reason)
    {
        Log.D($"[UIResult] Show {reason}");
        gameOverPanel.SetActive(reason == GameResultReason.GameOver);
        settlementPanel.SetActive(reason == GameResultReason.Settlement);

        // TODO(impl): 정산 패널은 귀환/사망 인원·백신 달성 여부 표시. 8초(⚪) 카운트다운 텍스트 시작.
    }
}
