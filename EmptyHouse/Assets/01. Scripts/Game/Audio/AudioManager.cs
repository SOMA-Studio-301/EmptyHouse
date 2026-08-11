using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using Border.Core;
using Border.Events;
using BorderAudio = Border.Audio;

/// <summary>
/// 오디오 재생과 볼륨 적용의 단일 창구.
/// 큐 채널로 들어온 재생 요청을 SoundEmitter 풀로 처리하고, 볼륨 채널을 믹서 노출 파라미터에 반영한다.
///
/// Border.Audio.AudioManager 를 쓰지 않고 새로 만든 이유는 셋이다:
/// Ambient 버스를 모르고, 확장 불가능한 패키지 소유 AudioId 에 묶여 있으며, 전작 잔재인 Cave Acoustics(리버브·로우패스)를 상주시킨다.
/// 대신 SoundEmitter·풀·큐 채널 같은 검증된 부품은 Border 것을 그대로 호출한다 — 별칭 BorderAudio 로 참조한다.
///
/// 믹서 파라미터 이름(MasterVolume/MusicVolume/SFXVolume/AmbientVolume)은 에셋에 노출된 문자열과 정확히 일치해야 한다.
/// </summary>
public class AudioManager : MonoBehaviour
{
    private const string MasterVolumeParam = "MasterVolume";
    private const string MusicVolumeParam = "MusicVolume";
    private const string SfxVolumeParam = "SFXVolume";
    private const string AmbientVolumeParam = "AmbientVolume";

    [Header("SoundEmitter Pool")]
    [Tooltip("첫 번째 풀이 SFX·BGM 의 기본 풀로 쓰인다.")]
    [SerializeField] private List<BorderAudio.SoundEmitterPoolSO> pools = new List<BorderAudio.SoundEmitterPoolSO>();
    [SerializeField] private int initialSize = 10;

    [Header("Mixer")]
    [SerializeField] private AudioMixer audioMixer;

    [Header("Registry")]
    [SerializeField] private AudioRegistrySO audioRegistry;

    [Header("Listening on - Cue")]
    [SerializeField] private BorderAudio.AudioCueEventChannelSO musicEventChannel;
    [SerializeField] private SFXEventChannelSO sfxEventChannel; // AudioId 를 받는 우리 채널
    [Tooltip("float 페이로드는 페이드 길이(초)다. 0 이면 즉시 정지와 같다.")]
    [SerializeField] private FloatEventChannelSO fadeOutMusicEvent; // BGM 을 서서히 줄여 끄는 요청

    [Header("Listening on - Volume")]
    [SerializeField] private FloatEventChannelSO changeMasterVolumeEvent;
    [SerializeField] private FloatEventChannelSO changeMusicVolumeEvent;
    [SerializeField] private FloatEventChannelSO changeSfxVolumeEvent;
    [SerializeField] private FloatEventChannelSO changeAmbientVolumeEvent;

    private BorderAudio.SoundEmitterVault soundEmitterVault;
    private BorderAudio.SoundEmitter musicSoundEmitter;

    /// <summary>기본 풀. 풀이 하나도 없으면 재생 자체가 불가능하므로 에러로 알린다.</summary>
    private BorderAudio.SoundEmitterPoolSO DefaultPool => pools.Count > 0 ? pools[0] : null;

    /// <summary>레지스트리와 풀을 초기화한다.</summary>
    private void Awake()
    {
        audioRegistry.Initialize();
        soundEmitterVault = new BorderAudio.SoundEmitterVault();

        for (int i = 0; i < pools.Count; i++)
        {
            BorderAudio.SoundEmitterPoolSO pool = pools[i];
            pool.Prewarm(pool.HasLimit ? pool.LimitActivation : initialSize);
            pool.SetParent(transform);
        }
    }

    /// <summary>큐 채널과 볼륨 채널을 구독한다.</summary>
    private void OnEnable()
    {
        musicEventChannel.OnAudioCuePlayRequested += PlayMusicTrack;
        musicEventChannel.OnAudioCueStopRequested += StopMusic;

        sfxEventChannel.OnSfxPlayRequested += PlaySfx;
        sfxEventChannel.OnSfxStopRequested += StopAudioCue;
        fadeOutMusicEvent.OnEventRaised += FadeOutMusic;

        changeMasterVolumeEvent.OnEventRaised += ChangeMasterVolume;
        changeMusicVolumeEvent.OnEventRaised += ChangeMusicVolume;
        changeSfxVolumeEvent.OnEventRaised += ChangeSfxVolume;
        changeAmbientVolumeEvent.OnEventRaised += ChangeAmbientVolume;
    }

