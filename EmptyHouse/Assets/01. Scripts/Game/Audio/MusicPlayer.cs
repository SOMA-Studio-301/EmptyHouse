using Border.Core;
using UnityEngine;
using BorderAudio = Border.Audio;

/// <summary>
/// 씬의 BGM 을 재생한다. 프리팹 하나를 씬마다 놓고 인스펙터에서 트랙만 바꾼다.
///
/// BGM 은 단발이 아니라 루프라 SFXEventChannelSO 가 아닌 Border 큐 채널을 쓴다.
/// 그 채널이 cue/config 를 인자로 받으므로 AudioId 룩업은 이쪽에서 한다 — SFX 와 달리 AudioManager 에 맡길 수 없다.
///
/// BGM 은 비디제틱이라 위치 개념이 없다. 좌표는 채널 시그니처를 맞추기 위한 값이며 2D 설정(SO_AudioConfig_Bgm)이 이를 무시한다.
/// </summary>
public class MusicPlayer : MonoBehaviour
{
    [Header("Track")]
    [SerializeField] private AudioId trackAudioId = AudioId.Bgm_Lobby_0; // 이 씬에서 틀 곡. None 이면 무음

    [Header("Audio")]
    [SerializeField] private BorderAudio.AudioCueEventChannelSO musicEventChannel; // AudioManager 가 구독하는 BGM 채널
    [SerializeField] private AudioRegistrySO audioRegistry; // AudioId → cue/config 룩업

    /// <summary>
    /// 트랙 재생을 시작한다.
    /// Awake/OnEnable 이 아니라 Start 인 이유는 둘이다: AudioManager 가 Awake 에서 레지스트리를 초기화하고
    /// OnEnable 에서 채널을 구독하므로, 그보다 먼저 발행하면 구독자가 없어 소리가 나지 않는다.
    /// </summary>
    private void Start()
    {
        Play();
    }

    /// <summary>인스펙터에 지정된 트랙을 BGM 채널로 발행한다. 룩업 실패나 None 이면 아무것도 하지 않는다.</summary>
    public void Play()
    {
        Log.D($"[MusicPlayer] Play {trackAudioId}");

        if (trackAudioId == AudioId.None) return;

        if (!audioRegistry.TryGetAudio(trackAudioId, out BorderAudio.AudioCueSO cue, out BorderAudio.AudioConfigurationSO config))
        {
            Log.W($"[MusicPlayer] 레지스트리 룩업 실패: {trackAudioId}");
            return;
        }

        musicEventChannel.RaisePlayEvent(cue, config, transform.position);
    }

    /// <summary>재생 중인 BGM 을 정지한다. 씬 전환처럼 곡을 끊어야 하는 쪽이 호출한다.</summary>
    public void Stop()
    {
        Log.D("[MusicPlayer] Stop");

        musicEventChannel.RaiseStopEvent(BorderAudio.AudioCueKey.Invalid);
    }
}
