using UnityEngine;

/// <summary>
/// GameState 방송을 구독해 입력 액션맵과 커서 모드를 상태에 맞게 전환하는 씬 컴포넌트.
/// 상태를 결정하지 않는다 — 채널의 리스너 중 하나일 뿐이며, 싱글톤도 DontDestroyOnLoad 도 아니다.
/// 씬마다 하나씩 배치하고 initialState 로 씬 진입 상태를 지정한다.
/// 멀티플레이이므로 어떤 상태에서도 Time.timeScale 을 건드리지 않고, Unity.Netcode 에 의존하지 않는다.
/// </summary>
public class GameManager : MonoBehaviour
{
    [Header("Input")]
    [SerializeField] private InputReader inputReader;

    [Header("State")]
    [SerializeField] private GameStateEventChannelSO gameStateChanged;

    /// <summary>씬 진입 시 발행할 초기 상태. Menu/Lobby 씬은 Menu, Game 씬은 Game 으로 설정한다.</summary>
    [SerializeField] private GameState initialState = GameState.Menu;

    /// <summary>상태 방송 구독을 시작한다.</summary>
    private void OnEnable()
    {
        gameStateChanged.OnEventRaised += HandleStateChanged;
    }

    /// <summary>상태 방송 구독을 해제한다. SO 채널에 죽은 델리게이트가 남지 않게 OnEnable 과 짝을 맞춘다.</summary>
    private void OnDisable()
    {
        gameStateChanged.OnEventRaised -= HandleStateChanged;
    }

    /// <summary>씬 진입 상태를 채널로 발행한다. 자기 자신도 구독자로서 이를 수신해 입력·커서를 적용한다.</summary>
    private void Start()
    {
        HandleStateChanged(initialState);
    }

    /// <summary>
    /// 수신한 상태에 따라 입력 액션맵과 커서를 전환한다.
    /// Game → Gameplay 맵 활성화 + 커서 잠금·숨김. Menu/Pause → UI 맵 활성화 + 커서 해제·표시.
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
                inputReader.EnableUIInput();
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                break;
        }
    }
}
