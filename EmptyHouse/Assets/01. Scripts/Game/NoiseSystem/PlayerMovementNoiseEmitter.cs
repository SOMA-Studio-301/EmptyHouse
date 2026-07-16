using Unity.Netcode;
using UnityEngine;

namespace EmptyHouse.NoiseSystem
{
    [RequireComponent(typeof(PlayerController), typeof(NetworkObject))]
    public sealed class PlayerMovementNoiseEmitter : NetworkBehaviour
    {
        [SerializeField] private NoiseEmittedEventChannelSO emittedChannel;
        [Min(0.51f)] [SerializeField] private float emissionIntervalSeconds = 0.6f;
        [Min(0f)] [SerializeField] private float movingSpeedThreshold = 0.05f;
        [Min(0f)] [SerializeField] private float walkingDb = 20f;
        [Min(0f)] [SerializeField] private float crouchingDb = 8f;

        private PlayerController playerController;
        private ZombiePerceptionSource perceptionSource;
        private readonly NetworkVariable<bool> serverCrouching = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private bool lastReportedCrouching;
        private float elapsed;

        private void Awake()
        {
            playerController = GetComponent<PlayerController>();
            perceptionSource = GetComponent<ZombiePerceptionSource>();
        }

        private void FixedUpdate()
        {
            SynchronizeCrouching();
            if (!IsServer || !IsSpawned || emittedChannel == null || playerController == null) return;

            elapsed += Time.fixedDeltaTime;
            if (elapsed < emissionIntervalSeconds) return;
            elapsed = 0f;

            if (playerController.PlanarSpeed < movingSpeedThreshold) return;

            emittedChannel.RaiseEvent(new NoiseEmittedEvent(
                NetworkObjectId,
                transform.position,
                serverCrouching.Value ? crouchingDb : walkingDb));
        }

        private void SynchronizeCrouching()
        {
            if (!IsSpawned || !IsOwner || playerController == null) return;

            bool crouching = playerController.Crouching;
            if (crouching == lastReportedCrouching) return;
            lastReportedCrouching = crouching;

            if (IsServer)
            {
                ApplyCrouchingServer(crouching);
            }
            else
            {
                SetCrouchingServerRpc(crouching);
            }
        }

        [ServerRpc]
        private void SetCrouchingServerRpc(bool crouching)
        {
            ApplyCrouchingServer(crouching);
        }

        private void ApplyCrouchingServer(bool crouching)
        {
            serverCrouching.Value = crouching;
            perceptionSource?.ServerSetCrouching(crouching);
        }
    }
}
