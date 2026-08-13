using System.Collections.Generic;
using Border.Core;
using Unity.Netcode;
using UnityEngine;

namespace EmptyHouse.NoiseSystem
{
    public sealed class NoisePropagationSystem : NetworkBehaviour
    {
        [SerializeField] private NoisePropagationSettingsSO settings;
        [SerializeField] private NoiseEmittedEventChannelSO emittedChannel;
        [SerializeField] private NoiseDetectedEventChannelSO detectedChannel;
        [SerializeField] private NoiseMeterSampleEventChannelSO meterSampleChannel; // 데시벨 미터 표본. 가청 임계 컷 이전의 발생 dB 를 흘린다
        [SerializeField] private LayerMask zombieMask;
        [SerializeField] private LayerMask occlusionMask;

        private readonly Dictionary<ulong, SourceBucket> buckets = new Dictionary<ulong, SourceBucket>();
        private readonly List<ulong> flushKeys = new List<ulong>();
        private readonly HashSet<ulong> candidateIds = new HashSet<ulong>();

        /// <summary>
        /// 한 소음원의 합산 창 상태. 지속 채널은 슬롯을 하나씩 차지해 <b>갱신</b>되고, 단발만 <see cref="OneShotDb"/> 에 <b>누적</b>된다
        /// (소음시스템.md 3-2). 창이 닫힐 때 비워지는 것은 단발뿐이며, 지속 레벨은 발신자가 0 을 보낼 때까지 남는다.
        /// </summary>
        private struct SourceBucket
        {
            public Vector3 OneShotOrigin; // 단발이 난 지점. 투척 착지점처럼 소음원 자신과 다를 수 있어 따로 둔다
            public Vector3 SustainedOrigin; // 지속 소음원의 최근 위치. 레벨이 그대로면 이벤트가 안 오므로 창을 닫을 때 되읽는다
            public float OneShotDb; // 이 창에 들어온 단발 소음의 합. 창이 닫히면 0 으로 비워진다
            public float MovementDb; // 지속 — 이동 레벨
            public float VoiceDb; // 지속 — 마이크 레벨
            public float RadioDb; // 지속 — 무전 수신 레벨
            public float PeakSustainedDb; // 이 창에서 관측한 지속 합의 최대치. 창보다 짧은 소음이 끝점 표본에서 사라지지 않게 한다
            public float FlushAt;

            public float SustainedDb => MovementDb + VoiceDb + RadioDb;

            // 스펙 3-2 의 선형 덧셈. 로그 합산(10·log₁₀)을 쓰지 않는다 — 이 dB 는 물리량이 아니라 게임 스칼라라
            // 걷기(20) + 말하기(40) 는 40.0 이 아니라 60 이어야 한다(12장 AC).
            public float TotalDb => OneShotDb + SustainedDb;
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;
            if (settings == null || emittedChannel == null || detectedChannel == null)
            {
                Debug.LogError($"[{nameof(NoisePropagationSystem)}] References are incomplete on {name}.", this);
                enabled = false;
                return;
            }
            emittedChannel.OnEventRaised += OnNoiseEmitted;
        }

        public override void OnNetworkDespawn()
        {
            if (emittedChannel != null) emittedChannel.OnEventRaised -= OnNoiseEmitted;
            buckets.Clear();
        }

