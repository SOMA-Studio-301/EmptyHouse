using Border.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 결과창 팀원 슬롯 뷰. 아바타·닉네임·메모 한 줄을 그린다.
/// 스팀 아바타는 슬롯이 스스로 로드해 그리고 자기 수명 안에서 파괴한다 — 생성은 <see cref="SteamAvatarUtility"/>, 파괴는 이쪽 책임이다.
/// UGS·세션 파싱은 <see cref="UIResult"/> 몫이라 여기서는 알지 못한다.
/// </summary>
public class UIResultUser : MonoBehaviour
{
    [Header("User UI Elements")]
    [SerializeField] private TMP_Text nicknameText;   // 닉네임
    [SerializeField] private RawImage avatarImage;    // 아바타를 그릴 RawImage
    [SerializeField] private UILocalizeText memoText; // 기여도 메모 라벨

    private Texture2D runtimeAvatarTexture; // 이 슬롯이 생성해 소유하는 아바타 텍스처

    /// <summary>런타임 아바타 텍스처를 정리한다.</summary>
    private void OnDestroy()
    {
        SetAvatarTexture(null);
    }

    /// <summary>팀원 정보를 슬롯에 그린다.</summary>
    /// <param name="playerName">닉네임.</param>
    /// <param name="steamId">아바타 조회용 스팀 ID. 없으면 빈 문자열.</param>
    /// <param name="memoKey">기여도 메모 로컬라이즈 키.</param>
    public void SetPlayerInfo(string playerName, string steamId, string memoKey)
    {
        gameObject.SetActive(true);

        nicknameText.text = playerName;
        memoText.SetKey(memoKey);
        SetAvatarTexture(SteamAvatarUtility.Load(steamId));
    }

    /// <summary>빈 자리로 만든다. 결과창에는 초대 개념이 없어 슬롯 자체를 숨긴다.</summary>
    public void SetEmptySlot()
    {
        SetAvatarTexture(null);
        gameObject.SetActive(false);
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
