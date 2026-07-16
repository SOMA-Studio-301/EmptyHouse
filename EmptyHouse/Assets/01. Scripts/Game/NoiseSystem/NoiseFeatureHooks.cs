using UnityEngine;

namespace EmptyHouse.NoiseSystem
{
    public readonly struct VoiceNoiseEvent
    {
        public ulong SourceId { get; }
        public Vector3 Origin { get; }
        public float VoiceDb { get; }
        public VoiceNoiseEvent(ulong sourceId, Vector3 origin, float voiceDb)
        { SourceId = sourceId; Origin = origin; VoiceDb = voiceDb; }
    }

    public readonly struct RadioReceivedNoiseEvent
    {
        public ulong ReceiverSourceId { get; }
        public Vector3 ReceiverPosition { get; }
        public float VoiceDb { get; }
        public RadioReceivedNoiseEvent(ulong receiverSourceId, Vector3 receiverPosition, float voiceDb)
        { ReceiverSourceId = receiverSourceId; ReceiverPosition = receiverPosition; VoiceDb = voiceDb; }
    }

    public readonly struct CamouflageNoiseStateEvent
    {
        public ulong SourceId { get; }
        public bool IsCamouflaged { get; }
        public CamouflageNoiseStateEvent(ulong sourceId, bool isCamouflaged)
        { SourceId = sourceId; IsCamouflaged = isCamouflaged; }
    }

    public readonly struct CrouchNoiseStateEvent
    {
        public ulong SourceId { get; }
        public bool IsCrouching { get; }
        public CrouchNoiseStateEvent(ulong sourceId, bool isCrouching)
        { SourceId = sourceId; IsCrouching = isCrouching; }
    }

    [CreateAssetMenu(fileName = "SO_Event_VoiceNoiseHook", menuName = "Events/Noise/Hooks/Voice")]
    public sealed class VoiceNoiseEventChannelSO : NoiseEventChannelSO<VoiceNoiseEvent> { }

    [CreateAssetMenu(fileName = "SO_Event_RadioNoiseHook", menuName = "Events/Noise/Hooks/Radio Reception")]
    public sealed class RadioReceivedNoiseEventChannelSO : NoiseEventChannelSO<RadioReceivedNoiseEvent> { }

    [CreateAssetMenu(fileName = "SO_Event_CamouflageNoiseHook", menuName = "Events/Noise/Hooks/Camouflage")]
    public sealed class CamouflageNoiseStateEventChannelSO : NoiseEventChannelSO<CamouflageNoiseStateEvent> { }

    [CreateAssetMenu(fileName = "SO_Event_CrouchNoiseHook", menuName = "Events/Noise/Hooks/Crouch")]
    public sealed class CrouchNoiseStateEventChannelSO : NoiseEventChannelSO<CrouchNoiseStateEvent> { }

}
