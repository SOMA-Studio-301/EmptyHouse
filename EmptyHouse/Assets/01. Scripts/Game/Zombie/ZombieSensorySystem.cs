using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(ZombieController))]
public class ZombieSensorySystem : NetworkBehaviour
{
    [SerializeField] private ZombieController controller;
    [SerializeField] private NoiseEventChannelSO noiseEmittedChannel;
    [SerializeField] private LayerMask obstructionMask = ~0;
    [SerializeField] private bool environmentIsBright;

    private readonly List<TimedNoise> pendingNoises = new List<TimedNoise>();
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

    private void OnValidate()
    {
        if (controller == null) controller = GetComponent<ZombieController>();
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;
        if (noiseEmittedChannel == null)
        {
            Debug.LogError($"[{nameof(ZombieSensorySystem)}] Noise channel is not assigned on {name}.", this);
            return;
        }

        noiseEmittedChannel.OnEventRaised += OnNoiseEmitted;
    }

    public override void OnNetworkDespawn()
    {
        if (noiseEmittedChannel != null)
        {
            noiseEmittedChannel.OnEventRaised -= OnNoiseEmitted;
        }
    }

    private void OnNoiseEmitted(NoiseEvent noiseEvent)
    {
        if (!IsServer) return;
        pendingNoises.Add(new TimedNoise(noiseEvent, Time.time));
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

        Transform preferredTarget = ChooseNearestTarget(vision.Target, hearing.Target);
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
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null) return default;

        bool watcher = controller.Data.ZombieType == ZombieType.Watcher;
        bool flashlightBlinding = false;

        if (watcher)
        {
            foreach (NetworkClient client in networkManager.ConnectedClientsList)
            {
                if (client.PlayerObject == null) continue;
                PlayerSignals signals = ReadSignals(client.PlayerObject.gameObject);
                if (!signals.IsSpectator && signals.IsFlashlightAimingAtZombie)
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
        Transform nearestTarget = null;
        Vector3 nearestPosition = default;
        bool instantDetection = false;

        foreach (NetworkClient client in networkManager.ConnectedClientsList)
        {
            if (client.PlayerObject == null) continue;
            GameObject playerObject = client.PlayerObject.gameObject;
            PlayerSignals signals = ReadSignals(playerObject);
            if (signals.IsDisguised || signals.IsSpectator) continue;

            Vector3 targetPosition = GetPlayerEyePosition(playerObject);
            Vector3 toTarget = targetPosition - origin;
            float distance = toTarget.magnitude;
            if (distance <= Mathf.Epsilon || distance > controller.Data.VisionDistance) continue;

            Vector3 direction = toTarget / distance;
            float facingDot = Vector3.Dot(forward, direction);
            if (facingDot < cosThreshold) continue;
            if (!HasLineOfSight(origin, direction, distance, playerObject.transform)) continue;

            float gain = ComputeVisualGain(distance, facingDot, signals);
            if (gain > bestGain) bestGain = gain;

            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestTarget = playerObject.transform;
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
        if (pendingNoises.Count == 0) return default;

        float bestEffectiveDb = float.MinValue;
        NoiseEvent bestNoise = default;

        for (int i = 0; i < pendingNoises.Count; i++)
        {
            float effectiveDb = ComputeEffectiveNoiseDb(pendingNoises[i].Noise);
            if (effectiveDb > bestEffectiveDb)
            {
                bestEffectiveDb = effectiveDb;
                bestNoise = pendingNoises[i].Noise;
            }
        }

        if (bestEffectiveDb < controller.Data.HearMinDb) return default;

        Transform target = ResolvePlayerSource(bestNoise.Source);
        if (target != null)
        {
            PlayerSignals signals = ReadSignals(target.gameObject);
            if (signals.IsSpectator) target = null;
        }

        return new HearingResult(
            true,
            target != null,
            bestEffectiveDb,
            bestNoise.Origin,
            target);
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
            ZombieSoundOccluder occluder = hits[i].collider.GetComponent<ZombieSoundOccluder>();
            attenuation = occluder != null ? occluder.AttenuationDb : controller.Data.DefaultWallOcclusionDb;
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
    }

    private Transform ResolvePlayerSource(GameObject source)
    {
        if (source == null || NetworkManager.Singleton == null) return null;

        foreach (NetworkClient client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject == null) continue;
            Transform player = client.PlayerObject.transform;
            if (source.transform == player || source.transform.IsChildOf(player)) return player;
        }

        return null;
    }

    private PlayerSignals ReadSignals(GameObject playerObject)
    {
        MonoBehaviour[] behaviours = playerObject.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] is IZombiePerceptionSource source)
            {
                return new PlayerSignals(
                    source.IsDisguised,
                    source.IsSpectator,
                    source.IsCrouching,
                    source.IsMoving,
                    source.IsFlashlightAimingAt(controller.transform));
            }
        }

        Rigidbody body = playerObject.GetComponent<Rigidbody>();
        bool isMoving = body != null && body.linearVelocity.sqrMagnitude > 0.01f;
        return new PlayerSignals(false, false, false, isMoving, false);
    }

    private static Transform ChooseNearestTarget(Transform visualTarget, Transform auditoryTarget)
    {
        return visualTarget != null ? visualTarget : auditoryTarget;
    }

    private static Vector3 GetPlayerEyePosition(GameObject playerObject)
    {
        Camera camera = playerObject.GetComponentInChildren<Camera>(true);
        return camera != null ? camera.transform.position : playerObject.transform.position + Vector3.up * 1.5f;
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
        public readonly Transform Target;

        public VisionResult(bool hasStimulus, bool instantDetection, float gainPerSecond, Vector3 position, Transform target)
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
        public readonly Transform Target;

        public HearingResult(bool hasStimulus, bool hasTrackableStimulus, float effectiveDb, Vector3 position, Transform target)
        {
            HasStimulus = hasStimulus;
            HasTrackableStimulus = hasTrackableStimulus;
            EffectiveDb = effectiveDb;
            Position = position;
            Target = target;
        }
    }
}
