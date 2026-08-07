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
}
