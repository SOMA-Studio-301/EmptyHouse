using UnityEngine;
using Border.Core;
using BorderAudio = Border.Audio;

/// <summary>
/// 단발 효과음 재생 요청 채널. 호출자는 AudioId 와 좌표만 보내고, 레지스트리 룩업은 AudioManager 가 한다.
/// Border 의 AudioCueEventChannelSO 를 쓰지 않는 이유는 그쪽이 Cue/Configuration 을 인자로 받기 때문이다 —
/// 그러면 모든 호출자가 AudioRegistrySO 를 들고 룩업까지 해야 해서 결합이 불필요하게 커진다.
/// 루프 큐를 정지하려면 RaisePlayEvent 가 돌려준 AudioCueKey 를 들고 있다가 RaiseStopEvent 에 넘긴다.
/// </summary>
[CreateAssetMenu(fileName = "SO_Event_Sfx", menuName = "EmptyHouse/Audio/SFX Event Channel")]
public class SFXEventChannelSO : ScriptableObject
{
    public SfxPlayAction OnSfxPlayRequested;    // AudioManager 가 구독하는 재생 요청
    public SfxStopAction OnSfxStopRequested;    // AudioManager 가 구독하는 정지 요청

    /// <summary>효과음 재생을 요청한다.</summary>
    /// <param name="audioId">재생할 사운드 식별자.</param>
    /// <param name="position">재생될 월드 좌표.</param>
    /// <returns>정지에 쓸 키. 실패 시 Invalid.</returns>
    public BorderAudio.AudioCueKey RaisePlayEvent(AudioId audioId, Vector3 position)
    {
        if (OnSfxPlayRequested == null)
        {
            Log.W("[SFXEventChannel] SFX Play 요청 액션 할당이 없습니다.");
            return BorderAudio.AudioCueKey.Invalid;
        }

        return OnSfxPlayRequested.Invoke(audioId, position);
    }

    /// <summary>키에 해당하는 효과음 정지를 요청한다.</summary>
    /// <param name="audioCueKey">정지할 대상의 키.</param>
    /// <returns>정지 요청이 처리됐는지 여부.</returns>
    public bool RaiseStopEvent(BorderAudio.AudioCueKey audioCueKey)
    {
        if (OnSfxStopRequested == null)
        {
            Log.W("[SFXEventChannel] SFX Stop 요청 액션 할당이 없습니다.");
            return false;
        }

        return OnSfxStopRequested.Invoke(audioCueKey);
    }
}

/// <summary>효과음 재생 요청 델리게이트.</summary>
/// <param name="audioId">재생할 사운드 식별자.</param>
/// <param name="position">재생될 월드 좌표.</param>
/// <returns>정지에 쓸 키.</returns>
public delegate BorderAudio.AudioCueKey SfxPlayAction(AudioId audioId, Vector3 position);

/// <summary>효과음 정지 요청 델리게이트.</summary>
/// <param name="audioCueKey">정지할 대상의 키.</param>
/// <returns>정지 성공 여부.</returns>
public delegate bool SfxStopAction(BorderAudio.AudioCueKey audioCueKey);
