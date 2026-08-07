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
                bucket.FlushAt = Time.time + settings.SumWindowSeconds;

            bucket.Origin = payload.Origin;
            bucket.Db += payload.EmittedDb;
            buckets[payload.SourceId] = bucket;
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
