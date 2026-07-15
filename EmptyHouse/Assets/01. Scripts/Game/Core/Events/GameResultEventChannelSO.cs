using Border.Core;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 세션 결과 브로드캐스트 이벤트 채널. 패키지 Border.Events 채널과 동일한 형태의 게임 전용 확장이다.
/// ServerGameManager 가 결과를 확정한 뒤(각 클라의 NetworkVariable OnValueChanged 시점) 발행하며,
/// 클라 UI 매니저(UIManager, 사유를 UIResult 로 핸드오프)와 ClientGameManager 가 대등한 구독자로서 수신한다.
/// GameResultReason 이 게임 전용 타입이므로 이 채널은 패키지가 아닌 게임 코드에 둔다.
/// </summary>
[CreateAssetMenu(fileName = "GameResultEventChannelSO", menuName = "Events/GameResult")]
public class GameResultEventChannelSO : ScriptableObject
{
    public UnityAction<GameResultReason> OnEventRaised = delegate { };

    /// <summary>
    /// 마지막으로 발행된 결과. 발행 이후 활성화된 늦은 구독자가 초기 동기화에 읽는다.
    /// 자동 프로퍼티라 직렬화되지 않는 런타임 전용 값이다.
    /// </summary>
    public GameResultReason CurrentReason { get; private set; }

    /// <summary>
    /// SO 활성화 시 CurrentReason 을 None 으로 초기화한다.
    /// 에디터에서 SO 는 플레이 세션 사이에 살아남으므로 이전 세션 값이 남지 않게 한다.
    /// </summary>
    private void OnEnable()
    {
        CurrentReason = GameResultReason.None;
    }

    /// <summary>새 세션 결과를 CurrentReason 에 기록하고 모든 구독자에게 방송한다.</summary>
    /// <param name="reason">확정된 종료 사유.</param>
    public void RaiseEvent(GameResultReason reason)
    {
        CurrentReason = reason;
        OnEventRaised.Invoke(reason);
    }
}
