using UnityEngine;

namespace EmptyHouse.NoiseSystem
{
    /// <summary>
    /// 소음 발생 방송 채널. 소음원이 발행하고 <see cref="NoisePropagationSystem"/> 이 받아 합산 창에 누적한다.
    /// </summary>
    [CreateAssetMenu(fileName = "SO_Event_NoiseEmitted", menuName = "Events/Noise/Noise Emitted")]
    public sealed class NoiseEmittedEventChannelSO : NoiseEventChannelSO<NoiseEmittedEvent> { }
}
