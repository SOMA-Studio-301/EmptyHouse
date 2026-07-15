using Border.Core;
using UnityEngine;

/// <summary>
/// 클라 로컬 세션 상태 총괄자. 서버로 복제되지 않는 클라이언트 상태(입력 액션맵·커서, 결과 국면 반영)를 관리
/// 상태를 스스로 결정하지 않는다
///   - GameStateEventChannelSO 의 구독자
///   - 서버가 확정한 결과(GameResultEventChannelSO)
/// 씬마다 하나씩 배치한다.
/// 멀티플레이이므로 어떤 상태에서도 Time.timeScale 을 건드리지 않고, Unity.Netcode 에 의존하지 않는다.
/// 결과창 UI 표시는 별도 클라 UI(UIResult) 몫이며 이 클래스는 입력·커서 전환만 책임진다.
/// </summary>
public class ClientGameManager : MonoBehaviour
{
    [Header("Input")]
    [SerializeField] private InputReader inputReader;

    [Header("Listening to")]
    [SerializeField] private GameStateEventChannelSO gameStateChanged;
    [SerializeField] private GameResultEventChannelSO gameResultChanged;

    [Space(10f)]
    [SerializeField] private GameState initialState = GameState.Menu; // 씬 진입 시 발행할 초기 상태. Menu/Lobby 씬은 Menu, Game 씬은 Game 으로 설정

    /// <summary>상태·결과 방송 구독을 시작한다.</summary>
    private void OnEnable()
    {
        gameStateChanged.OnEventRaised += HandleStateChanged;
        gameResultChanged.OnEventRaised += HandleGameResult;
    }

    /// <summary>구독을 해제한다. SO 채널에 죽은 델리게이트가 남지 않게 OnEnable 과 짝을 맞춘다.</summary>
    private void OnDisable()
    {
        gameStateChanged.OnEventRaised -= HandleStateChanged;
        gameResultChanged.OnEventRaised -= HandleGameResult;
    }

    /// <summary>씬 진입 상태를 채널로 발행한다. 자기 자신도 구독자로서 이를 수신해 입력·커서를 적용한다.</summary>
    private void Start()
    {
        HandleStateChanged(initialState);
    }

    /// <summary>
    /// 수신한 상태에 따라 입력 액션맵과 커서를 전환한다.
    /// Game → Gameplay 맵 활성화 + 커서 잠금·숨김. Menu/Pause/Result → UI 맵 활성화 + 커서 해제·표시.
    /// </summary>
    /// <param name="state">방송된 게임 상태.</param>
    private void HandleStateChanged(GameState state)
    {
        switch (state)
        {
            case GameState.Game:
                inputReader.EnableGameplayInput();
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                break;

            case GameState.Menu:
            case GameState.Pause:
            case GameState.Result:
                inputReader.EnableUIInput();
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                break;
        }
    }

    /// <summary>
    /// 서버가 확정한 세션 결과를 클라 입력 상태로 번역한다 — Result 상태를 발행해 UI 입력·커서로 전환한다.
    /// 결과창 UI 표시 자체는 이 클래스가 아니라 클라 UI(UIResult)가 같은 채널을 구독해 처리한다.
    /// </summary>
    /// <param name="reason">종료 사유(성공/전멸/미완수 탈출). 여기서는 상태 전환에만 쓰고 문구는 UI 가 해석한다.</param>
    private void HandleGameResult(GameResultReason reason)
    {
        // TODO(impl): 결과창 UI 표시는 UIResult 핸드오프. 이 클래스는 입력·커서 전환만 책임진다.
        Log.D($"[ClientGameManager] HandleGameResult {reason}");
        gameStateChanged.RaiseEvent(GameState.Result);
    }
}
