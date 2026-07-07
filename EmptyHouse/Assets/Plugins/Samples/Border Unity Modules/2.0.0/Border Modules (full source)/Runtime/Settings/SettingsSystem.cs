using UnityEngine;
using Border.Core;
using Border.Events;
using Border.UI;
using Border.Localization;

namespace Border.Settings
{
    /// <summary>
    /// 씬 시작 시 저장된 설정값을 적용하고 관련 이벤트를 브로드캐스트하는 시스템이다.
    /// </summary>
    public class SettingsSystem : MonoBehaviour
    {
        [SerializeField] private SettingsSO currentSettings;

        [Tooltip("ISettingsRepository를 구현한 컴포넌트(게임이 제공). 비우면 저장/로드는 비활성.")]
        [SerializeField] private MonoBehaviour settingsRepositoryBehaviour;
        private ISettingsRepository settingsRepository;

        [Header("Listening on")]
        [SerializeField] private VoidEventChannelSO saveSettingsEvent;

        [Header("Broadcasting on")]
        [SerializeField] private FloatEventChannelSO changeMasterVolumeEvent;
        [SerializeField] private FloatEventChannelSO changeMusicVolumeEvent;
        [SerializeField] private FloatEventChannelSO changeSfxVolumeEvent;
        [SerializeField] private IntEventChannelSO changeResolutionEvent;
        [SerializeField] private BoolEventChannelSO changeHudOnEvent;
        [SerializeField] private BoolEventChannelSO changeCameraShakeEvent;
        [SerializeField] private StringEventChannelSO changeLanguageEvent;

        /// <summary>
        /// 저장된 프로필 데이터를 설정 SO에 로드한다.
        /// </summary>
        private void Awake()
        {
            settingsRepository = settingsRepositoryBehaviour as ISettingsRepository;
            if (settingsRepositoryBehaviour != null && settingsRepository == null)
            {
                Log.W($"[SettingsSystem] 할당된 {settingsRepositoryBehaviour.GetType().Name} 는 ISettingsRepository 를 구현하지 않습니다.");
            }

            settingsRepository?.Load(currentSettings);
        }

        /// <summary>
        /// 설정 저장 이벤트를 구독한다.
        /// </summary>
        private void OnEnable()
        {
            if (saveSettingsEvent != null)
            {
                saveSettingsEvent.OnEventRaised += SaveSettings;
            }
        }

        /// <summary>
        /// 설정 저장 이벤트 구독을 해제한다.
        /// </summary>
        private void OnDisable()
        {
            if (saveSettingsEvent != null)
            {
                saveSettingsEvent.OnEventRaised -= SaveSettings;
            }
        }

        /// <summary>
        /// 씬 시작 시 현재 설정값을 다시 로드하고 각 시스템에 이벤트로 전파한다.
        /// </summary>
        private void Start()
        {
            // 디스크 로드 시점이 게임 측 초기화에 의존하므로 Start에서 최종 동기화한다.
            settingsRepository?.Load(currentSettings);
            SetCurrentSettings();
        }

        /// <summary>
        /// 현재 설정값을 각 이벤트 채널로 브로드캐스트한다.
        /// </summary>
        private void SetCurrentSettings()
        {
            SettingsGraphicsUtility.ApplyGraphicsSettings(currentSettings.ResolutionIndex, currentSettings.WindowModeIndex);
            changeMasterVolumeEvent?.RaiseEvent(currentSettings.MasterVolume);
            changeMusicVolumeEvent?.RaiseEvent(currentSettings.MusicVolume);
            changeSfxVolumeEvent?.RaiseEvent(currentSettings.SfxVolume);
            changeResolutionEvent?.RaiseEvent(currentSettings.ResolutionIndex);
            changeHudOnEvent?.RaiseEvent(currentSettings.IsHudOn);
            changeCameraShakeEvent?.RaiseEvent(currentSettings.IsCameraShakeOn);
            changeLanguageEvent?.RaiseEvent(currentSettings.LanguageCode);
        }

        /// <summary>
        /// 현재 설정 SO 값을 프로필 저장 데이터로 반영하고 디스크에 저장한다.
        /// </summary>
        private void SaveSettings()
        {
            settingsRepository?.Save(currentSettings);
        }
    }

}
