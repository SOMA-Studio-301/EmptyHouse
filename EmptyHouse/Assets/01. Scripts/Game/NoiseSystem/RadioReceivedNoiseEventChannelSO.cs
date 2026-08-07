using UnityEngine;

namespace EmptyHouse.NoiseSystem
{
    /// <summary>무전 수신음을 수신자 위치의 소음으로 넘기는 훅 채널 (소음시스템.md 7장).</summary>
    [CreateAssetMenu(fileName = "SO_Event_RadioNoiseHook", menuName = "Events/Noise/Hooks/Radio Reception")]
    public sealed class RadioReceivedNoiseEventChannelSO : NoiseEventChannelSO<RadioReceivedNoiseEvent> { }
}