        /// <summary>
        /// 소음을 소음원 버킷에 반영하고 미터에 즉시 흘린다. 지속 채널은 슬롯을 갈아끼우고 단발만 더한다(3-2).
        /// dB 0 을 걸러내지 않는 이유: 지속 채널의 0 은 "그쳤다"는 신호라 이게 막히면 레벨이 영구히 박힌다.
        /// </summary>
        /// <param name="payload">발행된 소음. <see cref="NoiseEmittedEvent.Channel"/> 이 누적 방식을 정한다.</param>
        private void OnNoiseEmitted(NoiseEmittedEvent payload)
        {
            // 지속 채널이 매 변화마다 호출되므로 진입 트레이스를 두지 않는다.
            if (!IsServer) return;

            if (!buckets.TryGetValue(payload.SourceId, out SourceBucket bucket))
            {
                if (payload.EmittedDb <= 0f) return; // 0 으로 시작하는 버킷은 만들 이유가 없다
                bucket.FlushAt = Time.time + settings.SumWindowSeconds;
            }

            switch (payload.Channel)
            {
                case NoiseSourceChannel.Movement: bucket.MovementDb = payload.EmittedDb; break;
                case NoiseSourceChannel.Voice: bucket.VoiceDb = payload.EmittedDb; break;
                case NoiseSourceChannel.Radio: bucket.RadioDb = payload.EmittedDb; break;
                default:
                    bucket.OneShotDb += payload.EmittedDb;
                    bucket.OneShotOrigin = payload.Origin;
                    break;
            }

            if (payload.Channel != NoiseSourceChannel.OneShot) bucket.SustainedOrigin = payload.Origin;

            // 창 안의 최대치를 들고 간다 — 창보다 짧게 그친 외침·걸음이 끝점 표본에서 통째로 사라지지 않게 한다.
            bucket.PeakSustainedDb = Mathf.Max(bucket.PeakSustainedDb, bucket.SustainedDb);
            buckets[payload.SourceId] = bucket;

            // 미터는 창이 닫히기를 기다리지 않는다 — 버킷의 합은 매 순간 이미 확정값이라
            // 여기서 흘려도 창 종료 시점과 같은 수를 보여주면서 반응만 즉시가 된다.
            // 가청 임계 컷(Propagate) 이전이라는 계약도 그대로다 — 웅크림처럼 아무도 못 듣는 소음도 미터에는 뜬다(9-1 ①).
            meterSampleChannel.RaiseEvent(new NoiseMeterSampleEvent(payload.SourceId, bucket.TotalDb));
        }

        private void Update()
        {
            if (!IsServer || buckets.Count == 0) return;
            flushKeys.Clear();
            foreach (KeyValuePair<ulong, SourceBucket> pair in buckets)
                if (Time.time >= pair.Value.FlushAt) flushKeys.Add(pair.Key);

            for (int i = 0; i < flushKeys.Count; i++)
            {
                ulong sourceId = flushKeys[i];
                SourceBucket bucket = buckets[sourceId];

                // 지속 소음은 레벨이 그대로면 이벤트가 다시 오지 않는다 — 위치를 여기서 되읽지 않으면
                // 걷는 내내 출발 지점에서 소리가 나고 좀비가 빈 자리로 간다.
                // 발신자가 이미 사라졌다면 0 을 보낼 주체가 없으므로 서버가 대신 레벨을 내려 유령 소음원을 막는다.
                if (bucket.SustainedDb > 0f)
                {
                    if (NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(sourceId, out NetworkObject source))
                    {
                        bucket.SustainedOrigin = source.transform.position;
                    }
                    else
                    {
                        bucket.MovementDb = 0f;
                        bucket.VoiceDb = 0f;
                        bucket.RadioDb = 0f;
                    }
                }

                // 좀비 판정은 스펙대로 합산 창 주기를 지킨다 — 발행 빈도가 곧 성능이다(11장).
                // 지속분은 끝점이 아니라 창 안의 최대치를 쓴다 — 창보다 짧은 소음도 반드시 한 번은 전파된다.
                float sustainedDb = Mathf.Max(bucket.PeakSustainedDb, bucket.SustainedDb);
                Vector3 origin = sustainedDb > 0f ? bucket.SustainedOrigin : bucket.OneShotOrigin;
                Propagate(sourceId, origin, bucket.OneShotDb + sustainedDb);

                // 창이 닫히며 소진되는 것은 단발뿐이다. 지속 레벨은 발신자가 0 을 보낼 때까지 남는다(3-2).
                bucket.OneShotDb = 0f;
                bucket.PeakSustainedDb = bucket.SustainedDb; // 다음 창의 시작 피크는 지금 레벨이다
                if (bucket.TotalDb <= 0f)
                {
                    buckets.Remove(sourceId);
                    meterSampleChannel.RaiseEvent(new NoiseMeterSampleEvent(sourceId, 0f));
                    continue;
                }

                bucket.FlushAt = Time.time + settings.SumWindowSeconds;
                buckets[sourceId] = bucket;
                // 단발이 빠진 만큼 미터도 내려와야 한다 — 창이 닫히는 순간이 단발의 유일한 하강 지점이다.
                meterSampleChannel.RaiseEvent(new NoiseMeterSampleEvent(sourceId, bucket.TotalDb));
            }
        }

