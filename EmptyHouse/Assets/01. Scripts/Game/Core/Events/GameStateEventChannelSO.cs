using Border.Core;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// GameState 브로드캐스트 이벤트 채널. 패키지 Border.Events 채널과 동일한 형태의 게임 전용 확장이다.
/// 발행자가 상태를 결정하며, ClientGameManager 를 포함한 모든 리스너는 대등한 구독자다.
/// GameState 는 게임 전용 타입이므로 이 채널은 패키지가 아닌 게임 코드에 둔다.
/// </summary>
[CreateAssetMenu(fileName = "GameStateEventChannelSO", menuName = "Events/GameState")]
public class GameStateEventChannelSO : ScriptableObject
{
    public UnityAction<GameState> OnEventRaised = delegate { };

    /// <summary>
    /// 마지막으로 발행된 상태. 발행 이후 활성화된 늦은 구독자가 초기 동기화에 읽는다.
    /// 자동 프로퍼티라 직렬화되지 않는 런타임 전용 값이다.
    /// </summary>
    public GameState CurrentState { get; private set; }

    /// <summary>
    /// SO 활성화 시 CurrentState 를 Menu 로 초기화한다.
    /// 에디터에서 SO 는 플레이 세션 사이에 살아남으므로 이전 세션 값이 남지 않게 한다.
    /// </summary>
    private void OnEnable()
    {
        CurrentState = GameState.Menu;
    }

    /// <summary>새 게임 상태를 CurrentState 에 기록하고 모든 구독자에게 방송한다.</summary>
    /// <param name="state">전환할 게임 상태.</param>
    public void RaiseEvent(GameState state)
    {
        CurrentState = state;
        OnEventRaised.Invoke(state);
    }
}
