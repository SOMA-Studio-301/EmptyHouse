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
    

    private Lobby _lobbyInfo;

    public void SetLobbyInfo(Lobby lobby, System.Action<Lobby> onJoinClick)
    {
        _lobbyInfo = lobby;
    
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
    
        // 1. 방이 꽉 찬 경우
        if (lobby.Players.Count >= lobby.MaxPlayers)
        {
            if (joinButton != null) joinButton.interactable = false;
            if (joinText != null) joinText.text = "Full";
        }
        // 2. 비밀번호가 걸려있는 방인 경우
        else if (hasPassword)
        {
            if (joinButton != null) joinButton.interactable = false; // ★ 버튼을 못 누르게 만듭니다.
            if (joinText != null) joinText.text = "Join";            // ★ 텍스트는 "Join" 그대로 유지합니다.
        }
        // 3. 아무 제약 없는 일반 방인 경우
        else
        {
            if (joinButton != null) joinButton.interactable = true;
            if (joinText != null) joinText.text = "Join";
        }
        
    }
}