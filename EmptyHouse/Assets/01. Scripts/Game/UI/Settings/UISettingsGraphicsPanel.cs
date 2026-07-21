using System.Collections.Generic;
using UnityEngine;
using Border.Events;
using Border.Localization;
using Border.Settings;

/// <summary>
/// 설정 창 GRAPHICS 탭. 해상도와 창 모드 드롭다운을 담당한다.
/// 값이 바뀌면 즉시 프로필에 쓰고 화면에 적용한다 — 디스크 저장은 창이 닫힐 때 UISettings 가 요청한다.
/// 해상도 목록과 창 모드 인덱스 규약은 Border 의 SettingsGraphicsUtility 를 그대로 따른다.
/// </summary>
public class UISettingsGraphicsPanel : MonoBehaviour
{
    [Header("Save")]
    [SerializeField] private SaveLoadSystem saveLoadSystem;

    [Header("Dropdowns")]
    [SerializeField] private UISettingsDropdown resolutionDropdown;
    [SerializeField] private UISettingsDropdown windowModeDropdown;

    [Header("Localization")]
    [LocalizeKey][SerializeField] private string fullscreenKey = "UI_OPT_WINMODE_FULLSCREEN";  // 창 모드 0 — 전체화면
    [LocalizeKey][SerializeField] private string windowedKey = "UI_OPT_WINMODE_WINDOWED";      // 창 모드 1 — 창모드
    [LocalizeKey][SerializeField] private string borderlessKey = "UI_OPT_WINMODE_BORDERLESS";  // 창 모드 2 — 테두리없는 창

    [Header("Broadcasting on")]
    [SerializeField] private IntEventChannelSO changeResolutionEvent;

    private List<Resolution> resolutions;

    /// <summary>드롭다운 옵션을 만들고 현재 프로필 값을 표시한 뒤 변경 구독을 건다.</summary>
    private void OnEnable()
    {
        ProfileSave profile = saveLoadSystem.Profile;
        resolutions = SettingsGraphicsUtility.GetResolutionsList();

        BuildResolutionOptions();
        BuildWindowModeOptions();

        resolutionDropdown.SetValue(SettingsGraphicsUtility.GetValidatedResolutionIndex(resolutions, profile.ResolutionIndex), false);
        resolutionDropdown.RefreshShownValue();
        windowModeDropdown.SetValue(SettingsGraphicsUtility.GetValidatedWindowModeIndex(profile.WindowModeIndex), false);
        windowModeDropdown.RefreshShownValue();

        resolutionDropdown.ValueChanged += SetResolution;
        windowModeDropdown.ValueChanged += SetWindowMode;
    }

    /// <summary>드롭다운 변경 구독을 해제한다.</summary>
    private void OnDisable()
    {
        resolutionDropdown.ValueChanged -= SetResolution;
        windowModeDropdown.ValueChanged -= SetWindowMode;
    }

    /// <summary>사용 가능한 해상도 목록으로 드롭다운 옵션을 채운다.</summary>
    private void BuildResolutionOptions()
    {
        List<string> options = new List<string>(resolutions.Count);
        for (int i = 0; i < resolutions.Count; i++)
        {
            options.Add($"{resolutions[i].width} x {resolutions[i].height} " +
                        $"@{Mathf.FloorToInt((float)resolutions[i].refreshRateRatio.value)}Hz");
        }

        resolutionDropdown.ClearOptions();
        resolutionDropdown.AddOptions(options);
    }

    /// <summary>로컬라이즈 테이블에서 창 모드 표기를 읽어 드롭다운 옵션을 채운다. 추가 순서가 곧 SettingsGraphicsUtility 의 창 모드 인덱스(0/1/2)다.</summary>
    private void BuildWindowModeOptions()
    {
        ILocalizationProvider localization = LocalizationManager.Current;

        List<string> options = new List<string>(3)
        {
            localization.Get(fullscreenKey),
            localization.Get(windowedKey),
            localization.Get(borderlessKey),
        };

        windowModeDropdown.ClearOptions();
        windowModeDropdown.AddOptions(options);
    }

    /// <summary>해상도를 프로필에 쓰고 화면에 적용한 뒤 방송한다.</summary>
    /// <param name="index">해상도 목록 인덱스.</param>
    private void SetResolution(int index)
    {
        ProfileSave profile = saveLoadSystem.Profile;
        profile.ResolutionIndex = SettingsGraphicsUtility.GetValidatedResolutionIndex(resolutions, index);

        SettingsGraphicsUtility.ApplyGraphicsSettings(profile.ResolutionIndex, profile.WindowModeIndex);
        changeResolutionEvent.RaiseEvent(profile.ResolutionIndex);
    }

    /// <summary>창 모드를 프로필에 쓰고 화면에 적용한다.</summary>
    /// <param name="index">창 모드 인덱스.</param>
    private void SetWindowMode(int index)
    {
        ProfileSave profile = saveLoadSystem.Profile;
        profile.WindowModeIndex = SettingsGraphicsUtility.GetValidatedWindowModeIndex(index);

        SettingsGraphicsUtility.ApplyGraphicsSettings(profile.ResolutionIndex, profile.WindowModeIndex);
    }
}
