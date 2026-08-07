using UnityEngine;

namespace EmptyHouse.NoiseSystem
{
    /// <summary>
    /// 서버 전용 미터 표본 방송 채널. 발행자는 <see cref="NoisePropagationSystem"/> 하나뿐이고,
    /// 구독자는 각 플레이어의 <see cref="PlayerNoise"/> 다(자기 SourceId 만 채택).
    /// </summary>
    [CreateAssetMenu(fileName = "SO_Event_NoiseMeterSample", menuName = "Events/Noise/Noise Meter Sample")]
    public sealed class NoiseMeterSampleEventChannelSO : NoiseEventChannelSO<NoiseMeterSampleEvent> { }
}
