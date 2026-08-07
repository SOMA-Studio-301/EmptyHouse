using Unity.Netcode;
using UnityEngine;

namespace EmptyHouse.NoiseSystem
{
    /// <summary>
    /// 플레이어의 이동 소음 발신. 서버가 표본 구간(emissionIntervalSeconds)의 평균 수평 속력을 보고 걷는 중이면 한 번 발신한다.
    ///
    /// 웅크린 동안은 이동 거리를 아예 누적하지 않아 소음이 발생하지 않는다 — 웅크림은 청각 은신 수단이고,
    /// 대신 시각 탐지에는 그대로 걸린다(<see cref="ZombiePerceptionSource"/>).
    /// </summary>
    [RequireComponent(typeof(PlayerController), typeof(NetworkObject))]
    public sealed class PlayerMovementNoiseEmitter : NetworkBehaviour
    {
        [SerializeField] private NoiseEmittedEventChannelSO emittedChannel;
        [Min(0.51f)] [SerializeField] private float emissionIntervalSeconds = 0.6f;
        [Min(0f)] [SerializeField] private float movingSpeedThreshold = 0.05f;
        [Min(0f)] [SerializeField] private float walkingDb = 20f;

        private PlayerController playerController;
        private ZombiePerceptionSource perceptionSource;
        private bool lastReportedCrouching;
        private float elapsed;
        private Vector3 previousServerPosition;
        private float planarDistanceSinceEmission;

        private void Awake()
        {
            playerController = GetComponent<PlayerController>();
            perceptionSource = GetComponent<ZombiePerceptionSource>();
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;

            previousServerPosition = transform.position;
            planarDistanceSinceEmission = 0f;
            elapsed = 0f;
        }

        private void FixedUpdate()
        {
            if (!IsServer || !IsSpawned) return;

            bool crouching = playerController != null && playerController.Crouching;
            SynchronizeCrouching(crouching);

            if (emittedChannel == null) return;

            Vector3 currentPosition = transform.position;
            Vector3 displacement = currentPosition - previousServerPosition;
            displacement.y = 0f;
            previousServerPosition = currentPosition;

            // 웅크린 프레임의 이동은 소음이 아니다 — 기준점만 옮기고 누적에서 뺀다.
            // 발신 순간에 웅크림을 묻지 않고 프레임 단위로 거르므로, 표본 구간 중간에 웅크렸다 편 경우도 그만큼만 반영된다.
            if (!crouching) planarDistanceSinceEmission += displacement.magnitude;

            elapsed += Time.fixedDeltaTime;
            if (elapsed < emissionIntervalSeconds) return;

            float sampleSeconds = elapsed;
            float averagePlanarSpeed = planarDistanceSinceEmission / Mathf.Max(sampleSeconds, 0.0001f);
            elapsed = 0f;
            planarDistanceSinceEmission = 0f;

            if (averagePlanarSpeed < movingSpeedThreshold) return;

            emittedChannel.RaiseEvent(new NoiseEmittedEvent(
                NetworkObjectId,
                transform.position,
                walkingDb));
        }

        /// <summary>
        /// 웅크림 상태가 바뀐 순간에만 시각 탐지 쪽(<see cref="ZombiePerceptionSource"/>)에 전달한다.
        /// 상태 자체는 PlayerController 의 소유자 권한 NetworkVariable 로 복제되므로 서버가 그대로 읽으면 된다.
        /// </summary>
        /// <param name="crouching">이번 프레임의 웅크림 상태.</param>
        private void SynchronizeCrouching(bool crouching)
        {
            if (crouching == lastReportedCrouching) return;
            lastReportedCrouching = crouching;
            perceptionSource?.ServerSetCrouching(crouching);
        }
    }
}
