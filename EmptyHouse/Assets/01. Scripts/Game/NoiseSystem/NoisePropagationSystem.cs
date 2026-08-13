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

        private struct SourceBucket
        {
            public Vector3 Origin;
            public float Db;
            public float FlushAt;
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

        private void OnNoiseEmitted(NoiseEmittedEvent payload)
        {
            if (!IsServer || payload.EmittedDb <= 0f) return;
            if (!buckets.TryGetValue(payload.SourceId, out SourceBucket bucket))
            {
                bucket.FlushAt = Time.time + settings.SumWindowSeconds;
                bucket.Db = payload.EmittedDb;
            }
            else
            {
                bucket.Db = CombineDb(bucket.Db, payload.EmittedDb);
            }

            bucket.Origin = payload.Origin;
            buckets[payload.SourceId] = bucket;
        }

        /// <summary>
        /// 같은 창에 겹친 두 소음을 에너지 영역에서 합쳐 dB 로 되돌린다. dB 를 선형으로 더하면
        /// 걷기(20)+말하기(30)가 50dB 가 되어 전파 반경이 두 배로 부풀지만, 에너지 합은 30.4dB 다 —
        /// 작은 소리는 큰 소리에 묻힌다는 실제 음향 특성 그대로다. 같은 크기 둘이 겹치면 +3dB 만 커진다.
        /// </summary>
        /// <param name="dbA">겹친 한쪽 dB.</param>
        /// <param name="dbB">겹친 다른쪽 dB.</param>
        /// <returns>에너지 합산된 dB.</returns>
        private static float CombineDb(float dbA, float dbB)
        {
            return 10f * Mathf.Log10(
                Mathf.Pow(10f, dbA / 10f) + Mathf.Pow(10f, dbB / 10f));
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
                buckets.Remove(sourceId);
                // 미터는 Propagate 의 가청 임계 컷(LowestHearingThresholdDb) 이전에 발행한다 —
                // 뒤에 두면 웅크림(8dB)처럼 아무도 못 듣는 소음이 미터에 0 으로 뜬다 (소음시스템.md 9-1 ①)
                meterSampleChannel.RaiseEvent(new NoiseMeterSampleEvent(sourceId, bucket.Db));
                Propagate(sourceId, bucket.Origin, bucket.Db);
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
