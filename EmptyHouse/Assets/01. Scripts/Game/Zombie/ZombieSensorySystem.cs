using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class ZombieSensorySystem : NetworkBehaviour
{
    [SerializeField] private ZombieController controller;
    [SerializeField] private ZombieRuntimeRegistrySO runtimeRegistry;
    [SerializeField] private NoiseEventChannelSO noiseEmittedChannel;
    [SerializeField] private EmptyHouse.NoiseSystem.NoiseDetectedEventChannelSO noiseDetectedChannel;
    [SerializeField] private LayerMask obstructionMask = ~0;
    [SerializeField] private bool environmentIsBright;

    private readonly List<TimedNoise> pendingNoises = new List<TimedNoise>();
    private readonly List<TimedDetectedNoise> pendingDetectedNoises = new List<TimedDetectedNoise>();
    private float watcherBlindUntil;

    private readonly struct TimedNoise
    {
        public readonly NoiseEvent Noise;
        public readonly float ReceivedAt;

        public TimedNoise(NoiseEvent noise, float receivedAt)
        {
            Noise = noise;
            ReceivedAt = receivedAt;
        }
    }

    private readonly struct TimedDetectedNoise
    {
        public readonly EmptyHouse.NoiseSystem.NoiseDetectedEvent Noise;
        public readonly float ReceivedAt;

        public TimedDetectedNoise(EmptyHouse.NoiseSystem.NoiseDetectedEvent noise, float receivedAt)
        {
            Noise = noise;
            ReceivedAt = receivedAt;
        }
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;
        if (controller == null || runtimeRegistry == null ||
            (noiseDetectedChannel == null && noiseEmittedChannel == null))
        {
            Debug.LogError($"[{nameof(ZombieSensorySystem)}] Explicit references are incomplete on {name}.", this);
            enabled = false;
            return;
        }

        // Production uses the propagated, target-specific channel. The legacy
        // channel remains a fallback for scenes that have not been migrated yet.
        if (noiseDetectedChannel != null)
        {
            noiseDetectedChannel.OnEventRaised += OnNoiseDetected;
        }
        else
        {
            noiseEmittedChannel.OnEventRaised += OnNoiseEmitted;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (noiseDetectedChannel != null)
        {
            noiseDetectedChannel.OnEventRaised -= OnNoiseDetected;
        }
        else if (noiseEmittedChannel != null)
        {
            noiseEmittedChannel.OnEventRaised -= OnNoiseEmitted;
        }
    }

    private void OnNoiseEmitted(NoiseEvent noiseEvent)
    {
        if (!IsServer) return;
        pendingNoises.Add(new TimedNoise(noiseEvent, Time.time));
    }

    private void OnNoiseDetected(EmptyHouse.NoiseSystem.NoiseDetectedEvent noiseEvent)
    {
        if (!IsServer || noiseEvent.TargetZombieId != NetworkObjectId) return;
        pendingDetectedNoises.Add(new TimedDetectedNoise(noiseEvent, Time.time));
    }

    public ZombiePerceptionFrame ServerCollectPerception()
    {
        if (!IsServer || controller == null || controller.Data == null)
        {
            return default;
        }

        PruneExpiredNoises();
        VisionResult vision = EvaluateVision();
        HearingResult hearing = EvaluateHearing();

        IZombiePerceptionSource preferredTarget = ChoosePreferredTarget(vision.Target, hearing.Target);
        bool trackingStimulus = vision.HasStimulus || hearing.HasTrackableStimulus;

        return new ZombiePerceptionFrame(
            vision.HasStimulus,
            hearing.HasStimulus,
            vision.InstantDetection,
            trackingStimulus,
            vision.GainPerSecond,
            hearing.EffectiveDb,
            vision.Position,
            hearing.Position,
            preferredTarget);
    }

    private VisionResult EvaluateVision()
    {
        if (runtimeRegistry == null) return default;

        bool watcher = controller.Data.ZombieType == ZombieType.Watcher;
        bool flashlightBlinding = false;

        if (watcher)
        {
            IReadOnlyList<IZombiePerceptionSource> sources = runtimeRegistry.PerceptionSources;
            for (int i = 0; i < sources.Count; i++)
            {
                IZombiePerceptionSource source = sources[i];
                if (source == null || source.Root == null || source.IsSpectator) continue;
                if (source.IsFlashlightAimingAt(controller.transform))
                {
                    flashlightBlinding = true;
                    break;
                }
            }

            if (environmentIsBright || flashlightBlinding)
            {
                watcherBlindUntil = Time.time + controller.Data.WatcherBlindRecoverySeconds;
            }

            if (Time.time < watcherBlindUntil) return default;
        }

        Vector3 origin = controller.VisionOrigin.position;
        Vector3 forward = controller.VisionOrigin.forward;
        float cosThreshold = Mathf.Cos(controller.Data.VisionAngle * 0.5f * Mathf.Deg2Rad);
        float bestGain = 0f;
        float nearestDistance = float.MaxValue;
        IZombiePerceptionSource nearestTarget = null;
        Vector3 nearestPosition = default;
        bool instantDetection = false;

        IReadOnlyList<IZombiePerceptionSource> perceptionSources = runtimeRegistry.PerceptionSources;
        for (int i = 0; i < perceptionSources.Count; i++)
        {
            IZombiePerceptionSource source = perceptionSources[i];
            if (source == null || source.Root == null) continue;
            PlayerSignals signals = ReadSignals(source);
            if (signals.IsDisguised || signals.IsSpectator) continue;

            Vector3 targetPosition = source.EyePosition;
            Vector3 toTarget = targetPosition - origin;
            float distance = toTarget.magnitude;
            if (distance <= Mathf.Epsilon || distance > controller.Data.VisionDistance) continue;

            Vector3 direction = toTarget / distance;
            float facingDot = Vector3.Dot(forward, direction);
            if (facingDot < cosThreshold) continue;
            if (!HasLineOfSight(origin, direction, distance, source.Root)) continue;

            float gain = ComputeVisualGain(distance, facingDot, signals);
            if (gain > bestGain) bestGain = gain;

            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestTarget = source;
                nearestPosition = targetPosition;
            }

            if (distance <= controller.Data.VisInstantRange)
            {
                instantDetection = true;
            }
        }

        return new VisionResult(
            nearestTarget != null,
            instantDetection,
            bestGain,
            nearestPosition,
            nearestTarget);
    }

    private HearingResult EvaluateHearing()
    {
        if (pendingNoises.Count == 0 && pendingDetectedNoises.Count == 0) return default;

        float bestEffectiveDb = float.MinValue;
        NoiseEvent bestNoise = default;
        EmptyHouse.NoiseSystem.NoiseDetectedEvent bestDetectedNoise = default;
        bool usesDetectedNoise = false;

        for (int i = 0; i < pendingNoises.Count; i++)
        {
            float effectiveDb = ComputeEffectiveNoiseDb(pendingNoises[i].Noise);
            if (effectiveDb > bestEffectiveDb)
            {
                bestEffectiveDb = effectiveDb;
                bestNoise = pendingNoises[i].Noise;
                usesDetectedNoise = false;
            }
        }

        for (int i = 0; i < pendingDetectedNoises.Count; i++)
        {
            EmptyHouse.NoiseSystem.NoiseDetectedEvent detected = pendingDetectedNoises[i].Noise;
            if (detected.ReachedDb <= bestEffectiveDb) continue;
            bestEffectiveDb = detected.ReachedDb;
            bestDetectedNoise = detected;
            usesDetectedNoise = true;
        }

        if (bestEffectiveDb < controller.Data.HearMinDb) return default;

        IZombiePerceptionSource target = usesDetectedNoise
            ? ResolveNetworkSource(bestDetectedNoise.SourceId)
            : ResolvePlayerSource(bestNoise.Source);

        // 사망(관전)·위장 대상은 추적 가능한 타겟이 아니다. 소리 자체는 남으므로
        // 좀비는 위치 조사만 하고 발각(latch)으로는 넘어가지 않는다(좀비AI E8).
        if (target != null && (target.Root == null || target.IsSpectator || target.IsDisguised))
        {
            target = null;
        }

        return new HearingResult(
            true,
            target != null,
            bestEffectiveDb,
            usesDetectedNoise ? bestDetectedNoise.Origin : bestNoise.Origin,
            target);
    }

    /// <summary>소음원 NetworkObjectId 를 지각 소스로 해석한다. 플레이어가 아닌 소음원(문·투척물 등)은 null 이다.</summary>
    private IZombiePerceptionSource ResolveNetworkSource(ulong sourceId)
    {
        if (runtimeRegistry == null) return null;

        IReadOnlyList<IZombiePerceptionSource> sources = runtimeRegistry.PerceptionSources;
        for (int i = 0; i < sources.Count; i++)
        {
            if (sources[i] != null && sources[i].NetworkObjectId == sourceId) return sources[i];
        }

        return null;
    }

    private float ComputeEffectiveNoiseDb(NoiseEvent noiseEvent)
    {
        Vector3 origin = controller.VisionOrigin.position;
        Vector3 delta = noiseEvent.Origin - origin;
        float distance = delta.magnitude;
        float effectiveDb = noiseEvent.Decibel - distance * controller.Data.SoundFalloffDbPerMeter;
        if (distance <= Mathf.Epsilon) return effectiveDb;

        RaycastHit[] hits = Physics.RaycastAll(origin, delta / distance, distance, obstructionMask, QueryTriggerInteraction.Ignore);
        float nearestBlocker = float.MaxValue;
        float attenuation = 0f;

        for (int i = 0; i < hits.Length; i++)
        {
            Transform hitTransform = hits[i].transform;
            if (hitTransform == null || hitTransform.IsChildOf(controller.transform)) continue;
            if (noiseEvent.Source != null && (hitTransform == noiseEvent.Source.transform || hitTransform.IsChildOf(noiseEvent.Source.transform))) continue;
            if (hits[i].distance >= nearestBlocker) continue;

            nearestBlocker = hits[i].distance;
            attenuation = runtimeRegistry != null
                && runtimeRegistry.TryGetSoundAttenuation(hits[i].collider, out float registeredAttenuation)
                    ? registeredAttenuation
                    : controller.Data.DefaultWallOcclusionDb;
        }

        return effectiveDb - attenuation;
    }

    private bool HasLineOfSight(Vector3 origin, Vector3 direction, float distance, Transform playerRoot)
    {
        RaycastHit[] hits = Physics.RaycastAll(origin, direction, distance, obstructionMask, QueryTriggerInteraction.Ignore);
        float nearestPlayerHit = float.MaxValue;
        float nearestObstacleHit = float.MaxValue;

        for (int i = 0; i < hits.Length; i++)
        {
            Transform hitTransform = hits[i].transform;
            if (hitTransform == null || hitTransform.IsChildOf(controller.transform)) continue;

            if (hitTransform == playerRoot || hitTransform.IsChildOf(playerRoot))
            {
                nearestPlayerHit = Mathf.Min(nearestPlayerHit, hits[i].distance);
            }
            else
            {
                nearestObstacleHit = Mathf.Min(nearestObstacleHit, hits[i].distance);
            }
        }

        return nearestObstacleHit >= nearestPlayerHit || nearestObstacleHit == float.MaxValue;
    }

    private float ComputeVisualGain(float distance, float facingDot, PlayerSignals signals)
    {
        float nearThreshold = controller.Data.VisionDistance * 0.25f;
        float distanceFactor = distance <= nearThreshold
            ? controller.Data.VisDistNear
            : Mathf.Lerp(controller.Data.VisDistNear, controller.Data.VisDistFar,
                Mathf.InverseLerp(nearThreshold, controller.Data.VisionDistance, distance));

        float frontFactor = facingDot >= Mathf.Cos(20f * Mathf.Deg2Rad) ? controller.Data.VisFront : 1f;
        float lightFactor = signals.IsFlashlightAimingAtZombie
            ? controller.Data.VisLightFlashlight
            : environmentIsBright ? controller.Data.VisLightBright : controller.Data.VisLightDark;
        float poseFactor = !signals.IsMoving
            ? controller.Data.VisPoseIdle
            : signals.IsCrouching ? controller.Data.VisPoseCrouch : controller.Data.VisPoseWalk;

        return controller.Data.VisGainBase * distanceFactor * frontFactor * lightFactor * poseFactor;
    }

    private void PruneExpiredNoises()
    {
        float cutoff = Time.time - controller.Data.HearingStimulusSeconds;
        for (int i = pendingNoises.Count - 1; i >= 0; i--)
        {
            if (pendingNoises[i].ReceivedAt < cutoff) pendingNoises.RemoveAt(i);
        }
        for (int i = pendingDetectedNoises.Count - 1; i >= 0; i--)
        {
            if (pendingDetectedNoises[i].ReceivedAt < cutoff) pendingDetectedNoises.RemoveAt(i);
        }
    }

    private IZombiePerceptionSource ResolvePlayerSource(GameObject source)
    {
        if (source == null || runtimeRegistry == null) return null;

        IReadOnlyList<IZombiePerceptionSource> sources = runtimeRegistry.PerceptionSources;
        for (int i = 0; i < sources.Count; i++)
        {
            Transform player = sources[i]?.Root;
            if (player == null) continue;
            if (source.transform == player || source.transform.IsChildOf(player)) return sources[i];
        }

        return null;
    }

    private PlayerSignals ReadSignals(IZombiePerceptionSource source)
    {
        return new PlayerSignals(
            source.IsDisguised,
            source.IsSpectator,
            source.IsCrouching,
            source.IsMoving,
            source.IsFlashlightAimingAt(controller.transform));
    }

    private static IZombiePerceptionSource ChoosePreferredTarget(
        IZombiePerceptionSource visualTarget,
        IZombiePerceptionSource auditoryTarget)
    {
        return visualTarget ?? auditoryTarget;
    }

    private readonly struct PlayerSignals
    {
        public readonly bool IsDisguised;
        public readonly bool IsSpectator;
        public readonly bool IsCrouching;
        public readonly bool IsMoving;
        public readonly bool IsFlashlightAimingAtZombie;

        public PlayerSignals(bool isDisguised, bool isSpectator, bool isCrouching, bool isMoving, bool isFlashlightAimingAtZombie)
        {
            IsDisguised = isDisguised;
            IsSpectator = isSpectator;
            IsCrouching = isCrouching;
            IsMoving = isMoving;
            IsFlashlightAimingAtZombie = isFlashlightAimingAtZombie;
        }
    }

    private readonly struct VisionResult
    {
        public readonly bool HasStimulus;
        public readonly bool InstantDetection;
        public readonly float GainPerSecond;
        public readonly Vector3 Position;
        public readonly IZombiePerceptionSource Target;

        public VisionResult(bool hasStimulus, bool instantDetection, float gainPerSecond, Vector3 position, IZombiePerceptionSource target)
        {
            HasStimulus = hasStimulus;
            InstantDetection = instantDetection;
            GainPerSecond = gainPerSecond;
            Position = position;
            Target = target;
        }
    }

    private readonly struct HearingResult
    {
        public readonly bool HasStimulus;
        public readonly bool HasTrackableStimulus;
        public readonly float EffectiveDb;
        public readonly Vector3 Position;
        public readonly IZombiePerceptionSource Target;

        public HearingResult(bool hasStimulus, bool hasTrackableStimulus, float effectiveDb, Vector3 position, IZombiePerceptionSource target)
        {
            HasStimulus = hasStimulus;
            HasTrackableStimulus = hasTrackableStimulus;
            EffectiveDb = effectiveDb;
            Position = position;
            Target = target;
        }
    }
}
