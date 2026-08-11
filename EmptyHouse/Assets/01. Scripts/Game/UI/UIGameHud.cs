using Border.Core;
using UnityEngine;

/// <summary>
/// 인게임 HUD 루트(Canvas-HUD)의 표시 전환자. GameStateEventChannelSO 를 구독해 게임플레이 HUD 와 관전 HUD 를 맞바꾼다.
/// 관전 진입(Spectating)은 되돌아오지 않는 전이로 취급한다 — MVP 는 부활(D18)이 없어 한 번 관전이면 세션 끝까지 관전이다.
/// 그래서 진입을 isSpectating 에 기억해 두고, 이후 들어오는 Game 은 퍼즈 복귀(UIManager.ResumeGame)로 해석해 관전 HUD 를 유지한다.
/// Pause/Result/Menu 는 관여하지 않는다 — 퍼즈·결과 패널이 HUD 위를 덮는 구조이며 그 화면들은 UIManager 소관이다.
/// ⚠ 부활(D18) 구현 시 PlayerSpectatorController.ExitSpectate 도 Game 을 발행하므로 "퍼즈 복귀 Game"과 구분되지 않는다.
///   그 시점에는 관전 전용 채널(BoolEventChannelSO)로 승격해야 한다.
/// </summary>
public class UIGameHud : MonoBehaviour
{
    /// <summary>구독: 게임 상태 방송. Spectating 수신이 HUD 교체의 유일한 트리거다.</summary>
    [Header("State")]
    [SerializeField] private GameStateEventChannelSO gameStateChanged;

    /// <summary>게임플레이 HUD 묶음 루트(크로스헤어·인벤 등). 자기 자식이라 프리팹 단독으로 완결된다.</summary>
    [Header("Roots")]
    [SerializeField] private GameObject gameplayRoot;

    /// <summary>관전 HUD 묶음 루트. 관전 진입 시에만 켜진다.</summary>
    [SerializeField] private GameObject spectatorRoot;

    // 관전 진입 여부. 한 번 켜지면 내려가지 않으며, 이후 Game 수신을 퍼즈 복귀로 해석하는 근거가 된다.
    private bool isSpectating;

    private void Awake()
    {
        // 관전 진입 전까지는 게임플레이 HUD 를 켠다. 관전 진입 후에는 Game 수신을 퍼즈 복귀로 해석해 관전 HUD 를 유지한다.
        isSpectating = false;
        ApplyRoots(true);
    }

    /// <summary>상태 방송을 구독하고, 이미 발행된 마지막 상태로 초기 표시를 맞춘다(늦은 구독자 동기화).</summary>
    private void OnEnable()
    {
        gameStateChanged.OnEventRaised += HandleStateChanged;
        HandleStateChanged(gameStateChanged.CurrentState);
    }

    /// <summary>구독을 해제한다. 채널은 SO 라 씬 밖에서 살아남으므로 죽은 델리게이트를 남기지 않는다.</summary>
    private void OnDisable()
    {
        gameStateChanged.OnEventRaised -= HandleStateChanged;
    }

    /// <summary>
    /// 상태를 HUD 표시로 번역한다. Spectating 이면 관전 HUD 로 교체하고, Game 은 아직 관전이 아닐 때만 게임플레이 HUD 로 되돌린다.
    /// Pause/Result/Menu 는 현재 표시를 그대로 둔다 — 그 화면들이 HUD 위를 덮는다.
    /// </summary>
    /// <param name="state">방송된 게임 상태.</param>
    private void HandleStateChanged(GameState state)
    {
        switch (state)
        {
            case GameState.Spectating:
                isSpectating = true;
                ApplyRoots(false);
                break;

            case GameState.Game:
                if (isSpectating) return; // 관전 중의 Game 은 퍼즈 복귀다 — 관전 HUD 를 유지한다
                ApplyRoots(true);
                break;
        }
    }

    /// <summary>두 HUD 루트를 배타적으로 켠다.</summary>
    /// <param name="showGameplay">true 면 게임플레이 HUD, false 면 관전 HUD 를 표시.</param>
    private void ApplyRoots(bool showGameplay)
    {
        Log.D($"[UIGameHud] HUD 전환 — {(showGameplay ? "Gameplay" : "Spectator")}");

        gameplayRoot.SetActive(showGameplay);
        spectatorRoot.SetActive(!showGameplay);
    }
}
