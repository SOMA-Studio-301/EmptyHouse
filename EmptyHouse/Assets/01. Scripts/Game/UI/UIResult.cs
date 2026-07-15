using Border.Core;
using UnityEngine;

/// <summary>
/// 세션 결과창 — 서버가 확정한 결과를 표시하는 클라 UI(세션루프.md 5장). GameResultEventChannelSO 를 구독해
/// 스스로 켜지고, 결과에 따라 게임오버 패널 / 정산 요약 패널 중 하나를 띄운다.
/// 폰이 죽어도 살아남아야 하는 세션 스코프 UI라 Player 자식이 아니라 클라 UI 계층에 둔다(전멸 시 내 폰 despawn 에도 표시).
/// 버튼은 없다 — 로비 복귀는 8초(⚪) 후 서버가 자동 전이하므로(D35, 5-1), 이 창은 카운트다운만 표시한다.
/// </summary>
public class UIResult : MonoBehaviour
{
    [Header("Result Channel")]
    [SerializeField] private GameResultEventChannelSO gameResultChanged;

    [Header("Panels")]
    [SerializeField] private GameObject gameOverPanel;   // GameOver → "이 외출은 실패했다"(전멸/전원 포기)
    [SerializeField] private GameObject settlementPanel;  // Settlement → 귀환 정산 요약(회수물·백신 달성 여부·사망자 드롭)

    /// <summary>결과 채널을 구독하고 두 패널을 닫은 상태로 초기화한다. 이 컴포넌트 자체는 상시 활성이라 채널을 계속 듣는다.</summary>
    private void OnEnable()
    {
        gameResultChanged.OnEventRaised += HandleGameResult;

        gameOverPanel.SetActive(false);
        settlementPanel.SetActive(false);
    }

    /// <summary>구독을 해제한다. OnEnable 과 짝을 맞춰 죽은 델리게이트를 남기지 않는다.</summary>
    private void OnDisable()
    {
        gameResultChanged.OnEventRaised -= HandleGameResult;
    }

    /// <summary>
    /// 종료 결과에 맞는 패널을 켜고 로비 복귀 카운트다운을 표시한다.
    /// GameOver → 게임오버 패널 / Settlement → 정산 요약 패널. None 은 진행 중 기본값이라 무시한다.
    /// 확인 버튼은 없다 — 복귀는 서버 타이머가 자동으로 하고 여기서는 카운트다운만 보여준다(5-1).
    /// </summary>
    /// <param name="reason">서버가 확정한 종료 결과.</param>
    private void HandleGameResult(GameResultReason reason)
    {
        // TODO(impl): reason==None 무시. GameOver→gameOverPanel / Settlement→settlementPanel SetActive.
        //             정산 패널은 귀환/사망 인원·백신 달성 여부 표시. 8초(⚪) 카운트다운 텍스트 시작.
        Log.D($"[UIResult] HandleGameResult {reason}");
    }
}
