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

        /// <summary>마이크 발화를 <b>지속</b> 채널로 넘긴다. dB 0 은 "말이 그쳤다"는 뜻이라 걸러내지 않는다(3-2).</summary>
        /// <param name="payload">오너가 측정하고 서버가 검증한 발화 표본.</param>
        private void OnVoiceNoise(VoiceNoiseEvent payload)
        {
            if (IsServer)
            {
                emittedChannel?.RaiseEvent(new NoiseEmittedEvent(
                    payload.SourceId,
                    payload.Origin,
                    payload.VoiceDb,
                    NoiseSourceChannel.Voice));
            }
        }

        /// <summary>무전 수신음을 수신자 위치의 <b>지속</b> 소음으로 넘긴다(7장 · E4).</summary>
        /// <param name="payload">수신자와 재생 중인 발화 dB.</param>
        private void OnRadioNoise(RadioReceivedNoiseEvent payload)
        {
            if (IsServer)
            {
                emittedChannel?.RaiseEvent(new NoiseEmittedEvent(
                    payload.ReceiverSourceId,
                    payload.ReceiverPosition,
                    payload.VoiceDb,
                    NoiseSourceChannel.Radio));
            }
        }
    }
}
