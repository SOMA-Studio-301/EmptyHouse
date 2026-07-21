using System.Collections;
using System.Collections.Generic;
using System.Text;
using Border.Core;
using Border.Localization;
using Unity.Services.Lobbies.Models;
using UnityEngine;

/// <summary>
/// 세션 결과창 — 서버가 확정한 결과를 표시하는 클라 UI(세션루프.md 5장). 자기 가시성은 UIManager 소유다:
/// UIManager 가 결과 채널을 받아 이 패널을 켜고 확정 사유를 넘겨준다 — UIPause·UISettings 와 같은 수동적 화면 컨트롤러다.
/// 패널은 하나뿐이며 사유에 따라 패널을 갈아끼우지 않는다. 타이틀(RESULT)은 프리팹 고정이라 코드가 관여하지 않는다.
/// 팀원 정보는 영속 <see cref="SessionCoordinator"/> 가 들고 있는 UGS 로비 스냅샷에서 읽는다 — 게임 씬에는 닉네임·스팀 ID 가 복제되지 않는다.
/// 폰이 죽어도 살아남아야 하는 세션 스코프 UI라 Player 자식이 아니라 클라 UI 계층에 둔다(전멸 시 내 폰 despawn 에도 표시).
/// 버튼은 없다 — 로비 복귀는 8초(⚪) 후 서버가 자동 전이하므로(D35, 5-1), 이 창은 카운트다운만 표시한다.
/// </summary>
public class UIResult : MonoBehaviour
{
    [Header("User Slots")]
    [SerializeField] private UIResultUser[] userSlots; // 프리팹에 미리 배치된 팀원 슬롯. 남는 자리는 숨긴다

    [Header("Lobby Return")]
    [SerializeField] private UILocalizeText lobbyReturnText;  // "n초 후 로비로 이동". 남은 초는 동적 prefix 로 주입한다
    [SerializeField] private float lobbyReturnSeconds = 8f;   // 표시용 카운트다운 길이. ServerGameManager.resultDisplaySeconds 와 값을 맞춘다(복제 아님)

    [Header("Localize Keys")]
    [LocalizeKey] public string DefaultMemoKey; // 전원 공통 임시 메모 키. 기여도 판정 기획 확정 시 슬롯별로 갈린다

    // 카운트다운 숫자 전용 버퍼. UILocalizeText 가 참조로 들고 있어 내용만 갈아끼운다.
    private readonly StringBuilder countdownBuilder = new StringBuilder(4);
    // 1초 대기 인스턴스. 매 틱 new 하면 GC 를 태운다.
    private readonly WaitForSeconds oneSecond = new WaitForSeconds(1f);
    private Coroutine countdownRoutine;

    /// <summary>결과창이 닫히면 카운트다운을 정지한다.</summary>
    private void OnDisable()
    {
        if (countdownRoutine == null) return;

        StopCoroutine(countdownRoutine);
        countdownRoutine = null;
    }

    /// <summary>
    /// UIManager 가 넘긴 확정 결과를 받아 팀원 슬롯을 그리고 로비 복귀 카운트다운을 시작한다.
    /// 확인 버튼은 없다 — 복귀는 서버 타이머가 자동으로 하고 여기서는 카운트다운만 보여준다(5-1).
    /// </summary>
    /// <param name="reason">서버가 확정한 종료 결과. 현재는 표시 분기가 없어 로그로만 남긴다.</param>
    public void Show(GameResultReason reason)
    {
        Log.D($"[UIResult] Show {reason}");

        ShowTeamSlots();

        countdownRoutine = StartCoroutine(CountdownToLobby());
    }

    /// <summary>세션 로비 스냅샷의 인원을 슬롯에 배분한다. 남는 슬롯은 숨긴다.</summary>
    private void ShowTeamSlots()
    {
        IReadOnlyList<Player> players = SessionCoordinator.Instance.CurrentLobby.Players;

        for (int i = 0; i < userSlots.Length; i++)
        {
            if (i >= players.Count)
            {
                userSlots[i].SetEmptySlot();
                continue;
            }

            Player player = players[i];
            userSlots[i].SetPlayerInfo(
                TryGetPlayerData(player, LobbyDataKeys.PlayerName, out string playerName) ? playerName : "Unknown",
                TryGetPlayerData(player, LobbyDataKeys.SteamId, out string steamId) ? steamId : string.Empty,
                DefaultMemoKey);
        }
    }

    /// <summary>남은 초를 1초 간격으로 동적 prefix 에 주입한다. 0 이 되면 숫자를 지운다(씬 전환은 서버가 한다).</summary>
    /// <returns>카운트다운 열거자.</returns>
    private IEnumerator CountdownToLobby()
    {
        for (int remain = Mathf.CeilToInt(lobbyReturnSeconds); remain > 0; remain--)
        {
            SetCountdownPrefix(remain.ToString());
            yield return oneSecond;
        }

        SetCountdownPrefix(string.Empty);
        countdownRoutine = null;
    }

    /// <summary>동적 prefix 버퍼를 갈아끼우고 합성을 갱신한다.</summary>
    /// <param name="text">표시할 남은 초 문자열. 빈 문자열이면 prefix 미출력.</param>
    private void SetCountdownPrefix(string text)
    {
        countdownBuilder.Length = 0;
        countdownBuilder.Append(text);
        lobbyReturnText.SetDynamicPrefix(countdownBuilder);
    }

    /// <summary>플레이어 데이터에서 키 하나를 읽는다.</summary>
    /// <param name="player">대상 플레이어.</param>
    /// <param name="key">데이터 키.</param>
    /// <param name="value">읽은 값. 없으면 빈 문자열.</param>
    /// <returns>읽기 성공 여부.</returns>
    private static bool TryGetPlayerData(Player player, string key, out string value)
    {
        value = string.Empty;
        if (player?.Data == null || !player.Data.TryGetValue(key, out PlayerDataObject data)) return false;

        value = data.Value;
        return true;
    }
}
