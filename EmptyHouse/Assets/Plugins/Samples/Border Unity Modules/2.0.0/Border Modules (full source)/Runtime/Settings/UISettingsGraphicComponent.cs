using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Border.Core;
using Border.Events;
using Border.UI;
using Border.Localization;

namespace Border.Settings
{
    public class UISettingsGraphicsComponent : MonoBehaviour
    {
        [Header("UI Components")]
        [SerializeField] private UISettingsDropdown resolutionDropdown;
        [SerializeField] private UISettingsDropdown windowModeDropdown;

        [Header("Window Mode Localizing Key")]
        [SerializeField] private List<string> windowModeLocalizingKeys;

        [Header("Listening to")]
        [SerializeField] private IntEventChannelSO changeResolutionEvent;

        private List<Resolution> resolutionList;
        private Resolution currentResolution;
        private int currentResolutionIndex;
        private int windowModeIndex;

        /// <summary>
        /// 드롭다운과 해상도 변경 이벤트를 구독한다.
        /// </summary>
        private void OnEnable()
        {
            resolutionDropdown.ValueChanged += OnResolutionDropdownChanged;
            windowModeDropdown.ValueChanged += OnWindowModeDropdownChanged;
            changeResolutionEvent.OnEventRaised += OnResolutionDropdownChanged;

            ILocalizationProvider localizationManager = LocalizationManager.Current;
            if (localizationManager != null)
            {
                localizationManager.OnLanguageChanged += RefreshLocalizedDropdowns;
            }
        }

        /// <summary>
        /// 드롭다운과 해상도 변경 이벤트 구독을 해제한다.
        /// </summary>
        private void OnDisable()
        {
            resolutionDropdown.ValueChanged -= OnResolutionDropdownChanged;
            windowModeDropdown.ValueChanged -= OnWindowModeDropdownChanged;
            changeResolutionEvent.OnEventRaised -= OnResolutionDropdownChanged;

            ILocalizationProvider localizationManager = LocalizationManager.Current;
            if (localizationManager != null)
            {
                localizationManager.OnLanguageChanged -= RefreshLocalizedDropdowns;
            }
        }

        /// <summary>
        /// 현재 그래픽 설정 상태를 기준으로 드롭다운들을 초기화한다.
        /// </summary>
        private void Init()
        {
            resolutionList = GetResolutionsList();
            currentResolutionIndex = SettingsGraphicsUtility.GetValidatedResolutionIndex(resolutionList, currentResolutionIndex);
            windowModeIndex = GetValidatedWindowModeIndex(windowModeIndex);
            currentResolution = resolutionList[currentResolutionIndex];

            InitializeResolutionDropdown();
            InitializeWindowModeDropdown();
        }

        /// <summary>
        /// 저장된 그래픽 설정 값을 UI와 내부 상태에 반영한다.
        /// </summary>
        public void Setup(int currentResolutionIndex, int windowModeIndex)
        {
            this.currentResolutionIndex = currentResolutionIndex;
            this.windowModeIndex = windowModeIndex;

            Init();
        }

        #region RESOLUTION

        /// <summary>
        /// 사용 가능한 해상도 목록을 조건에 맞게 필터링하여 반환한다.
        /// </summary>
        /// <returns>설정 가능한 해상도 목록</returns>
        private List<Resolution> GetResolutionsList()
        {
            return SettingsGraphicsUtility.GetResolutionsList();
        }

        /// <summary>
        /// 해상도 드롭다운 옵션과 현재 선택값을 초기화한다.
        /// </summary>
        private void InitializeResolutionDropdown()
        {
            if (resolutionDropdown == null)
            {
                return;
            }

            resolutionDropdown.ClearOptions();
            List<string> options = new List<string>(resolutionList.Count);

            for (int i = 0; i < resolutionList.Count; ++i)
            {
                options.Add($"{resolutionList[i].width} x {resolutionList[i].height} " +
                            $"@{Mathf.FloorToInt((float)resolutionList[i].refreshRateRatio.value)}Hz");
            }

            resolutionDropdown.AddOptions(options);
            resolutionDropdown.SetValue(currentResolutionIndex, false);
            resolutionDropdown.RefreshShownValue();
        }

        /// <summary>
        /// 해상도 드롭다운 변경 시 선택된 해상도를 적용한다.
        /// </summary>
        /// <param name="resolutionIndex">선택된 해상도 인덱스</param>
        private void OnResolutionDropdownChanged(int resolutionIndex)
        {
            if (resolutionList == null || resolutionList.Count == 0)
            {
                resolutionList = GetResolutionsList();
            }

            int validatedResolutionIndex = SettingsGraphicsUtility.GetValidatedResolutionIndex(resolutionList, resolutionIndex);
            if (currentResolutionIndex == validatedResolutionIndex)
            {
                return;
            }

            currentResolutionIndex = validatedResolutionIndex;
            OnResolutionChanged();
        }

        /// <summary>
        /// 현재 선택된 해상도와 창 모드를 화면 설정에 반영한다.
        /// </summary>
        private void OnResolutionChanged()
        {
            if (resolutionList == null || resolutionList.Count == 0)
            {
                resolutionList = GetResolutionsList();
            }

            currentResolutionIndex = SettingsGraphicsUtility.GetValidatedResolutionIndex(resolutionList, currentResolutionIndex);
            currentResolution = resolutionList[currentResolutionIndex];
            FullScreenMode fullScreenMode = GetFullScreenMode(windowModeIndex);
            Screen.SetResolution(currentResolution.width, currentResolution.height, fullScreenMode);
            StartCoroutine(VerifyResolutionChange(fullScreenMode));
        }

