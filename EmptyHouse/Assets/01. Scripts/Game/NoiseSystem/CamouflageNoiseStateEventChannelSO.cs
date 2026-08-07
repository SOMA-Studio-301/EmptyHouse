using UnityEngine;

namespace EmptyHouse.NoiseSystem
{
    /// <summary>위장 상태 전환을 소음 파이프에 알리는 훅 채널. 위장 중에는 소음 판정이 달라진다.</summary>
    [CreateAssetMenu(fileName = "SO_Event_CamouflageNoiseHook", menuName = "Events/Noise/Hooks/Camouflage")]
    public sealed class CamouflageNoiseStateEventChannelSO : NoiseEventChannelSO<CamouflageNoiseStateEvent> { }
}