        private void Propagate(ulong sourceId, Vector3 origin, float emittedDb)
        {
            if (emittedDb < settings.LowestHearingThresholdDb) return;
            float radius = Mathf.Max(0.01f,
                (emittedDb - settings.LowestHearingThresholdDb) / settings.FalloffDbPerMeter);

            Collider[] hits = Physics.OverlapSphere(origin, radius, zombieMask, QueryTriggerInteraction.Collide);
            Log.D($"[NoisePropagationSystem] 전파 {emittedDb:F0}dB at {origin} 반경 {radius:F1}m — 후보 콜라이더 {hits.Length}");
            candidateIds.Clear();
            for (int i = 0; i < hits.Length; i++)
            {
                ZombieController existingZombie = hits[i].GetComponentInParent<ZombieController>();
                if (existingZombie != null && existingZombie.IsSpawned && existingZombie.IsServer)
                {
                    if (!candidateIds.Add(existingZombie.NetworkObjectId)) continue;
                    float reachedDb = CalculateReachedDb(origin, existingZombie.VisionOrigin.position, emittedDb);
                    if (existingZombie.Data == null || reachedDb < existingZombie.Data.HearMinDb)
                    {
                        // 못 들은 이유가 감쇠인지 데이터 미할당인지 구분되지 않으면 "좀비가 안 온다"를 추적할 방법이 없다.
                        Log.D($"[NoisePropagationSystem] 좀비 {existingZombie.NetworkObjectId} 미달 — 도달 {reachedDb:F1}dB, 하한 {(existingZombie.Data == null ? "Data 미할당" : existingZombie.Data.HearMinDb.ToString("F0"))}");
                        continue;
                    }

                    detectedChannel.RaiseEvent(new NoiseDetectedEvent(
                        existingZombie.NetworkObjectId, sourceId, origin, reachedDb));
                    Log.D($"[NoisePropagationSystem] 좀비 {existingZombie.NetworkObjectId} 감지 — 도달 {reachedDb:F1}dB, 지점 {origin}");
                    continue;
                }

            }
        }

        private float CalculateReachedDb(Vector3 origin, Vector3 headPosition, float emittedDb)
        {
            Vector3 delta = headPosition - origin;
            float distance = delta.magnitude;
            float attenuation = 0f;
            if (distance > Mathf.Epsilon)
            {
                RaycastHit[] hits = Physics.RaycastAll(origin, delta / distance, distance,
                    occlusionMask, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < hits.Length; i++)
                {
                    NoiseOcclusionSurface surface = hits[i].collider.GetComponentInParent<NoiseOcclusionSurface>();
                    if (surface == null || surface.Kind == NoiseOcclusionKind.Wall)
                        attenuation += settings.WallAttenuationDb;
                    else
                        attenuation += surface.IsOpen
                            ? settings.OpenDoorAttenuationDb
                            : settings.ClosedDoorAttenuationDb;
                }
            }
            return emittedDb - distance * settings.FalloffDbPerMeter - attenuation;
        }
    }
}