        /// <summary>
        /// 해상도 변경 적용 결과를 확인하고 실패 시 기본 해상도로 복구한다.
        /// </summary>
        private IEnumerator VerifyResolutionChange(FullScreenMode fullScreenMode)
        {
            yield return new WaitForSeconds(5.0f);

            if (Screen.width == currentResolution.width &&
                Screen.height == currentResolution.height)
            {
                yield break;
            }

            Log.W($"해상도 변경 실패 {Screen.width}x{Screen.height}" +
                  $" to {currentResolution.width}x{currentResolution.height}");

            currentResolutionIndex = 0;
            currentResolution = resolutionList[currentResolutionIndex];
            Screen.SetResolution(currentResolution.width, currentResolution.height, fullScreenMode);
            resolutionDropdown.SetValue(currentResolutionIndex, false);
        }

        #endregion

        #region WINDOW MODE

        /// <summary>
        /// 창 모드 키 순서대로 로컬라이징 문자열을 조회해 드롭다운 옵션을 초기화한다.
        /// </summary>
        private void InitializeWindowModeDropdown()
        {
            if (windowModeDropdown == null)
            {
                return;
            }

            ILocalizationProvider localizationManager = LocalizationManager.Current;
            if (localizationManager == null)
            {
                return;
            }

            RebuildWindowModeDropdownOptions(localizationManager);
            windowModeDropdown.SetValue(GetValidatedWindowModeIndex(windowModeIndex), false);
            windowModeDropdown.RefreshShownValue();
        }

        /// <summary>
        /// 현재 언어 기준으로 로컬라이즈가 필요한 드롭다운 옵션 문자열을 다시 구성한다.
        /// 현재는 창 모드 드롭다운만 재빌드한다.
        /// </summary>
        private void RefreshLocalizedDropdowns()
        {
            InitializeWindowModeDropdown();
        }

        /// <summary>
        /// 창 모드 드롭다운 옵션 문자열을 현재 언어에 맞춰 다시 생성한다.
        /// </summary>
        /// <param name="localizationManager">현재 로컬라이징 조회에 사용할 매니저</param>
        private void RebuildWindowModeDropdownOptions(ILocalizationProvider localizationManager)
        {
            if (windowModeDropdown == null || localizationManager == null)
            {
                return;
            }

            windowModeDropdown.ClearOptions();

            List<string> options = new List<string>(windowModeLocalizingKeys.Count);
            for (int i = 0; i < windowModeLocalizingKeys.Count; ++i)
            {
                string localizingKey = windowModeLocalizingKeys[i];
                options.Add(string.IsNullOrWhiteSpace(localizingKey) ? string.Empty : localizationManager.Get(localizingKey));
            }

            windowModeDropdown.AddOptions(options);
        }

        /// <summary>
        /// 창 모드 드롭다운 변경 시 선택된 모드를 화면 설정에 즉시 반영한다.
        /// </summary>
        private void OnWindowModeDropdownChanged(int selectedWindowModeIndex)
        {
            windowModeIndex = GetValidatedWindowModeIndex(selectedWindowModeIndex);

            FullScreenMode fullScreenMode = GetFullScreenMode(windowModeIndex);
            Screen.SetResolution(currentResolution.width, currentResolution.height, fullScreenMode);
        }

        /// <summary>
        /// 창 모드 인덱스를 Unity의 FullScreenMode 값으로 변환한다.
        /// </summary>
        private FullScreenMode GetFullScreenMode(int modeIndex)
        {
            return SettingsGraphicsUtility.GetFullScreenMode(modeIndex);
        }

        /// <summary>
        /// 창 모드 인덱스가 허용 범위를 벗어나면 기본값으로 보정한다.
        /// </summary>
        private int GetValidatedWindowModeIndex(int modeIndex)
        {
            return SettingsGraphicsUtility.GetValidatedWindowModeIndex(modeIndex);
        }

        #endregion

        /// <summary>
        /// 현재 그래픽 설정 상태를 SettingsSO에 저장한다.
        /// </summary>
        /// <param name="currentSettings">저장 대상 설정 데이터</param>
        public void SaveGraphics(SettingsSO currentSettings)
        {
            currentSettings.SaveGraphicsSettings(currentResolutionIndex, windowModeIndex);
        }

        /// <summary>
        /// 그래픽 설정을 기본값으로 되돌리고 UI와 저장값에 반영한다.
        /// </summary>
        /// <param name="currentSettings">저장 대상 설정 데이터</param>
        public void ResetGraphics(SettingsSO currentSettings)
        {
            currentResolutionIndex = 0;
            windowModeIndex = SettingsGraphicsUtility.BorderlessWindowModeIndex;

            resolutionDropdown.SetValue(currentResolutionIndex, false);
            resolutionDropdown.RefreshShownValue();
            windowModeDropdown.SetValue(windowModeIndex, false);
            windowModeDropdown.RefreshShownValue();

            OnResolutionChanged();

            currentSettings.SaveGraphicsSettings(currentResolutionIndex, windowModeIndex);
            Setup(currentResolutionIndex, windowModeIndex);
        }
    }

}
