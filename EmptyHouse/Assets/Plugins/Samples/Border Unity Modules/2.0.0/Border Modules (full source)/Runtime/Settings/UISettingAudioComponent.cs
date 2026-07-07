using UnityEngine;
using UnityEngine.UI;
using Border.Core;
using Border.Events;
using Border.UI;
using Border.Localization;

namespace Border.Settings
{
    public class UISettingsAudioComponent : MonoBehaviour
    {
        [Header("UI Components")]
        [SerializeField] private UISettingsSlider masterVolumeSlider;
        [SerializeField] private UISettingsSlider musicVolumeSlider;
        [SerializeField] private UISettingsSlider sfxVolumeSlider;
        public UISettingsSlider MasterVolumeSlider => masterVolumeSlider;

        [Header("Mute Buttons")]
        [SerializeField] private UIGenericButton masterMuteButton;
        [SerializeField] private UIGenericButton musicMuteButton;
        [SerializeField] private UIGenericButton sfxMuteButton;

        [Header("Broadcasting on")]
        [SerializeField] private FloatEventChannelSO changeMasterVolumeEvent;
        [SerializeField] private FloatEventChannelSO changeMusicVolumeEvent;
        [SerializeField] private FloatEventChannelSO changeSfxVolumeEvent;

        private float masterVolume;
        private float musicVolume;
        private float sfxVolume;

        private float cachedMasterVolume;
        private float cachedMusicVolume;
        private float cachedSfxVolume;

        private const int maxVolume = 100;

        private void OnEnable()
        {
            masterVolumeSlider.ValueChanged += SetMasterVolume;
            musicVolumeSlider .ValueChanged += SetMusicVolume;
            sfxVolumeSlider   .ValueChanged += SetSfxVolume;

            masterMuteButton.Clicked += ToggleMuteMaster;
            musicMuteButton .Clicked += ToggleMuteMusic;
            sfxMuteButton   .Clicked += ToggleMuteSfx;
        }

        private void OnDisable()
        {
            masterVolumeSlider.ValueChanged -= SetMasterVolume;
            musicVolumeSlider .ValueChanged -= SetMusicVolume;
            sfxVolumeSlider   .ValueChanged -= SetSfxVolume;

            masterMuteButton.Clicked -= ToggleMuteMaster;
            musicMuteButton .Clicked -= ToggleMuteMusic;
            sfxMuteButton   .Clicked -= ToggleMuteSfx;
        }

        public void Setup(float newMasterVolume, float newMusicVolume, float newSfxVolume)
        {
            this.masterVolume = Mathf.Clamp01(newMasterVolume);
            this.musicVolume = Mathf.Clamp01(newMusicVolume);
            this.sfxVolume = Mathf.Clamp01(newSfxVolume);

            masterVolumeSlider.SetSlider(masterVolume * maxVolume);
            musicVolumeSlider .SetSlider(musicVolume * maxVolume);
            sfxVolumeSlider   .SetSlider(sfxVolume * maxVolume);

            SetMasterVolume();
            SetMusicVolume();
            SetSfxVolume();
        }

        private void SetMasterVolume()
        {
            changeMasterVolumeEvent?.OnEventRaised(masterVolume);
        }

        private void SetMasterVolume(float value)
        {
            masterVolume = value / maxVolume;
            changeMasterVolumeEvent?.OnEventRaised(masterVolume);
        }

        private void SetMusicVolume()
        {
            changeMusicVolumeEvent?.OnEventRaised(musicVolume);
        }

        private void SetMusicVolume(float value)
        {
            musicVolume = value / maxVolume;
            changeMusicVolumeEvent?.OnEventRaised(musicVolume);
        }

        private void SetSfxVolume()
        {
            changeSfxVolumeEvent?.OnEventRaised(sfxVolume);
        }

        private void SetSfxVolume(float value)
        {
            sfxVolume = value / maxVolume;
            changeSfxVolumeEvent?.OnEventRaised(sfxVolume);
        }

        /// <summary>
        /// Master 볼륨 음소거 토글. 현재 볼륨이 0이면 캐시된 값으로 복원, 아니면 0으로 설정한다.
        /// </summary>
        private void ToggleMuteMaster()
        {
            if (masterVolume > 0f)
            {
                cachedMasterVolume = masterVolume;
                masterVolume = 0f;
            }
            else
            {
                masterVolume = cachedMasterVolume;
            }

            masterVolumeSlider.SetSlider(masterVolume * maxVolume);
            changeMasterVolumeEvent?.OnEventRaised(masterVolume);
        }

        /// <summary>
        /// Music 볼륨 음소거 토글. 현재 볼륨이 0이면 캐시된 값으로 복원, 아니면 0으로 설정한다.
        /// </summary>
        private void ToggleMuteMusic()
        {
            if (musicVolume > 0f)
            {
                cachedMusicVolume = musicVolume;
                musicVolume = 0f;
            }
            else
            {
                musicVolume = cachedMusicVolume;
            }

            musicVolumeSlider.SetSlider(musicVolume * maxVolume);
            changeMusicVolumeEvent?.OnEventRaised(musicVolume);
        }

        /// <summary>
        /// SFX 볼륨 음소거 토글. 현재 볼륨이 0이면 캐시된 값으로 복원, 아니면 0으로 설정한다.
        /// </summary>
        private void ToggleMuteSfx()
        {
            if (sfxVolume > 0f)
            {
                cachedSfxVolume = sfxVolume;
                sfxVolume = 0f;
            }
            else
            {
                sfxVolume = cachedSfxVolume;
            }

            sfxVolumeSlider.SetSlider(sfxVolume * maxVolume);
            changeSfxVolumeEvent?.OnEventRaised(sfxVolume);
        }

        public void SaveVolumes(SettingsSO currentSettings)
        {
            currentSettings.SaveAudioSettings(masterVolumeSlider.GetValue() / maxVolume, 
                musicVolumeSlider.GetValue() / maxVolume, 
                sfxVolumeSlider.GetValue() / maxVolume);
        }

        /// <summary>
        /// Default 값으로 바꾸고 설정 적용
        /// </summary>
        public void ResetVolumes(SettingsSO currentSettings)
        {
            currentSettings.SaveAudioSettings(1f, 0.8f, 1f);
            Setup(1f, 0.8f, 1f);
        }
    }

}
