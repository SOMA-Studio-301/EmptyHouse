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
    [SerializeField] private PlayerDeathHandler deathHandler; // 같은 프리팹의 사망 권위 — IsSpectator 를 여기서 직접 읽는다
    [SerializeField] private DisguiseStateChangedEventChannelSO disguiseStateChanged;
    [SerializeField, Min(0f)] private float movingSpeedThreshold = 0.05f;
    [SerializeField, Range(0f, 90f)] private float flashlightAimTolerance = 10f;

    [Header("Server-owned perception flags")]
    private bool isDisguised;
    [SerializeField] private bool isCrouching;
    [SerializeField] private bool isFlashlightActive;

    private Vector3 previousPosition;
    private bool isMoving;
    private PlayerReturn playerReturn; // 같은 오브젝트의 귀환 권위 — Awake 캐시(자기완결, 인스펙터 배선 불필요)

    public Transform Root => transform;
    public Vector3 EyePosition => eyeAnchor != null ? eyeAnchor.position : transform.position + Vector3.up * 1.5f;
    public bool IsDisguised => isDisguised;
    // 사망·귀환 권위를 서버 메모리에서 직접 읽는다(push 아님) — 확정이 선 프레임부터 즉시 인지에서 제외된다(좀비AI E2).
    // 귀환도 관전이다(EH-94) — 귀환한 폰은 버스 자리에 투명하게 남으므로, 사망만 거르면 좀비가
    // 이 유령 폰을 보고 타겟으로 물어 귀환한 관전자에게 추격 BGM 이 깔리고 좀비도 빈자리를 공격한다.
    public bool IsSpectator => deathHandler.IsDead.Value || playerReturn.HasExtracted.Value;

    /// <summary>형제 PlayerReturn 참조를 캐시한다 — PlayerBodyVisibility 와 같은 자기완결 방식.</summary>
    private void Awake()
    {
        playerReturn = GetComponent<PlayerReturn>();
    }
    public bool IsCrouching => isCrouching;
    public bool IsMoving => isMoving;

    public override void OnNetworkSpawn()
    {
        previousPosition = transform.position;
        if (!IsServer) return;

        if (disguiseStateChanged == null)
        {
            Debug.LogError($"[{nameof(ZombiePerceptionSource)}] Disguise state channel is not assigned on {name}.", this);
            enabled = false;
            return;
        }

        disguiseStateChanged.OnEventRaised += HandleDisguiseStateChanged;

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
        if (disguiseStateChanged != null)
            disguiseStateChanged.OnEventRaised -= HandleDisguiseStateChanged;

        runtimeRegistry?.UnregisterPerceptionSource(this);
    }

    private void HandleDisguiseStateChanged(DisguiseStateChangedEvent evt)
    {
        if (!IsServer || evt.PlayerNetworkObjectId != NetworkObjectId) return;
        isDisguised = evt.IsDisguised;
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

    public void ServerSetCrouching(bool value) { if (IsServer) isCrouching = value; }
    public void ServerSetFlashlightActive(bool value) { if (IsServer) isFlashlightActive = value; }
}
