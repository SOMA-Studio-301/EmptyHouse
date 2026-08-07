using UnityEngine;

namespace EmptyHouse.NoiseSystem
{
    /// <summary>보이스 채팅 발화량을 소음 파이프로 넘기는 훅 채널 (소음시스템.md 7장).</summary>
    [CreateAssetMenu(fileName = "SO_Event_VoiceNoiseHook", menuName = "Events/Noise/Hooks/Voice")]
    public sealed class VoiceNoiseEventChannelSO : NoiseEventChannelSO<VoiceNoiseEvent> { }
}
