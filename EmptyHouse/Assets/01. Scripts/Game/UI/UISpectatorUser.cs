using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 관전 패널의 비활성(사망·탈출) 목록 행 뷰. 스팀 아바타·닉네임·상태 라벨 한 줄을 그린다.
/// 상태 라벨은 상태별 전용 오브젝트 둘 중 하나만 켜는 방식이다 — 로컬라이즈 키·색·스타일이 프리팹에 실려 코드는 문구를 모른다.
/// 아바타 텍스처는 행이 스스로 로드해 자기 수명 안에서 파괴한다 — 생성은 <see cref="SteamAvatarUtility"/>, 파괴는 이쪽 책임이다.
/// 누가 목록에 오르는지 고르는 일은 <see cref="UISpectator"/> 몫이라 여기서는 알지 못한다.
/// </summary>
public class UISpectatorUser : MonoBehaviour
{
    [Header("User UI Elements")]
    [SerializeField] private TMP_Text nicknameText;   // 닉네임
    [SerializeField] private RawImage avatarImage;    // 아바타를 그릴 RawImage
    [SerializeField] private GameObject deadLabel;    // 사망 상태 라벨 오브젝트. 키·색이 프리팹에 실려 있다
    [SerializeField] private GameObject escapedLabel; // 탈출 상태 라벨 오브젝트. 위와 동일

    private Texture2D runtimeAvatarTexture; // 이 행이 생성해 소유하는 아바타 텍스처

    /// <summary>런타임 아바타 텍스처를 정리한다.</summary>
    private void OnDestroy()
    {
        SetAvatarTexture(null);
    }

    /// <summary>행 정보를 그린다. 상태 라벨은 사망/탈출 전용 오브젝트 중 해당 쪽만 켠다.</summary>
    /// <param name="playerName">닉네임.</param>
    /// <param name="steamId">아바타 조회용 스팀 ID. 없으면 빈 문자열.</param>
    /// <param name="isDead">사망 여부. false 면 탈출로 표시한다.</param>
    public void SetPlayerInfo(string playerName, string steamId, bool isDead)
    {
        nicknameText.text = playerName;
        deadLabel.SetActive(isDead);
        escapedLabel.SetActive(!isDead);
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
