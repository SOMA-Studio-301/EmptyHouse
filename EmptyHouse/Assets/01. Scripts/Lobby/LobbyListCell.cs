using System;
using Border.Localization;
using TMPro;
using UnityEngine;
using Unity.Services.Lobbies.Models;

/// <summary>
/// 로비 목록 셀 뷰. 방 이름·인원·비밀번호 아이콘을 그리고 Join 클릭 의도만 이벤트로 올린다.
/// 입장 가능 여부 판정(만석)은 셀이 표시 수준에서만 하고, 실제 입장 로직은 상위 몫이다.
/// </summary>
public class LobbyListCell : MonoBehaviour
{
    /// <summary>발행: Join 버튼 클릭. 표시 중인 로비를 싣는다.</summary>
    public event Action<Lobby> JoinClicked;

    [SerializeField] private UILocalizeText lobbyNameText; // 방 이름 라벨. 키 미등록 문자열은 원문 그대로 출력된다
    [SerializeField] private TMP_Text playerCountText;     // 인원 수 라벨
    [SerializeField] private GameObject passwordIcon;      // 비밀번호 방 자물쇠 표시
    [SerializeField] private UIGenericButton joinButton;   // 입장 버튼. 라벨은 SetButton 으로 갱신

    private Lobby lobby; // 표시 중인 로비

    /// <summary>버튼 리스너를 등록한다.</summary>
    private void Awake()
    {
        joinButton.Clicked += RaiseJoinClicked;
    }

    /// <summary>리스너를 해제한다.</summary>
    private void OnDestroy()
    {
        joinButton.Clicked -= RaiseJoinClicked;
    }

    /// <summary>로비 정보를 셀에 그린다. 만석이면 Join 버튼을 잠근다.</summary>
    /// <param name="lobby">표시할 로비</param>
    public void SetLobbyInfo(Lobby lobby)
    {
        this.lobby = lobby;

        lobbyNameText.SetKey(lobby.Name); // 방 이름은 동적 문자열 — 키 미스 폴백으로 원문이 표시된다
        playerCountText.text = $"{lobby.Players.Count}/{lobby.MaxPlayers}";
        passwordIcon.SetActive(LobbyDataKeys.HasPassword(lobby));

        bool isFull = lobby.Players.Count >= lobby.MaxPlayers;
        joinButton.Interactable = !isFull;
        joinButton.SetButton(isFull ? "Full" : "Join"); // TODO: 로컬라이즈 테이블에 키 등록 시 실제 키로 교체
    }

    /// <summary>Join 의도를 올린다.</summary>
    private void RaiseJoinClicked()
    {
        JoinClicked?.Invoke(lobby);
    }
}
