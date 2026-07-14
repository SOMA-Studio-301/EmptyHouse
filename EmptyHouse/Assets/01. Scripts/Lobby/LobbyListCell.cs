using System;
using UnityEngine;
using UnityEngine.UI;
using Unity.Services.Lobbies.Models;

public class LobbyListCell : MonoBehaviour
{
    [SerializeField] private Text lobbyNameText;
    [SerializeField] private Text playerCountText;
    [SerializeField] private GameObject passwordIcon;
    [SerializeField] private Button joinButton;
    [SerializeField] private Text joinText;
    public void SetLobbyInfo(Lobby lobby, Action<Lobby> onJoinClick)
    {
        if (lobby == null) return;

        if (lobbyNameText != null) lobbyNameText.text = lobby.Name;
        if (playerCountText != null) playerCountText.text = $"{lobby.Players.Count}/{lobby.MaxPlayers}";

        if (joinButton != null)
        {
            joinButton.onClick.RemoveAllListeners();
            joinButton.onClick.AddListener(() => onJoinClick?.Invoke(lobby));
        }

        // UGS 로비 커스텀 비밀번호 데이터 체크
        bool hasPassword = lobby.Data != null && lobby.Data.ContainsKey("Password");

        // 패스워드 아이콘(자물쇠 등)은 방 상태에 따라 온오프
        if (passwordIcon != null)
        {
            passwordIcon.SetActive(hasPassword);
        }
    
        // 방이 꽉 찬 경우
        if (lobby.Players.Count >= lobby.MaxPlayers)
        {
            if (joinButton != null) joinButton.interactable = false;
            if (joinText != null) joinText.text = "Full";
        }
        else
        {
            if (joinButton != null) joinButton.interactable = true;
            if (joinText != null) joinText.text = "Join";
        }
    }
}