    /// <summary>구독을 해제한다. 채널은 SO 라 씬 밖에서 살아남으므로 짝을 맞춘다.</summary>
    private void OnDisable()
    {
        musicEventChannel.OnAudioCuePlayRequested -= PlayMusicTrack;
        musicEventChannel.OnAudioCueStopRequested -= StopMusic;

        sfxEventChannel.OnSfxPlayRequested -= PlaySfx;
        sfxEventChannel.OnSfxStopRequested -= StopAudioCue;
        fadeOutMusicEvent.OnEventRaised -= FadeOutMusic;

        changeMasterVolumeEvent.OnEventRaised -= ChangeMasterVolume;
        changeMusicVolumeEvent.OnEventRaised -= ChangeMusicVolume;
        changeSfxVolumeEvent.OnEventRaised -= ChangeSfxVolume;
        changeAmbientVolumeEvent.OnEventRaised -= ChangeAmbientVolume;
    }

    /// <summary>
    /// 씬 전환 시 살아있는 이미터를 모두 정리한다.
    /// 정리하지 않으면 파괴된 AudioSource 에 신호를 보내 오류가 난다.
    /// </summary>
    private void OnDestroy()
    {
        StopMusic(BorderAudio.AudioCueKey.Invalid);

        List<BorderAudio.SoundEmitter> emitters = soundEmitterVault.GetAll();
        for (int i = 0; i < emitters.Count; i++)
        {
            soundEmitterVault.Release(emitters[i]);
        }

        soundEmitterVault.Clear();

        for (int i = 0; i < pools.Count; i++)
        {
            pools[i].ClearPool();
        }
    }

    #region VOLUME

    /// <summary>마스터 볼륨을 믹서에 적용한다.</summary>
    /// <param name="normalized">0~1 정규화 볼륨.</param>
    private void ChangeMasterVolume(float normalized)
    {
        SetGroupVolume(MasterVolumeParam, normalized);
    }

    /// <summary>음악 볼륨을 믹서에 적용한다.</summary>
    /// <param name="normalized">0~1 정규화 볼륨.</param>
    private void ChangeMusicVolume(float normalized)
    {
        SetGroupVolume(MusicVolumeParam, normalized);
    }

    /// <summary>효과음 볼륨을 믹서에 적용한다.</summary>
    /// <param name="normalized">0~1 정규화 볼륨.</param>
    private void ChangeSfxVolume(float normalized)
    {
        SetGroupVolume(SfxVolumeParam, normalized);
    }

    /// <summary>앰비언트 볼륨을 믹서에 적용한다. Border 판에는 없는 우리 버스다.</summary>
    /// <param name="normalized">0~1 정규화 볼륨.</param>
    private void ChangeAmbientVolume(float normalized)
    {
        SetGroupVolume(AmbientVolumeParam, normalized);
    }

    /// <summary>정규화 볼륨을 dB 로 바꿔 믹서 노출 파라미터에 쓴다.</summary>
    /// <param name="parameterName">믹서에 노출된 파라미터 이름.</param>
    /// <param name="normalized">0~1 정규화 볼륨.</param>
    private void SetGroupVolume(string parameterName, float normalized)
    {
        float decibel = NormalizedToDecibel(Mathf.Clamp01(normalized));
        if (!audioMixer.SetFloat(parameterName, decibel))
        {
            Log.E($"[AudioManager] 믹서 파라미터를 찾을 수 없다: {parameterName}");
        }
    }

    /// <summary>0~1 을 -80~0dB 로 선형 변환한다. 0 이면 완전 무음(-80dB)이다.</summary>
    /// <param name="normalized">0~1 정규화 볼륨.</param>
    /// <returns>믹서에 넣을 dB 값.</returns>
    private float NormalizedToDecibel(float normalized)
    {
        return (normalized - 1f) * 80f;
    }

    #endregion

    #region PLAYBACK

