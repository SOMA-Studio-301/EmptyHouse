using Border.Core;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 플레이어의 체력을 소유하는 서버 권위 컴포넌트 (EH-97 — 좀비 타격 3대째 사망).
/// 좀비 타격(ZombiePlayerCaughtEventChannelSO)을 서버에서 수신해 남은 체력을 깎고,
/// 0이 되는 타격에서 <see cref="PlayerDeathHandler.Die"/> 를 호출한다 — 사망 이후 파이프라인(관전·드롭·로스터)은 기존 그대로다.
/// 마지막 피격 후 regenDelaySeconds 동안 무피격이면 regenIntervalSeconds 간격으로 체력이 1씩 자연 회복된다 (EH-117).
/// 체력에 전용 UI 는 없고, 소유 클라이언트가 체력 비율을 PlayerHealthChangedEventChannelSO 로 발행하면
/// 씬 HUD 의 UIDamageOverlay 가 화면 붉어짐으로 표현한다.
/// </summary>
public sealed class PlayerHealth : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerDeathHandler deathHandler; // 체력 소진 시 사망 확정을 위임할 기존 사망 권위자

    [Header("Broadcasting on")]
    [SerializeField] private PlayerHealthChangedEventChannelSO healthChanged; // 발행: 체력 비율(0~1). 오너만 발행하며 씬 레벨 HUD 가 구독한다

    [Header("Listening to")]
    [SerializeField] private ZombiePlayerCaughtEventChannelSO playerCaught; // 좀비 타격 수신 채널 — 서버에서만 구독

    [Header("Health")]
    [SerializeField, Min(1)] private int maxHits = 3; // 버틸 수 있는 타격 횟수. 이 횟수째 타격에 사망한다
    [SerializeField, Min(0f)] private float regenDelaySeconds = 10f; // 마지막 피격 후 회복이 시작되기까지의 대기 시간. 좀비 공격 주기(타격 락 5초+접근)보다 길어야 교전 중 회복이 못 따라온다 (EH-117)
    [SerializeField, Min(0.01f)] private float regenIntervalSeconds = 1f; // 회복 시작 후 체력 1씩 차오르는 간격

    // 서버 권위 남은 체력(타격 횟수 단위). 서버가 쓰고 전 클라가 읽는다 — 오너가 HUD 발행에 구독한다.
    private readonly NetworkVariable<int> remainingHits = new NetworkVariable<int>(
        3,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private double nextRegenTime = double.PositiveInfinity; // 서버 전용. 다음 회복 틱 시각 — 피격 시 regenDelaySeconds 뒤로 밀린다

    public int RemainingHits => remainingHits.Value;
    public float Health01 => maxHits <= 0 ? 0f : (float)remainingHits.Value / maxHits;

    /// <summary>서버는 체력을 초기화하고 좀비 타격 채널을 구독하며, 오너는 복제 변경을 HUD 채널로 중계하기 시작한다.</summary>
    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            remainingHits.Value = maxHits;
            playerCaught.OnEventRaised += HandlePlayerCaught;
        }

        if (IsOwner)
        {
            remainingHits.OnValueChanged += HandleRemainingHitsChanged;
            PublishHealth(remainingHits.Value);
        }
    }

    /// <summary>구독을 해제한다. 오너는 HUD 를 만땅으로 되돌려 다음 세션에 붉은 화면이 남지 않게 한다.</summary>
    public override void OnNetworkDespawn()
    {
        if (IsServer)
        {
            playerCaught.OnEventRaised -= HandlePlayerCaught;
        }

        if (IsOwner)
        {
            remainingHits.OnValueChanged -= HandleRemainingHitsChanged;
            PublishHealth(maxHits);
        }
    }

    /// <summary>
    /// 좀비 타격을 수신해 체력을 깎는다(서버 전용). 채널은 전원의 핸들러에 발화되므로
    /// 자기 NetworkObjectId 와 대조해 대상이 자신일 때만 처리한다 — 없으면 한 명이 잡힐 때 전원이 깎인다.
    /// </summary>
    /// <param name="payload">타격한 좀비와 잡힌 플레이어의 NetworkObjectId 쌍.</param>
    private void HandlePlayerCaught(ZombiePlayerCaughtEvent payload)
    {
        if (payload.PlayerNetworkObjectId != NetworkObjectId) return;
        if (deathHandler.IsDead.Value) return; // 이미 사망 확정이면 무시 — 시체를 더 깎지 않는다

        // 무적 시간 없음 — 좀비 여럿이 같은 순간 때리면 그만큼 다 깎인다(의도). 좀비별 연타는 타격 락(5초)이 막는다.
        double now = Time.timeAsDouble;
        nextRegenTime = now + regenDelaySeconds; // 피격마다 회복 시작 시점을 뒤로 민다

        remainingHits.Value = Mathf.Max(0, remainingHits.Value - 1);
        Log.D($"[PlayerHealth] Player {NetworkObjectId} hit by zombie={payload.ZombieNetworkObjectId}, remaining={remainingHits.Value}/{maxHits}, t={now:F2}");

        if (remainingHits.Value <= 0)
        {
            deathHandler.Die();
        }
    }

    /// <summary>
    /// 서버 전용 자연 회복 틱 (EH-117). 마지막 피격 후 regenDelaySeconds 가 지나면
    /// regenIntervalSeconds 간격으로 체력을 1씩 회복한다. 만땅이거나 사망 확정이면 아무것도 하지 않는다.
    /// </summary>
    private void Update()
    {
        if (!IsSpawned || !IsServer) return;
        if (deathHandler.IsDead.Value) return;
        if (remainingHits.Value >= maxHits) return;

        double now = Time.timeAsDouble;
        if (now < nextRegenTime) return;

        nextRegenTime = now + regenIntervalSeconds;
        remainingHits.Value = Mathf.Min(maxHits, remainingHits.Value + 1);
        Log.D($"[PlayerHealth] Player {NetworkObjectId} regenerated, remaining={remainingHits.Value}/{maxHits}, t={now:F2}");
    }

    /// <summary>복제된 체력 변경을 HUD 채널로 중계한다(오너 전용 구독이라 남의 체력은 여기로 오지 않는다).</summary>
    /// <param name="previous">직전 남은 타격 횟수.</param>
    /// <param name="current">변경된 남은 타격 횟수.</param>
    private void HandleRemainingHitsChanged(int previous, int current)
    {
        PublishHealth(current);
    }

    /// <summary>남은 타격 횟수를 0~1 로 정규화해 HUD 채널에 발행한다.</summary>
    /// <param name="hits">발행할 남은 타격 횟수(0~maxHits).</param>
    private void PublishHealth(int hits)
    {
        // maxHits 는 [Min(1)] 이지만, 0 으로 새면 나눗셈이 깨지므로 발행 전에 막는다.
        healthChanged.RaiseEvent(maxHits <= 0 ? 0f : (float)hits / maxHits);
    }
}
