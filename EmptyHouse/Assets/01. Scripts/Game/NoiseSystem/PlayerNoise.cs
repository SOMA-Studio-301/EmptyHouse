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
    /// (<c>PlayerDisguise</c> → <c>UIDisguiseGauge</c> 와 같은 형태).
    ///
    /// 이동은 <b>지속 소음</b>이라 주기적으로 쏘는 이벤트가 아니라 매 물리 틱 갱신되는 레벨로 발행한다(3-2) —
    /// 걷기 시작·정지가 합산 창을 기다리지 않고 미터에 즉시 나타나는 것이 이 구조 덕이다.
    /// 스펙 미결 항목 Q4(지속 소음 미터 감쇠 곡선)는 "즉시"로 구현했다 — 미결표에는 아직 미반영이다.
    ///
    /// 웅크린 동안은 이동 레벨이 0 이라 소음이 발생하지 않는다 — 웅크림은 청각 은신 수단이고,
    /// 대신 시각 탐지에는 그대로 걸린다(<see cref="ZombiePerceptionSource"/>).
    /// </summary>
    [RequireComponent(typeof(PlayerController), typeof(NetworkObject))]
    public sealed class PlayerNoise : NetworkBehaviour
    {
        [SerializeField] private NoiseEmittedEventChannelSO emittedChannel;
        [Min(0f)] [SerializeField] private float movingSpeedThreshold = 0.05f;
        [Min(0f)] [SerializeField] private float walkingDb = 20f;
        [Min(0f)] [SerializeField] private float movementHoldSeconds = 0.1f; // 이동 관측이 끊긴 뒤 0 으로 내리기까지의 유예. 위치 복제 틱 간격(30Hz ≈ 0.033초)보다 넉넉해야 걷는 중에 레벨이 깜빡이지 않는다

        [Header("Meter")]
        [SerializeField] private NoiseMeterSampleEventChannelSO meterSampleChannel; // 구독(서버): 버킷 합계 표본. 자기 SourceId 것만 채택한다
        [SerializeField] private NoiseMeterLevelChangedEventChannelSO meterLevelChanged; // 발행(오너): 표시용 raw dB. 씬 레벨 HUD 가 구독한다

        private PlayerController playerController;
        private ZombiePerceptionSource perceptionSource;

        // 서버가 쓰고 오너만 읽는 현재 발생 dB. 좀비 판정에 쓰인 그 값이며 미터의 유일한 진실 원천이다
        private readonly NetworkVariable<float> emittedDb = new NetworkVariable<float>(
            0f,
            NetworkVariableReadPermission.Owner,
            NetworkVariableWritePermission.Server);

        // 서버가 마지막으로 이동을 관측한 시각. movementHoldSeconds 유예의 기준점이다
        private float lastMovingTime;

        // 마지막으로 파이프에 발행한 이동 레벨. 값이 실제로 바뀐 틱에만 채널을 두드리기 위한 비교값
        private float publishedMovementDb;

        private bool lastReportedCrouching; // 시각 탐지 쪽에 마지막으로 전달한 웅크림 상태. 변화 순간에만 전달하기 위한 비교값
        private Vector3 previousServerPosition;

        private void Awake()
        {
            playerController = GetComponent<PlayerController>();
            perceptionSource = GetComponent<ZombiePerceptionSource>();
        }

        /// <summary>
        /// 서버면 이동 관측 기준점을 잡고 표본 채널을 구독한다. 오너면 복제값 변경을 구독하고 HUD 초기 표시를 0 으로 맞춘다.
        /// </summary>
        public override void OnNetworkSpawn()
        {
            Log.D($"[PlayerNoise] Spawn — id {NetworkObjectId} / server {IsServer} / owner {IsOwner}");

            if (IsServer)
            {
                previousServerPosition = transform.position;
                lastMovingTime = float.NegativeInfinity; // 스폰 직후는 정지 상태로 시작한다
                publishedMovementDb = 0f;

                meterSampleChannel.OnEventRaised += OnMeterSample;
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
                // 구독을 먼저 끊고 0 을 발행한다 — 순서를 바꾸면 되돌아온 표본이 디스폰 중인 NetworkVariable 을 건드린다.
                meterSampleChannel.OnEventRaised -= OnMeterSample;
                PublishMovementLevel(0f); // 남은 이동 레벨을 지워야 이 소음원의 버킷이 정리된다
            }

            if (IsOwner)
            {
                emittedDb.OnValueChanged -= HandleEmittedDbChanged;
                PublishLevel(0f);
            }

            Log.D($"[PlayerNoise] Despawn — id {NetworkObjectId}");
        }

        /// <summary>
        /// 매 프레임 이동 레벨을 평가해 파이프에 반영한다. 이동은 <b>지속 소음</b>이라 주기적으로 쏘는 이벤트가 아니라
        /// 상태 그대로 다룬다(3-2) — 그래서 걷기 시작·정지가 창을 기다리지 않고 미터에 바로 나타난다.
        /// <b>FixedUpdate 가 아니라 LateUpdate 인 이유</b>: 비권한 NetworkTransform 은 프레임당 한 번(PreLateUpdate) 위치를 쓴다.
        /// 물리 틱마다 재면 같은 위치를 두 번 읽는 틱이 생겨 걷는 중에 속도가 0 으로 튄다.
        /// </summary>
        private void LateUpdate()
        {
            // 매 프레임 호출되므로 진입 트레이스를 두지 않는다.
            if (!IsServer || !IsSpawned) return;

            bool crouching = playerController != null && playerController.Crouching;
            SynchronizeCrouching(crouching);

            Vector3 currentPosition = transform.position;
            Vector3 displacement = currentPosition - previousServerPosition;
            displacement.y = 0f;
            previousServerPosition = currentPosition;

            // 이동은 오너 권한이라 서버는 복제로 위치를 받는다 — 네트워크 틱(30Hz)이 프레임보다 성기면
            // 변위가 0 인 프레임이 섞인다. 마지막 관측 시각에 유예를 두어 그 구멍을 메운다.
            if (displacement.magnitude / Time.deltaTime >= movingSpeedThreshold) lastMovingTime = Time.time;

            // 웅크린 동안은 이동 소음이 없다 — 웅크림은 청각 은신 수단이고 시각 탐지에는 그대로 걸린다.
            bool moving = !crouching && Time.time - lastMovingTime <= movementHoldSeconds;
            PublishMovementLevel(moving ? walkingDb : 0f);
        }

        /// <summary>이동 레벨을 소음 파이프에 발행한다. 값이 실제로 바뀐 틱에만 보내 채널·복제 트래픽을 최소로 둔다.</summary>
        /// <param name="db">이번 틱의 이동 발생 dB. 정지·웅크림이면 0.</param>
        private void PublishMovementLevel(float db)
        {
            // 매 물리 틱 호출되므로 진입 트레이스를 두지 않는다.
            if (Mathf.Approximately(db, publishedMovementDb)) return;

            publishedMovementDb = db;
            emittedChannel.RaiseEvent(new NoiseEmittedEvent(
                NetworkObjectId,
                transform.position,
                db,
                NoiseSourceChannel.Movement));
        }

        /// <summary>
        /// 서버 표본을 받아 내 것이면 <see cref="emittedDb"/> 에 기록한다. 남의 소음은 버린다(D32).
        /// 표본은 버킷이 바뀔 때마다 오므로 값이 실제로 달라진 경우에만 복제를 건드린다.
        /// </summary>
        /// <param name="payload">버킷 합계가 갱신되며 확정된 표본.</param>
        private void OnMeterSample(NoiseMeterSampleEvent payload)
        {
            // 버킷이 바뀔 때마다 호출되므로 진입 트레이스를 두지 않는다.
            if (payload.SourceId != NetworkObjectId) return;
            if (Mathf.Approximately(payload.EmittedDb, emittedDb.Value)) return;

            emittedDb.Value = payload.EmittedDb;
        }

        /// <summary>복제된 발생 dB 변경을 HUD 채널로 중계한다(오너 전용 구독이라 남의 dB 는 여기로 오지 않는다 — D32).</summary>
        /// <param name="previous">직전 발생 dB.</param>
        /// <param name="current">변경된 발생 dB.</param>
        private void HandleEmittedDbChanged(float previous, float current)
        {
            // 발생 dB 가 바뀔 때마다 호출되므로 진입 트레이스를 두지 않는다.
            PublishLevel(current);
        }

        /// <summary>표시용 발생 dB 를 HUD 채널에 발행한다. 정규화하지 않은 raw dB 그대로 보낸다.</summary>
        /// <param name="db">발행할 발생 dB.</param>
        private void PublishLevel(float db)
        {
            // 발생 dB 가 바뀔 때마다 호출되므로 진입 트레이스를 두지 않는다.
            meterLevelChanged.RaiseEvent(db);
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
