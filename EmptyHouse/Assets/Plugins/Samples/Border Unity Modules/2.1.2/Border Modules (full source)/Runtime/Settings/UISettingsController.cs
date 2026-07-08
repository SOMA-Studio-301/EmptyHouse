using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using Border.Core;
using Border.Events;
using Border.UI;
using Border.Localization;

namespace Border.Settings
{
    /// <summary>
    /// 설정 패널 UI 입력을 SettingsSO와 이벤트 채널에 반영하는 컨트롤러이다.
    /// </summary>
    public class UISettingsController : MonoBehaviour
    {
        [Header("UI Components")]
        [SerializeField] private UIGenericButton backButton;
        [SerializeField] private UIGenericButton resetButton;

        [Header("Settings")]
        [SerializeField] private SettingsSO currentSettings;
        [SerializeField] private UISettingsAudioComponent audioComponent;
        [SerializeField] private UISettingsGraphicsComponent graphicsComponent;
        //[SerializeField] private UISettingsCheck enableHudButton;
        [SerializeField] private UISettingsCheck cameraShakeButton;
        [SerializeField] private UISettingsDropdown languageDropdown;

        [Header("Broadcasting on")]
        [SerializeField] private VoidEventChannelSO saveSettingsEvent;
        //[SerializeField] private BoolEventChannelSO changeHudOnEvent;
        [SerializeField] private BoolEventChannelSO changeCameraShakeEvent;
        [SerializeField] private StringEventChannelSO changeLanguageEvent;

        private static readonly string[] LanguageCodes = { "en", "ko", "ja", "zh" };
        private static readonly string[] LanguageDisplayNames = { "English", "한국어", "日本語", "中文" };

        /// <summary>
        /// 게임패드 네비게이션 시 첫 번째로 선택될 UI 요소를 반환한다.
        /// </summary>
        public GameObject FirstSelected => audioComponent.MasterVolumeSlider.Slider.gameObject;

        public event UnityAction CloseButtonAction;

        /// <summary>
        /// 설정 패널 이벤트를 구독하고 현재 설정값을 UI에 표시한다.
        /// </summary>
        private void OnEnable()
        {
            backButton.Clicked += CloseSettingPanel;
            resetButton.Clicked += ResetSettings;

            //if (enableHudButton != null)
            //{
            //    enableHudButton.OnValueChanged += HandleHudToggleChanged;
            //}

            if (cameraShakeButton != null)
            {
                cameraShakeButton.OnValueChanged += HandleCameraShakeToggleChanged;
            }

            if (languageDropdown != null)
            {
                languageDropdown.ValueChanged += HandleLanguageChanged;
            }

            ShowSettingPanel();
        }

        /// <summary>
        /// 설정 패널 이벤트 구독을 해제하고 닫힐 때 설정 저장을 수행한다.
        /// </summary>
        private void OnDisable()
        {
            backButton.Clicked -= CloseSettingPanel;
            resetButton.Clicked -= ResetSettings;

            //if (enableHudButton != null)
            //{
            //    enableHudButton.OnValueChanged -= HandleHudToggleChanged;
            //}

            if (cameraShakeButton != null)
            {
                cameraShakeButton.OnValueChanged -= HandleCameraShakeToggleChanged;
            }

            if (languageDropdown != null)
            {
                languageDropdown.ValueChanged -= HandleLanguageChanged;
            }

            // 창이 닫힐 때 자동 저장
            SaveSettings();
        }

        /// <summary>
        /// 현재 SettingsSO 값을 설정 패널 UI에 반영한다.
        /// </summary>
        private void ShowSettingPanel()
        {
            audioComponent.Setup(currentSettings.MasterVolume, currentSettings.MusicVolume, currentSettings.SfxVolume);
            graphicsComponent.Setup(currentSettings.ResolutionIndex, currentSettings.WindowModeIndex);

            //if (enableHudButton != null)
            //{
            //    enableHudButton.SetValue(currentSettings.IsHudOn);
            //}

            if (cameraShakeButton != null)
            {
                cameraShakeButton.SetValue(currentSettings.IsCameraShakeOn);
            }

            SetupLanguageDropdown();
        }

        /// <summary>
        /// 설정 패널 닫기 이벤트를 외부로 전달한다.
        /// </summary>
        private void CloseSettingPanel()
        {
            CloseButtonAction?.Invoke();
        }

