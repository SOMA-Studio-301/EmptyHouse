using System;
using UnityEngine;
using UnityEngine.UI;
using Unity.Services.Lobbies.Models;

/// <summary>
/// 로비 목록 셀 뷰. 방 이름·인원·비밀번호 아이콘을 그리고 Join 클릭 의도만 이벤트로 올린다.
/// 입장 가능 여부 판정(만석)은 셀이 표시 수준에서만 하고, 실제 입장 로직은 상위 몫이다.
/// </summary>
public class LobbyListCell : MonoBehaviour
{
    /// <summary>발행: Join 버튼 클릭. 표시 중인 로비를 싣는다.</summary>
    public event Action<Lobby> JoinClicked;

    [SerializeField] private Text lobbyNameText;         // 방 이름 라벨
    [SerializeField] private Text playerCountText;       // 인원 수 라벨
    [SerializeField] private GameObject passwordIcon;    // 비밀번호 방 자물쇠 아이콘
    [SerializeField] private UIGenericButton joinButton; // 입장 버튼
    [SerializeField] private Text joinText;              // 입장 버튼 라벨(Join/Full) — TODO: TMP 전환 시 로컬라이즈 키로 대체

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

        lobbyNameText.text = lobby.Name;
        playerCountText.text = $"{lobby.Players.Count}/{lobby.MaxPlayers}";
        passwordIcon.SetActive(LobbyDataKeys.HasPassword(lobby));

        bool isFull = lobby.Players.Count >= lobby.MaxPlayers;
        joinButton.Interactable = !isFull;
        joinText.text = isFull ? "Full" : "Join";
    }

    /// <summary>Join 의도를 올린다.</summary>
    private void RaiseJoinClicked()
    {
        JoinClicked?.Invoke(lobby);
    }
}
