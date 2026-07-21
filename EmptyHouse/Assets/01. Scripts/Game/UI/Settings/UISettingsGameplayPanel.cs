using System.Collections.Generic;
using UnityEngine;
using Border.Events;
using Border.Localization;
using Border.Settings;

/// <summary>
/// 설정 창 GAMEPLAY 탭. 언어 선택 드롭다운과 세이브 데이터 삭제를 담당한다.
/// 언어는 선택 즉시 프로필에 쓰고 언어 채널로 방송한다 — LocalizationManager 가 이를 듣고 화면의 모든 UILocalizeText 를 갱신한다.
/// 디스크 저장은 창이 닫힐 때 UISettings 가 한다.
/// 드롭다운 표기는 각 언어의 자기 이름(한국어/English/日本語…)이라 언어를 바꿔도 다시 만들 필요가 없다.
/// 데이터 삭제는 되돌릴 수 없으므로 공용 팝업에 확인을 한 단계 거친다 — 버튼 한 번으로는 절대 지워지지 않는다.
/// 팝업의 생김새는 UIPopup 이 PopupType 으로 정하고, 이쪽은 "무엇을 할지"만 콜백으로 실어 보낸다.
/// 지우는 것은 RunSave(런 진행)뿐이고 ProfileSave(설정)는 보존된다. 두 파일을 나눈 이유가 바로 이것이다.
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

    [Header("Buttons")]
    [SerializeField] private UIGenericButton deleteDataButton;

    [Header("Broadcasting on")]
    [SerializeField] private StringEventChannelSO changeLanguageEvent;
    [SerializeField] private PopupEventChannelSO popupRequested;

    /// <summary>드롭다운 옵션을 만들고 현재 언어를 표시한 뒤 구독을 건다.</summary>
    private void OnEnable()
    {
        BuildOptions();

        languageDropdown.SetValue(GetLanguageIndex(saveLoadSystem.Profile.LanguageCode), false);
        languageDropdown.RefreshShownValue();

        languageDropdown.ValueChanged += SetLanguage;
        deleteDataButton.Clicked += RequestDeleteConfirm;
    }

    /// <summary>드롭다운·버튼 구독을 해제한다.</summary>
    private void OnDisable()
    {
        languageDropdown.ValueChanged -= SetLanguage;
        deleteDataButton.Clicked -= RequestDeleteConfirm;
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

    /// <summary>삭제 버튼. 바로 지우지 않고 공용 팝업에 확인을 요청한다. 확인을 누르면 팝업이 넘겨준 콜백을 부른다.</summary>
    private void RequestDeleteConfirm()
    {
        popupRequested.RaiseEvent(PopupType.DeleteRunData, ConfirmDelete);
    }

    /// <summary>런 데이터를 삭제한다. 설정(ProfileSave)은 건드리지 않는다.</summary>
    private void ConfirmDelete()
    {
        saveLoadSystem.DeleteRun();
    }
}
