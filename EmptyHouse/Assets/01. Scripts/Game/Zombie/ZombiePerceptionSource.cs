using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Explicit player-to-zombie perception adapter. It registers once in the SO runtime set,
/// replacing per-zombie hierarchy scans for interfaces, rigidbodies, cameras, and NetworkObjects.
/// </summary>
public sealed class ZombiePerceptionSource : NetworkBehaviour, IZombiePerceptionSource
{
    [SerializeField] private ZombieRuntimeRegistrySO runtimeRegistry;
    [SerializeField] private Transform eyeAnchor;
    [SerializeField] private Transform flashlightAimAnchor;
    [SerializeField, Min(0f)] private float movingSpeedThreshold = 0.05f;
    [SerializeField, Range(0f, 90f)] private float flashlightAimTolerance = 10f;

    [Header("Server-owned perception flags")]
    [SerializeField] private bool isDisguised;
    [SerializeField] private bool isSpectator;
    [SerializeField] private bool isCrouching;
    [SerializeField] private bool isFlashlightActive;

    private Vector3 previousPosition;
    private bool isMoving;

    public Transform Root => transform;
    public Vector3 EyePosition => eyeAnchor != null ? eyeAnchor.position : transform.position + Vector3.up * 1.5f;
    public bool IsDisguised => isDisguised;
    public bool IsSpectator => isSpectator;
    public bool IsCrouching => isCrouching;
    public bool IsMoving => isMoving;

    public override void OnNetworkSpawn()
    {
        previousPosition = transform.position;
        if (!IsServer) return;

        if (runtimeRegistry == null)
        {
            Debug.LogError($"[{nameof(ZombiePerceptionSource)}] Runtime registry is not assigned on {name}.", this);
            enabled = false;
            return;
        }

        runtimeRegistry.RegisterPerceptionSource(this);
    }

    public override void OnNetworkDespawn()
    {
        runtimeRegistry?.UnregisterPerceptionSource(this);
    }

    private void FixedUpdate()
    {
        if (!IsServer) return;

        float speed = Vector3.Distance(transform.position, previousPosition) / Mathf.Max(Time.fixedDeltaTime, 0.0001f);
        isMoving = speed >= movingSpeedThreshold;
        previousPosition = transform.position;
    }

    public bool IsFlashlightAimingAt(Transform zombie)
    {
        if (!isFlashlightActive || flashlightAimAnchor == null || zombie == null) return false;

        Vector3 direction = zombie.position - flashlightAimAnchor.position;
        if (direction.sqrMagnitude <= Mathf.Epsilon) return true;
        return Vector3.Angle(flashlightAimAnchor.forward, direction) <= flashlightAimTolerance;
    }

    public void ServerSetDisguised(bool value) { if (IsServer) isDisguised = value; }
    public void ServerSetSpectator(bool value) { if (IsServer) isSpectator = value; }
    public void ServerSetCrouching(bool value) { if (IsServer) isCrouching = value; }
    public void ServerSetFlashlightActive(bool value) { if (IsServer) isFlashlightActive = value; }
}
