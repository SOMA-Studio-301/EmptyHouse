using Border.Core;
using Unity.Netcode;
using UnityEngine;

namespace EmptyHouse.NoiseSystem
{
    /// <summary>
    /// 이 플레이어와 소음 시스템의 접점 전체를 소유한다 — <b>발행</b>(이동 소음 → 판정 파이프)과 <b>표시</b>(합산 결과 → 데시벨 미터).
    /// 같은 파이프의 출력과 입력이고 도메인·권한(서버)·키(NetworkObjectId)가 모두 같아 한 컴포넌트로 둔다.
    /// 미터가 보여야 하는 값은 "판정에 실제로 쓰인 그 값"이므로(소음시스템.md 9-1 ①), 클라에서 다시 합산하지 않고
    /// 서버가 <see cref="emittedDb"/> 에 쓴 것을 오너만 읽는다 — 규칙이 두 벌이 되는 순간 미터가 거짓말을 한다(9-3).
    /// HUD 위젯은 참조하지 않는다 — 오너가 <see cref="NoiseMeterLevelChangedEventChannelSO"/> 로 발행하고 씬 레벨 Canvas-HUD 가 구독한다
    /// (<c>PlayerDisguise</c> → <c>UIDisguiseGauge</c> 와 같은 형태). 표시 스무딩(Q4-B)은 표시 관심사라 HUD 쪽이 맡는다.
    /// </summary>
    [RequireComponent(typeof(PlayerController), typeof(NetworkObject))]
    public sealed class PlayerNoise : NetworkBehaviour
    {
        [SerializeField] private NoiseEmittedEventChannelSO emittedChannel;
        [Min(0.51f)] [SerializeField] private float emissionIntervalSeconds = 0.6f;
        [Min(0f)] [SerializeField] private float movingSpeedThreshold = 0.05f;
        [Min(0f)] [SerializeField] private float walkingDb = 20f;
        [Min(0f)] [SerializeField] private float crouchingDb = 8f;

        [Header("Meter")]
        [SerializeField] private NoiseMeterSampleEventChannelSO meterSampleChannel; // 구독(서버): 합산 창이 닫힌 표본. 자기 SourceId 것만 채택한다
        [SerializeField] private NoiseMeterLevelChangedEventChannelSO meterLevelChanged; // 발행(오너): 표시용 raw dB. 씬 레벨 HUD 가 구독한다
        [SerializeField] private NoisePropagationSettingsSO settings; // 합산 창 길이의 원천. 무음 판정이 판정계와 어긋나지 않게 같은 값을 쓴다
        [Min(1f)] [SerializeField] private float silenceWindowMultiplier = 2f; // 이 배수 × 합산 창 동안 표본이 없으면 무음(0)으로 확정한다

        private PlayerController playerController;
        private ZombiePerceptionSource perceptionSource;

        // 서버가 쓰고 오너만 읽는 현재 발생 dB. 좀비 판정에 쓰인 그 값이며 미터의 유일한 진실 원천이다
        private readonly NetworkVariable<float> emittedDb = new NetworkVariable<float>(
            0f,
            NetworkVariableReadPermission.Owner,
            NetworkVariableWritePermission.Server);

        // 서버가 마지막 표본을 받은 시각. 무음 타임아웃의 기준점이다
        private float lastSampleTime;
        private readonly NetworkVariable<bool> serverCrouching = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private bool lastReportedCrouching;
        private float elapsed;
        private Vector3 previousServerPosition;
        private float planarDistanceSinceEmission;

        private void Awake()
        {
            playerController = GetComponent<PlayerController>();
            perceptionSource = GetComponent<ZombiePerceptionSource>();
        }