    /// <summary>
    /// AudioId 로 레지스트리를 룩업해 효과음을 재생한다. SFXEventChannelSO 가 부르는 진입점이다.
    /// 룩업이 여기 있는 덕에 호출자는 AudioRegistrySO 를 알 필요가 없다.
    /// </summary>
    /// <param name="audioId">재생할 사운드 식별자. None 이면 스킵.</param>
    /// <param name="position">재생될 월드 좌표.</param>
    /// <returns>정지에 쓸 키. 실패 시 Invalid.</returns>
    private BorderAudio.AudioCueKey PlaySfx(AudioId audioId, Vector3 position)
    {
        if (audioId == AudioId.None)
        {
            return BorderAudio.AudioCueKey.Invalid;
        }

        if (!audioRegistry.TryGetAudio(audioId, out BorderAudio.AudioCueSO cue, out BorderAudio.AudioConfigurationSO config))
        {
            Log.W($"[AudioManager] 레지스트리 룩업 실패: {audioId}");
            return BorderAudio.AudioCueKey.Invalid;
        }

        return PlayAudioCue(cue, config, position);
    }

    /// <summary>기본 풀로 효과음을 재생한다.</summary>
    /// <param name="audioCue">재생할 큐.</param>
    /// <param name="settings">적용할 오디오 설정.</param>
    /// <param name="position">재생될 월드 좌표.</param>
    /// <returns>정지에 쓸 키. 실패 시 Invalid.</returns>
    public BorderAudio.AudioCueKey PlayAudioCue(BorderAudio.AudioCueSO audioCue, BorderAudio.AudioConfigurationSO settings, Vector3 position)
    {
        return PlayAudioCue(DefaultPool, audioCue, settings, position);
    }

    /// <summary>지정한 풀로 효과음을 재생한다.</summary>
    /// <param name="pool">사용할 이미터 풀.</param>
    /// <param name="audioCue">재생할 큐.</param>
    /// <param name="settings">적용할 오디오 설정.</param>
    /// <param name="position">재생될 월드 좌표.</param>
    /// <returns>정지에 쓸 키. 실패 시 Invalid.</returns>
    public BorderAudio.AudioCueKey PlayAudioCue(BorderAudio.SoundEmitterPoolSO pool, BorderAudio.AudioCueSO audioCue, BorderAudio.AudioConfigurationSO settings, Vector3 position)
    {
        if (audioCue == null || settings == null || pool == null)
        {
            return BorderAudio.AudioCueKey.Invalid;
        }

        AudioClip clip = audioCue.GetClip();
        if (clip == null)
        {
            return BorderAudio.AudioCueKey.Invalid;
        }

        BorderAudio.SoundEmitter emitter = pool.Request();
        if (emitter == null)
        {
            return BorderAudio.AudioCueKey.Invalid;
        }

        emitter.PlayAudioClip(clip, settings, audioCue.Looping, position, audioCue.priority, audioCue.Volume, audioCue.GainMultiplier);
        emitter.OnSoundFinishedPlaying += OnSoundEmitterFinishedPlaying;

        return soundEmitterVault.Add(audioCue, emitter, pool);
    }

    /// <summary>키에 해당하는 효과음을 정지한다.</summary>
    /// <param name="audioCueKey">정지할 대상의 키.</param>
    /// <returns>대상을 찾아 정지했는지 여부.</returns>
    private bool StopAudioCue(BorderAudio.AudioCueKey audioCueKey)
    {
        if (!soundEmitterVault.Get(audioCueKey, out BorderAudio.SoundEmitter emitter))
        {
            return false;
        }

        emitter.OnSoundFinishedPlaying -= OnSoundEmitterFinishedPlaying;
        emitter.Stop();
        soundEmitterVault.Release(emitter);
        return true;
    }

    /// <summary>재생이 끝난 이미터를 풀에 반납한다.</summary>
    /// <param name="emitter">끝난 이미터.</param>
    private void OnSoundEmitterFinishedPlaying(BorderAudio.SoundEmitter emitter)
    {
        emitter.OnSoundFinishedPlaying -= OnSoundEmitterFinishedPlaying;
        soundEmitterVault.Release(emitter);
    }

