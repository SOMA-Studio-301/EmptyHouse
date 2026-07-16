using UnityEngine;
using Border.Core;
using BorderAudio = Border.Audio;

/// <summary>
/// 오디오 재생과 볼륨 슬라이더 연동을 런타임에서 수동 검증하는 테스트 트리거.
/// 각 bool 을 인스펙터에서 체크하면 상승 에지에서 1회 실행되고 스스로 false 로 돌아간다.
/// 검증 절차: playAmbientLoop + playMusicLoop 로 루프를 깔아두고 설정창에서 슬라이더를 움직여 버스별 반응을 듣는다.
/// 개발 전용이므로 빌드 씬에 남기지 않는다.
/// </summary>
public class AudioTester : MonoBehaviour
{
    [Header("Audio")]
    [SerializeField] private BorderAudio.AudioCueEventChannelSO sfxEventChannel;
    [SerializeField] private BorderAudio.AudioCueEventChannelSO musicEventChannel;
    [SerializeField] private AudioRegistrySO audioRegistry;

    [Header("Targets")]
    [SerializeField] private AudioId sfxAudioId = AudioId.Sfx_Ui_Click;         // playSfxBeep 이 재생할 단발 SFX
    [SerializeField] private AudioId ambientAudioId = AudioId.Amb_1F_Wind;      // playAmbientLoop 이 재생할 앰비언트 루프
    [SerializeField] private AudioId musicAudioId = AudioId.Bgm_Lobby_0;        // playMusicLoop 이 재생할 BGM

    [Header("Triggers")]
    [Tooltip("SFX 버스로 단발 비프를 재생한다.")]
    public bool playSfxBeep;

    [Tooltip("Ambient 버스로 루프를 재생한다. 정지 전까지 계속된다.")]
    public bool playAmbientLoop;

    [Tooltip("Music 버스로 루프를 재생한다.")]
    public bool playMusicLoop;

    [Tooltip("재생 중인 Ambient 루프를 정지한다.")]
    public bool stopAmbientLoop;

    [Tooltip("재생 중인 BGM 을 정지한다.")]
    public bool stopMusic;

    private BorderAudio.AudioCueKey ambientKey = BorderAudio.AudioCueKey.Invalid;

    /// <summary>각 트리거의 상승 에지를 감지해 1회 실행하고 플래그를 되돌린다.</summary>
    private void Update()
    {
        if (playSfxBeep)
        {
            playSfxBeep = false;
            PlaySfx(sfxAudioId);
        }

        if (playAmbientLoop)
        {
            playAmbientLoop = false;
            ambientKey = PlaySfx(ambientAudioId);
        }

        if (playMusicLoop)
        {
            playMusicLoop = false;
            PlayMusic(musicAudioId);
        }

        if (stopAmbientLoop)
        {
            stopAmbientLoop = false;
            sfxEventChannel.RaiseStopEvent(ambientKey);
            ambientKey = BorderAudio.AudioCueKey.Invalid;
        }

        if (stopMusic)
        {
            stopMusic = false;
            musicEventChannel.RaiseStopEvent(BorderAudio.AudioCueKey.Invalid);
        }
    }

    /// <summary>
    /// audioRegistry에서 AudioId로 cue/config를 룩업해 sfxEventChannel로 발행한다.
    /// 룩업 실패나 AudioId.None이면 아무것도 하지 않는다.
    /// </summary>
    /// <param name="audioId">재생할 사운드 식별자. None이면 스킵.</param>
    /// <returns>정지에 쓸 키. 실패 시 Invalid.</returns>
    private BorderAudio.AudioCueKey PlaySfx(AudioId audioId)
    {
        if (audioId == AudioId.None) return BorderAudio.AudioCueKey.Invalid;
        if (!audioRegistry.TryGetAudio(audioId, out BorderAudio.AudioCueSO cue, out BorderAudio.AudioConfigurationSO config))
        {
            Log.W($"[AudioTester] 레지스트리 룩업 실패: {audioId}");
            return BorderAudio.AudioCueKey.Invalid;
        }

        if (cue == null || config == null || cue.GetClip() == null) return BorderAudio.AudioCueKey.Invalid;
        return sfxEventChannel.RaisePlayEvent(cue, config, transform.position);
    }

    /// <summary>audioRegistry에서 AudioId로 cue/config를 룩업해 musicEventChannel로 발행한다.</summary>
    /// <param name="audioId">재생할 BGM 식별자. None이면 스킵.</param>
    private void PlayMusic(AudioId audioId)
    {
        if (audioId == AudioId.None) return;
        if (!audioRegistry.TryGetAudio(audioId, out BorderAudio.AudioCueSO cue, out BorderAudio.AudioConfigurationSO config))
        {
            Log.W($"[AudioTester] 레지스트리 룩업 실패: {audioId}");
            return;
        }

        if (cue == null || config == null || cue.GetClip() == null) return;
        musicEventChannel.RaisePlayEvent(cue, config, transform.position);
    }
}