        /// <summary>
        /// 서버면 이동 적분 기준점을 잡고 표본 채널을 구독한다. 오너면 복제값 변경을 구독하고 HUD 초기 표시를 0 으로 맞춘다.
        /// </summary>
        public override void OnNetworkSpawn()
        {
            Log.D($"[PlayerNoise] Spawn — id {NetworkObjectId} / server {IsServer} / owner {IsOwner}");

            if (IsServer)
            {
                previousServerPosition = transform.position;
                planarDistanceSinceEmission = 0f;
                elapsed = 0f;

                meterSampleChannel.OnEventRaised += OnMeterSample;
                lastSampleTime = Time.time;
            }

            if (IsOwner)
            {
                emittedDb.OnValueChanged += HandleEmittedDbChanged;
                PublishLevel(emittedDb.Value);
            }
        }

        /// <summary>구독을 해제한다. 채널은 SO 라 씬 밖에서 살아남으므로 죽은 델리게이트를 남기지 않는다.</summary>
        public override void OnNetworkDespawn()
        {
            if (IsServer)
            {
                meterSampleChannel.OnEventRaised -= OnMeterSample;
            }

            if (IsOwner)
            {
                emittedDb.OnValueChanged -= HandleEmittedDbChanged;
                PublishLevel(0f);
            }

            Log.D($"[PlayerNoise] Despawn — id {NetworkObjectId}");
        }

        private void FixedUpdate()
        {
            SynchronizeCrouching();
            if (!IsServer || !IsSpawned || emittedChannel == null) return;

            Vector3 currentPosition = transform.position;
            Vector3 displacement = currentPosition - previousServerPosition;
            displacement.y = 0f;
            planarDistanceSinceEmission += displacement.magnitude;
            previousServerPosition = currentPosition;

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
                serverCrouching.Value ? crouchingDb : walkingDb));
        }

        /// <summary>
        /// 서버 표본을 받아 내 것이면 <see cref="emittedDb"/> 에 기록하고 무음 타이머를 되감는다. 남의 소음은 버린다(D32).
        /// </summary>
        /// <param name="payload">합산 창이 닫히며 확정된 표본.</param>
        private void OnMeterSample(NoiseMeterSampleEvent payload)
        {
            // 합산 창(0.5초)마다 호출되므로 진입 트레이스를 두지 않는다.
            if (payload.SourceId != NetworkObjectId) return;

            emittedDb.Value = payload.EmittedDb;
            lastSampleTime = Time.time;
        }

        /// <summary>서버의 무음 타임아웃만 돌린다. 표시 갱신은 HUD 소관이라 오너 쪽에서 할 일이 없다.</summary>
        private void Update()
        {
            // 매 프레임 호출되므로 진입 트레이스를 두지 않는다.
            if (!IsServer || !IsSpawned) return;

            TickServerSilence();
        }

        /// <summary>
        /// 표본이 끊긴 지 (합산 창 × <see cref="silenceWindowMultiplier"/>) 를 넘기면 발생 dB 를 0 으로 확정한다.
        /// 정지한 플레이어는 소음 이벤트 자체를 내지 않아 표본이 오지 않으므로, 0 은 이 타임아웃으로만 만들어진다.
        /// </summary>
        private void TickServerSilence()
        {
            // 매 프레임 호출되므로 진입 트레이스를 두지 않는다.
            if (emittedDb.Value <= 0f) return;
            if (Time.time - lastSampleTime <= settings.SumWindowSeconds * silenceWindowMultiplier) return;

            emittedDb.Value = 0f;
        }

        /// <summary>복제된 발생 dB 변경을 HUD 채널로 중계한다(오너 전용 구독이라 남의 dB 는 여기로 오지 않는다 — D32).</summary>
        /// <param name="previous">직전 발생 dB.</param>
        /// <param name="current">변경된 발생 dB.</param>
        private void HandleEmittedDbChanged(float previous, float current)
        {
            // 합산 창(0.5초)마다 호출되므로 진입 트레이스를 두지 않는다.
            PublishLevel(current);
        }

        /// <summary>표시용 발생 dB 를 HUD 채널에 발행한다. 정규화하지 않은 raw dB 그대로 보낸다.</summary>
        /// <param name="db">발행할 발생 dB.</param>
        private void PublishLevel(float db)
        {
            // 합산 창(0.5초)마다 호출되므로 진입 트레이스를 두지 않는다.
            meterLevelChanged.RaiseEvent(db);
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