    /// <summary>BGM 을 재생한다. 이미 같은 곡이 루프 중이면 재시작하지 않고, 다른 곡이면 교체한다.</summary>
    /// <param name="audioCue">재생할 큐.</param>
    /// <param name="settings">적용할 오디오 설정.</param>
    /// <param name="position">재생될 월드 좌표.</param>
    /// <returns>BGM 은 키로 개별 정지하지 않으므로 항상 Invalid.</returns>
    private BorderAudio.AudioCueKey PlayMusicTrack(BorderAudio.AudioCueSO audioCue, BorderAudio.AudioConfigurationSO settings, Vector3 position)
    {
        if (audioCue == null || settings == null)
        {
            return BorderAudio.AudioCueKey.Invalid;
        }

        AudioClip clip = audioCue.GetClip();
        if (clip == null)
        {
            return BorderAudio.AudioCueKey.Invalid;
        }

        if (musicSoundEmitter != null)
        {
            bool sameTrackAlreadyLooping = musicSoundEmitter.IsPlaying()
                                        && musicSoundEmitter.IsLooping()
                                        && musicSoundEmitter.CurrentClip == clip;
            if (sameTrackAlreadyLooping)
            {
                // 재생 요청은 "이 곡이 지금 들려야 한다"는 뜻이다. 페이드아웃이 걸린 채라면
                // 그대로 두면 요청을 받고도 계속 작아져 무음이 되므로, 페이드를 취소하고 음량을 되돌린다.
                // 길이 0 의 페이드가 진행 중인 코루틴을 끊고 즉시 원래 음량으로 되돌리는 역할을 한다.
                musicSoundEmitter.FadeVolume(1f, 0f);
                return BorderAudio.AudioCueKey.Invalid;
            }

            StopMusic(BorderAudio.AudioCueKey.Invalid);
        }

        musicSoundEmitter = DefaultPool.Request();
        if (musicSoundEmitter == null)
        {
            return BorderAudio.AudioCueKey.Invalid;
        }

        musicSoundEmitter.PlayAudioClip(clip, settings, audioCue.Looping, position, 0, audioCue.Volume, audioCue.GainMultiplier);
        musicSoundEmitter.OnSoundFinishedPlaying += StopMusicEmitter;

        soundEmitterVault.Add(audioCue, musicSoundEmitter, DefaultPool);
        return BorderAudio.AudioCueKey.Invalid;
    }

    /// <summary>재생 중인 BGM 을 정지한다.</summary>
    /// <param name="key">쓰이지 않는다. 채널 델리게이트 시그니처를 맞추기 위한 인자다.</param>
    /// <returns>정지했는지 여부.</returns>
    private bool StopMusic(BorderAudio.AudioCueKey key)
    {
        if (musicSoundEmitter == null || !musicSoundEmitter.IsPlaying())
        {
            return false;
        }

        musicSoundEmitter.Stop();
        StopMusicEmitter(musicSoundEmitter);
        return true;
    }

    /// <summary>
    /// 재생 중인 BGM 을 서서히 줄여 끈다. 페이드가 끝나면 이미터가 스스로 종료를 알리고,
    /// 그 신호를 받은 <see cref="StopMusicEmitter"/> 가 풀에 반납한다 —
    /// 루프 큐는 재생 완료 콜백이 없으므로 이 경로가 유일한 반납 통로다.
    /// </summary>
    /// <param name="fadeSeconds">페이드 길이(초). 0 이하면 즉시 정지와 같다.</param>
    private void FadeOutMusic(float fadeSeconds)
    {
        if (musicSoundEmitter == null || !musicSoundEmitter.IsPlaying())
        {
            return;
        }

        musicSoundEmitter.FadeVolume(0f, Mathf.Max(0f, fadeSeconds), stopAfterFade: true, invokeFinishAfterStop: true);
    }

    /// <summary>BGM 이미터를 정리하고 풀에 반납한다.</summary>
    /// <param name="emitter">정리할 이미터.</param>
    private void StopMusicEmitter(BorderAudio.SoundEmitter emitter)
    {
        emitter.OnSoundFinishedPlaying -= StopMusicEmitter;

        if (musicSoundEmitter == emitter)
        {
            musicSoundEmitter = null;
        }

        soundEmitterVault.Release(emitter);
    }

    #endregion
}