        /// <summary>
        /// 현재 UI 입력값을 SettingsSO에 저장하고 저장 이벤트를 발행한다.
        /// </summary>
        private void SaveSettings()
        {
            audioComponent.SaveVolumes(currentSettings);
            graphicsComponent.SaveGraphics(currentSettings);

            //if (enableHudButton != null)
            //{
            //    currentSettings.SaveEnableHud(enableHudButton.IsOn);
            //}

            if (cameraShakeButton != null)
            {
                currentSettings.SaveEnableCameraShake(cameraShakeButton.IsOn);
            }

            saveSettingsEvent?.RaiseEvent();
        }

        /// <summary>
        /// 설정값을 기본값으로 되돌리고 즉시 저장 이벤트를 발행한다.
        /// </summary>
        private void ResetSettings()
        {
            audioComponent.ResetVolumes(currentSettings);
            graphicsComponent.ResetGraphics(currentSettings);

            //if (enableHudButton != null)
            //{
            //    enableHudButton.SetValue(true, true);
            //    currentSettings.SaveEnableHud(enableHudButton.IsOn);
            //}

            if (cameraShakeButton != null)
            {
                cameraShakeButton.SetValue(true, true);
                currentSettings.SaveEnableCameraShake(cameraShakeButton.IsOn);
            }

            currentSettings.SaveLanguageCode(LanguageCodes[0]);
            ApplyLanguageDropdownValue(0);
            changeLanguageEvent?.RaiseEvent(currentSettings.LanguageCode);

            saveSettingsEvent?.RaiseEvent();
        }

        ///// <summary>
        ///// 허드 표시 토글 변경을 처리하고 이벤트로 전달한다.
        ///// </summary>
        ///// <param name="isOn">HUD 표시 여부</param>
        //private void HandleHudToggleChanged(bool isOn)
        //{
        //    currentSettings.SaveEnableHud(isOn);
        //    changeHudOnEvent?.RaiseEvent(isOn);
        //}

        /// <summary>
        /// 카메라 쉐이크 토글 변경을 처리하고 이벤트로 전달한다.
        /// </summary>
        /// <param name="isOn">카메라 쉐이크 사용 여부</param>
        private void HandleCameraShakeToggleChanged(bool isOn)
        {
            currentSettings.SaveEnableCameraShake(isOn);
            changeCameraShakeEvent?.RaiseEvent(isOn);
        }

        /// <summary>
        /// 언어 드롭다운 옵션을 구성하고 현재 언어 코드에 맞는 값을 표시한다.
        /// </summary>
        private void SetupLanguageDropdown()
        {
            if (languageDropdown == null)
            {
                return;
            }

            languageDropdown.ClearOptions();
            languageDropdown.AddOptions(new List<string>(LanguageDisplayNames));

            int selectedIndex = GetLanguageIndex(currentSettings.LanguageCode);
            ApplyLanguageDropdownValue(selectedIndex);
        }

        /// <summary>
        /// 언어 드롭다운 선택 변경을 처리하고 설정값 및 이벤트를 갱신한다.
        /// </summary>
        private void HandleLanguageChanged(int index)
        {
            string selectedCode = GetLanguageCode(index);
            currentSettings.SaveLanguageCode(selectedCode);
            changeLanguageEvent?.RaiseEvent(currentSettings.LanguageCode);
        }

        /// <summary>
        /// 언어 코드에 대응하는 드롭다운 인덱스를 반환한다.
        /// </summary>
        private int GetLanguageIndex(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return 0;
            }

            string normalized = code.Trim().ToLowerInvariant();
            for (int i = 0; i < LanguageCodes.Length; i++)
            {
                if (LanguageCodes[i] == normalized)
                {
                    return i;
                }
            }

            return 0;
        }

        /// <summary>
        /// 인덱스에 대응하는 언어 코드를 반환한다.
        /// </summary>
        /// <param name="index">언어 인덱스</param>
        /// <returns>언어 코드</returns>
        private string GetLanguageCode(int index)
        {
            if (index < 0 || index >= LanguageCodes.Length)
            {
                return LanguageCodes[0];
            }

            return LanguageCodes[index];
        }

        /// <summary>
        /// 언어 드롭다운 표시값을 갱신한다.
        /// </summary>
        /// <param name="index">표시할 언어 인덱스</param>
        private void ApplyLanguageDropdownValue(int index)
        {
            if (languageDropdown == null)
            {
                return;
            }

            languageDropdown.SetValue(index, false);
            languageDropdown.RefreshShownValue();
        }
    }

}
