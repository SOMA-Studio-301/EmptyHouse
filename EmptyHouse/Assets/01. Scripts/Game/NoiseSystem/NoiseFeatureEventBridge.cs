using Unity.Netcode;
using UnityEngine;

namespace EmptyHouse.NoiseSystem
{
    public sealed class NoiseFeatureEventBridge : NetworkBehaviour
    {
        [SerializeField] private VoiceNoiseEventChannelSO voiceChannel;
        [SerializeField] private RadioReceivedNoiseEventChannelSO radioChannel;
        [SerializeField] private NoiseEmittedEventChannelSO emittedChannel;

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;
            if (voiceChannel != null) voiceChannel.OnEventRaised += OnVoiceNoise;
            if (radioChannel != null) radioChannel.OnEventRaised += OnRadioNoise;
        }

        public override void OnNetworkDespawn()
        {
            if (voiceChannel != null) voiceChannel.OnEventRaised -= OnVoiceNoise;
            if (radioChannel != null) radioChannel.OnEventRaised -= OnRadioNoise;
        }

        private void OnVoiceNoise(VoiceNoiseEvent payload)
        {
            if (IsServer)
            {
                emittedChannel?.RaiseEvent(
                    new NoiseEmittedEvent(payload.SourceId, payload.Origin, payload.VoiceDb));
            }
        }

        private void OnRadioNoise(RadioReceivedNoiseEvent payload)
        {
            if (IsServer)
            {
                emittedChannel?.RaiseEvent(new NoiseEmittedEvent(
                    payload.ReceiverSourceId,
                    payload.ReceiverPosition,
                    payload.VoiceDb));
            }
        }
    }
}
