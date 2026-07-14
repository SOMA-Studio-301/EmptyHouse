using UnityEngine;
using Border.Events;
using Border.Settings;

/// <summary>
/// 설정 창 AUDIO 탭. 마스터·음악·효과음·앰비언트 볼륨 슬라이더를 담당한다.
/// 값이 바뀌면 즉시 프로필에 쓰고 채널로 방송한다 — 디스크 저장은 창이 닫힐 때 UISettings 가 요청한다.
/// 슬라이더는 0~100 눈금이고 프로필은 0~1 정규화 값을 쓴다.
/// </summary>
public class UISettingsAudioPanel : MonoBehaviour
{
    private const float SliderMax = 100f;

    [Header("Save")]
    [SerializeField] private SaveLoadSystem saveLoadSystem;

    [Header("Sliders")]
    [SerializeField] private UISettingsSlider masterSlider;
    [SerializeField] private UISettingsSlider musicSlider;
    [SerializeField] private UISettingsSlider sfxSlider;
    [SerializeField] private UISettingsSlider ambientSlider;

    [Header("Broadcasting on")]
    [SerializeField] private FloatEventChannelSO changeMasterVolumeEvent;
    [SerializeField] private FloatEventChannelSO changeMusicVolumeEvent;
    [SerializeField] private FloatEventChannelSO changeSfxVolumeEvent;
    [SerializeField] private FloatEventChannelSO changeAmbientVolumeEvent;

    /// <summary>슬라이더에 현재 프로필 값을 채우고 변경 구독을 건다. 구독보다 값 주입이 먼저다 — 방송을 되풀이하지 않기 위함이다.</summary>
    private void OnEnable()
    {
        ProfileSave profile = saveLoadSystem.Profile;

        masterSlider.SetSlider(profile.MasterVolume * SliderMax);
        musicSlider.SetSlider(profile.MusicVolume * SliderMax);
        sfxSlider.SetSlider(profile.SfxVolume * SliderMax);
        ambientSlider.SetSlider(profile.AmbientVolume * SliderMax);

        masterSlider.ValueChanged += SetMasterVolume;
        musicSlider.ValueChanged += SetMusicVolume;
        sfxSlider.ValueChanged += SetSfxVolume;
        ambientSlider.ValueChanged += SetAmbientVolume;
    }

    /// <summary>슬라이더 변경 구독을 해제한다.</summary>
    private void OnDisable()
    {
        masterSlider.ValueChanged -= SetMasterVolume;
        musicSlider.ValueChanged -= SetMusicVolume;
        sfxSlider.ValueChanged -= SetSfxVolume;
        ambientSlider.ValueChanged -= SetAmbientVolume;
    }

    /// <summary>마스터 볼륨을 프로필에 쓰고 방송한다.</summary>
    /// <param name="sliderValue">슬라이더 눈금값(0~100).</param>
    private void SetMasterVolume(float sliderValue)
    {
        float volume = sliderValue / SliderMax;
        saveLoadSystem.Profile.MasterVolume = volume;
        changeMasterVolumeEvent.RaiseEvent(volume);
    }

    /// <summary>음악 볼륨을 프로필에 쓰고 방송한다.</summary>
    /// <param name="sliderValue">슬라이더 눈금값(0~100).</param>
    private void SetMusicVolume(float sliderValue)
    {
        float volume = sliderValue / SliderMax;
        saveLoadSystem.Profile.MusicVolume = volume;
        changeMusicVolumeEvent.RaiseEvent(volume);
    }

    /// <summary>효과음 볼륨을 프로필에 쓰고 방송한다.</summary>
    /// <param name="sliderValue">슬라이더 눈금값(0~100).</param>
    private void SetSfxVolume(float sliderValue)
    {
        float volume = sliderValue / SliderMax;
        saveLoadSystem.Profile.SfxVolume = volume;
        changeSfxVolumeEvent.RaiseEvent(volume);
    }

    /// <summary>앰비언트 볼륨을 프로필에 쓰고 방송한다.</summary>
    /// <param name="sliderValue">슬라이더 눈금값(0~100).</param>
    private void SetAmbientVolume(float sliderValue)
    {
        float volume = sliderValue / SliderMax;
        saveLoadSystem.Profile.AmbientVolume = volume;
        changeAmbientVolumeEvent.RaiseEvent(volume);
    }
}
