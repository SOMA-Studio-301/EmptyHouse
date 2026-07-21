using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 관전 패널의 사망자 목록 행 뷰. 스팀 아바타와 닉네임 한 줄을 그린다.
/// 아바타 텍스처는 행이 스스로 로드해 자기 수명 안에서 파괴한다 — 생성은 <see cref="SteamAvatarUtility"/>, 파괴는 이쪽 책임이다.
/// 누가 사망자인지 고르는 일은 <see cref="UISpectator"/> 몫이라 여기서는 알지 못한다.
/// </summary>
public class UISpectatorUser : MonoBehaviour
{
    [Header("User UI Elements")]
    [SerializeField] private TMP_Text nicknameText; // 닉네임
    [SerializeField] private RawImage avatarImage;  // 아바타를 그릴 RawImage

    private Texture2D runtimeAvatarTexture; // 이 행이 생성해 소유하는 아바타 텍스처

    /// <summary>런타임 아바타 텍스처를 정리한다.</summary>
    private void OnDestroy()
    {
        SetAvatarTexture(null);
    }

    /// <summary>사망자 정보를 행에 그린다.</summary>
    /// <param name="playerName">닉네임.</param>
    /// <param name="steamId">아바타 조회용 스팀 ID. 없으면 빈 문자열.</param>
    public void SetPlayerInfo(string playerName, string steamId)
    {
        nicknameText.text = playerName;
        SetAvatarTexture(SteamAvatarUtility.Load(steamId));
    }

    /// <summary>아바타 텍스처를 교체하고 이전 런타임 텍스처를 파괴한다.</summary>
    /// <param name="texture">적용할 텍스처. null이면 비움.</param>
    private void SetAvatarTexture(Texture2D texture)
    {
        if (runtimeAvatarTexture != null && runtimeAvatarTexture != texture)
        {
            Destroy(runtimeAvatarTexture);
        }

        runtimeAvatarTexture = texture;
        avatarImage.texture = texture;
    }
}
