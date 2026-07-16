using Unity.Netcode;
using UnityEngine;

namespace EmptyHouse.NoiseSystem
{
    /// <summary>
    /// Migrates existing interaction noise events into the server propagation pipeline.
    /// ZombieSensorySystem consumes only the propagated channel when it is assigned,
    /// preventing the legacy path from bypassing multi-wall attenuation.
    /// </summary>
    public sealed class LegacyNoisePropagationBridge : NetworkBehaviour
    {
        [SerializeField] private NoiseEventChannelSO legacyChannel;
        [SerializeField] private NoiseEmittedEventChannelSO emittedChannel;
        [SerializeField] private ZombieRuntimeRegistrySO runtimeRegistry;
        [Min(0f)] [SerializeField] private float playerSourceSearchRadius = 3f;

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;
            if (legacyChannel == null || emittedChannel == null || runtimeRegistry == null)
            {
                Debug.LogError($"[{nameof(LegacyNoisePropagationBridge)}] References are incomplete on {name}.", this);
                enabled = false;
                return;
            }

            legacyChannel.OnEventRaised += OnLegacyNoise;
        }

        public override void OnNetworkDespawn()
        {
            if (legacyChannel != null) legacyChannel.OnEventRaised -= OnLegacyNoise;
        }

        private void OnLegacyNoise(NoiseEvent payload)
        {
            if (!IsServer || payload.Decibel <= 0f) return;

            ulong sourceId = ResolveNearestPlayerSource(payload.Origin);
            NetworkObject sourceNetworkObject = payload.Source != null
                ? payload.Source.GetComponentInParent<NetworkObject>()
                : null;
            if (sourceId == 0UL && sourceNetworkObject != null && sourceNetworkObject.IsSpawned)
            {
                sourceId = sourceNetworkObject.NetworkObjectId;
            }

            emittedChannel.RaiseEvent(new NoiseEmittedEvent(sourceId, payload.Origin, payload.Decibel));
        }

        private ulong ResolveNearestPlayerSource(Vector3 origin)
        {
            float bestSqrDistance = playerSourceSearchRadius * playerSourceSearchRadius;
            ulong bestSourceId = 0UL;
            var sources = runtimeRegistry.PerceptionSources;

            for (int i = 0; i < sources.Count; i++)
            {
                IZombiePerceptionSource source = sources[i];
                if (source == null || source.Root == null || source.IsSpectator) continue;

                float sqrDistance = (source.Root.position - origin).sqrMagnitude;
                if (sqrDistance > bestSqrDistance) continue;

                bestSqrDistance = sqrDistance;
                bestSourceId = source.NetworkObjectId;
            }

            return bestSourceId;
        }
    }
}
