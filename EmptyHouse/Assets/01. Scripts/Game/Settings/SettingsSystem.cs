using UnityEngine;
using Border.Events;

/// <summary>
/// 씬 진입 시 저장된 설정을 화면에 적용하고 각 채널로 방송하는 시스템.
/// 로드도 저장도 하지 않는다 — 디스크 읽기는 SaveLoadSystem 이 첫 접근 시 스스로 하고,
/// 쓰기는 설정 창이 닫힐 때 UISettings 가 직접 요청한다.
/// 이 컴포넌트의 유일한 책임은 "이 씬의 리스너들에게 현재 설정값을 한 번 흘려보내는 것"이다.
/// 따라서 설정이 적용되어야 하는 모든 씬(Menu·Lobby·Game)에 하나씩 놓는다.
/// 기본값 복원 방송을 받으면 같은 일을 한 번 더 한다 — 리셋은 프로필만 바꾸므로, 화면에 반영하는 것은 여기의 몫이다.
/// </summary>
public class SettingsSystem : MonoBehaviour
{
    [Header("Save")]
    [SerializeField] private SaveLoadSystem saveLoadSystem;

    [Header("Listening to")]
    [SerializeField] private VoidEventChannelSO resetSettingsEvent;

    [Header("Broadcasting on")]
    [SerializeField] private FloatEventChannelSO changeMasterVolumeEvent;
    [SerializeField] private FloatEventChannelSO changeMusicVolumeEvent;
    [SerializeField] private FloatEventChannelSO changeSfxVolumeEvent;
    [SerializeField] private FloatEventChannelSO changeAmbientVolumeEvent;
    [SerializeField] private FloatEventChannelSO changeMouseSensitivityEvent;
    [SerializeField] private IntEventChannelSO changeResolutionEvent;
    [SerializeField] private StringEventChannelSO changeLanguageEvent;

    /// <summary>기본값 복원 방송 구독을 건다.</summary>
    private void OnEnable()
    {
        resetSettingsEvent.OnEventRaised += ApplySettings;
    }

    /// <summary>기본값 복원 방송 구독을 해제한다.</summary>
    private void OnDisable()
    {
        resetSettingsEvent.OnEventRaised -= ApplySettings;
    }

    /// <summary>씬 진입 시 저장된 설정을 한 번 흘려보낸다. 리스너들이 구독을 마친 뒤여야 하므로 Awake 가 아니라 Start 다.</summary>
    private void Start()
    {
        ApplySettings();
    }

    /// <summary>
    /// 현재 프로필 값을 화면에 적용하고 각 채널로 방송한다.
    /// Profile 첫 접근이 디스크 로드를 유발하므로 별도의 로드 호출이 없다.
    /// </summary>
    private void ApplySettings()
    {
        ProfileSave profile = saveLoadSystem.Profile;

        GraphicsSettingsUtility.ApplyGraphicsSettings(profile.ResolutionIndex, profile.WindowModeIndex);

        changeMasterVolumeEvent.RaiseEvent(profile.MasterVolume);
        changeMusicVolumeEvent.RaiseEvent(profile.MusicVolume);
        changeSfxVolumeEvent.RaiseEvent(profile.SfxVolume);
        changeAmbientVolumeEvent.RaiseEvent(profile.AmbientVolume);
        changeResolutionEvent.RaiseEvent(profile.ResolutionIndex);

        // 스폰 시점엔 플레이어가 프로필을 직접 읽지만, 기본값 복원은 이미 스폰된 플레이어에게 알려야 하므로 감도도 방송한다.
        changeMouseSensitivityEvent.RaiseEvent(profile.MouseSensitivity);

        // LocalizationManager 는 Awake 에서 기본 언어(ko)를 적용하므로, 저장된 언어를 여기서 덮어써야 한다.
        changeLanguageEvent.RaiseEvent(profile.LanguageCode);
    }
}
