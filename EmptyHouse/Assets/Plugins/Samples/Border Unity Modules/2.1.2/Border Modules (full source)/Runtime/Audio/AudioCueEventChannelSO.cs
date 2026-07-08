using UnityEngine;
using UnityEngine.Events;
using Border.Core;

namespace Border.Audio
{
    [CreateAssetMenu(fileName = "NewAudioCueEventChannel", menuName = "Events/Audio/AudioCueEventChannel")]
    public class AudioCueEventChannelSO : ScriptableObject
    {
        public AudioCuePlayAction OnAudioCuePlayRequested;
        public AudioCuePlayWithPoolAction OnAudioCuePlayWithPoolRequested;
        public AudioCueStopAction OnAudioCueStopRequested;

        public AudioCueKey RaisePlayEvent(AudioCueSO audioCue, AudioConfigurationSO audioConfiguration, Vector3 position)
        {
            AudioCueKey audioCueKey = AudioCueKey.Invalid;
            // Audio Source 플레이
            if (OnAudioCuePlayRequested != null)
            {
                audioCueKey = OnAudioCuePlayRequested.Invoke(audioCue, audioConfiguration, position);    
            }
            else
            {
                Log.W("AudioCue Play 요청 액션 할당이 없습니다.");
            }
            return audioCueKey;
        }

        public AudioCueKey RaisePlayEvent(SoundEmitterPoolSO pool, AudioCueSO audioCue, AudioConfigurationSO audioConfiguration, Vector3 position)
        {
            AudioCueKey audioCueKey = AudioCueKey.Invalid;
            if (OnAudioCuePlayWithPoolRequested != null)
            {
                audioCueKey = OnAudioCuePlayWithPoolRequested.Invoke(pool, audioCue, audioConfiguration, position);
            }
            else
            {
                Log.W("AudioCue Play(With Pool) 요청 액션 할당이 없습니다.");
            }
            return audioCueKey;
        }

        public bool RaiseStopEvent(AudioCueKey audioCueKey)
        {
            bool requestSucceed = false;
            if (OnAudioCueStopRequested != null)
            {
                // Key에 맞는거 종료
                requestSucceed = OnAudioCueStopRequested.Invoke(audioCueKey);    
            }
            else
            {
                Log.W("AudioCue Stop 요청 액션 할당이 없습니다.");
            }
            return requestSucceed;
        }
    }

    public delegate AudioCueKey AudioCuePlayAction(AudioCueSO audioCue, AudioConfigurationSO audioConfiguration, Vector3 position);
    public delegate AudioCueKey AudioCuePlayWithPoolAction(SoundEmitterPoolSO pool, AudioCueSO audioCue, AudioConfigurationSO audioConfiguration, Vector3 position);
    public delegate bool AudioCueStopAction(AudioCueKey key);
}
