using UnityEngine;

namespace EmptyHouse.NoiseSystem
{
    /// <summary>
    /// 소음 감지 방송 채널. <see cref="NoisePropagationSystem"/> 이 거리·차폐 감쇠를 끝낸 뒤 좀비별로 발행한다.
    /// </summary>
    [CreateAssetMenu(fileName = "SO_Event_NoiseDetected", menuName = "Events/Noise/Noise Detected")]
    public sealed class NoiseDetectedEventChannelSO : NoiseEventChannelSO<NoiseDetectedEvent> { }
}
