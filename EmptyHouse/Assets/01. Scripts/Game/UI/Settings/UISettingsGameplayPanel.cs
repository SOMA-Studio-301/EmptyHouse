using System.Collections.Generic;
using UnityEngine;
using Border.Events;
using Border.Localization;
using Border.Settings;

/// <summary>
/// 설정 창 GAMEPLAY 탭. 언어 선택 드롭다운을 담당한다.
/// 선택 즉시 프로필에 쓰고 언어 채널로 방송한다 — LocalizationManager 가 이를 듣고 화면의 모든 UILocalizeText 를 갱신한다.
/// 디스크 저장은 창이 닫힐 때 UISettings 가 한다.
/// 드롭다운 표기는 각 언어의 자기 이름(한국어/English/日本語…)이라 언어를 바꿔도 다시 만들 필요가 없다.
/// </summary>
public class UISettingsGameplayPanel : MonoBehaviour
{
    /// <summary>지원 언어 코드. 표기 키(LanguageNameKeys)와 순서가 일치해야 한다.</summary>
    private static readonly string[] LanguageCodes = { "ko", "en", "jp", "cn", "tw" };

    /// <summary>각 언어의 자기 이름을 담은 로컬라이즈 키. LanguageCodes 와 순서가 일치해야 한다.</summary>
    private static readonly string[] LanguageNameKeys =
    {
        "UI_OPT_LANG_KO", "UI_OPT_LANG_EN", "UI_OPT_LANG_JP", "UI_OPT_LANG_CN", "UI_OPT_LANG_TW",
    };

    [Header("Save")]
    [SerializeField] private SaveLoadSystem saveLoadSystem;

    [Header("Dropdown")]
    [SerializeField] private UISettingsDropdown languageDropdown;

    [Header("Broadcasting on")]
    [SerializeField] private StringEventChannelSO changeLanguageEvent;

    /// <summary>드롭다운 옵션을 만들고 현재 언어를 표시한 뒤 변경 구독을 건다.</summary>
    private void OnEnable()
    {
        BuildOptions();

        languageDropdown.SetValue(GetLanguageIndex(saveLoadSystem.Profile.LanguageCode), false);
        languageDropdown.RefreshShownValue();

        languageDropdown.ValueChanged += SetLanguage;
    }

    /// <summary>드롭다운 변경 구독을 해제한다.</summary>
    private void OnDisable()
    {
        languageDropdown.ValueChanged -= SetLanguage;
    }

    /// <summary>로컬라이즈 테이블에서 각 언어의 자기 이름을 읽어 드롭다운 옵션을 채운다.</summary>
    private void BuildOptions()
    {
        ILocalizationProvider localization = LocalizationManager.Current;

        List<string> options = new List<string>(LanguageNameKeys.Length);
        for (int i = 0; i < LanguageNameKeys.Length; i++)
        {
            options.Add(localization.Get(LanguageNameKeys[i]));
        }

        languageDropdown.ClearOptions();
        languageDropdown.AddOptions(options);
    }

    /// <summary>선택한 언어를 프로필에 쓰고 방송한다. LocalizationManager 가 받아 즉시 적용한다.</summary>
    /// <param name="index">언어 목록 인덱스.</param>
    private void SetLanguage(int index)
    {
        string code = GetLanguageCode(index);
        saveLoadSystem.Profile.LanguageCode = code;
        changeLanguageEvent.RaiseEvent(code);
    }

    /// <summary>언어 코드에 대응하는 드롭다운 인덱스를 반환한다. 모르는 코드면 첫 번째(ko)로 떨어진다.</summary>
    /// <param name="code">언어 코드.</param>
    /// <returns>드롭다운 인덱스.</returns>
    private int GetLanguageIndex(string code)
    {
        for (int i = 0; i < LanguageCodes.Length; i++)
        {
            if (LanguageCodes[i] == code)
            {
                return i;
            }
        }

        return 0;
    }

    /// <summary>인덱스에 대응하는 언어 코드를 반환한다. 범위를 벗어나면 첫 번째(ko)로 떨어진다.</summary>
    /// <param name="index">드롭다운 인덱스.</param>
    /// <returns>언어 코드.</returns>
    private string GetLanguageCode(int index)
    {
        if (index < 0 || index >= LanguageCodes.Length)
        {
            return LanguageCodes[0];
        }

        return LanguageCodes[index];
    }
}
