using UnityEngine;

namespace EmptyHouse.NoiseSystem
{
    /// <summary>웅크림 상태 전환을 소음 파이프에 알리는 훅 채널. 이동 소음 dB 산출의 입력이다.</summary>
    [CreateAssetMenu(fileName = "SO_Event_CrouchNoiseHook", menuName = "Events/Noise/Hooks/Crouch")]
    public sealed class CrouchNoiseStateEventChannelSO : NoiseEventChannelSO<CrouchNoiseStateEvent> { }
}
