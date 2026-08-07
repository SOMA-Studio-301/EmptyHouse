using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 복제된 좀비 상태에 좀비 발성(신음·으르렁·포효)을 편승시킨다 — 오브젝트 사운드다.
/// ZombieAnimator 와 같은 자리다: 상태는 서버가 쓰고 전 클라이언트에 복제되므로 IsServer/IsOwner 로 가르지 않는다.
/// 각 클라가 자기 인스턴스의 소리를 울리고, 누구에게 들리는지는 3D 감쇠가 정한다.
///
/// 주기 발성(신음·으르렁)의 시점은 로컬 난수가 아니라 서버 시간 격자에서 끌어낸다 —
/// 클라마다 제 난수로 굴리면 같은 좀비가 클라마다 다른 순간에 울어, 소리로 위치를 나누는 협동이 어긋난다.
/// 발소리(PlayerFootstepSfx)가 복제된 위치에서 시점을 끌어내는 것과 같은 이유다.
///
/// 이 발성은 연출 전용이라 NoiseEventChannelSO 로 소음을 내지 않는다 — 좀비 지각의 자극원은 플레이어뿐이다.
/// </summary>
public class ZombieSfx : NetworkBehaviour
{
    [Header("Refs")]
    [SerializeField] private ZombieController controller;

    [Header("Audio")]
    [SerializeField] private SFXEventChannelSO sfxEventChannel; // 프로젝트 에셋(SO) — 자기완결 프리팹의 허용 의존
    [SerializeField] private AudioId roarAudioId = AudioId.Sfx_Zombie_Roar;   // 포효 전이 진입 — 발각을 알리는 단발
    [SerializeField] private AudioId attackAudioId = AudioId.Sfx_Zombie_Roar; // 타격 진입 — 전용 음원이 등재되기 전까지 포효를 재사용한다
    [SerializeField] private AudioId[] idleAudioIds = { AudioId.Sfx_Zombie_Idle }; // 평상시 신음 후보
    [SerializeField] private AudioId[] chaseAudioIds = // 추격 으르렁 후보
    {
        AudioId.Sfx_Zombie_Chase0,
        AudioId.Sfx_Zombie_Chase1,
        AudioId.Sfx_Zombie_Chase2,
    };

    [Header("Vocalization")]
    [SerializeField, Min(0.5f)] private float idleIntervalSeconds = 9f;  // 평상시 발성 간격(초)
    [SerializeField, Min(0.5f)] private float chaseIntervalSeconds = 4f; // 추격 발성 간격(초)

    private const int NoSlot = int.MinValue; // 아직 격자에 자리를 잡지 않았다

    private int lastVocalizedSlot = NoSlot;

    /// <summary>
    /// 상태 전이 발성을 구독한다. ZombieAnimator 와 달리 스폰 시점의 현재 상태는 울리지 않는다 —
    /// 늦게 합류한 클라에게 이미 지나간 포효가 난데없이 들린다(DoorInteractable 의 "늦은 합류 동기화는 연출 무음"과 같다).
    /// </summary>
    public override void OnNetworkSpawn()
    {
        controller.StateChanged += HandleStateChanged;
    }

    /// <summary>상태 전이 구독을 해제한다.</summary>
    public override void OnNetworkDespawn()
    {
        controller.StateChanged -= HandleStateChanged;
    }

    /// <summary>전이 단발 — 포효·타격은 그 상태에 들어선 순간 한 번만 운다.</summary>
    /// <param name="kind">복제된 새 좀비 상태.</param>
    private void HandleStateChanged(ZombieStateKind kind)
    {
        if (kind == ZombieStateKind.RoarTransition)
        {
            sfxEventChannel.RaisePlayEvent(roarAudioId, transform.position);
            return;
        }

        if (kind == ZombieStateKind.Attack)
        {
            sfxEventChannel.RaisePlayEvent(attackAudioId, transform.position);
        }
    }

    /// <summary>주기 발성 — 서버 시간 격자의 칸이 바뀌는 순간 1회 울린다.</summary>
    private void Update()
    {
        // 매 프레임 호출되므로 진입 트레이스를 두지 않는다.
        if (!IsSpawned) return;

        ZombieStateKind kind = controller.CurrentState;
        AudioId[] pool = VocalizationPool(kind);
        if (pool == null || pool.Length == 0) return;

        int slot = VocalizationSlot(kind == ZombieStateKind.Chase ? chaseIntervalSeconds : idleIntervalSeconds);
        if (slot == lastVocalizedSlot) return;

        bool firstSlot = lastVocalizedSlot == NoSlot;
        lastVocalizedSlot = slot;

        // 스폰 직후 첫 칸은 경계로 치지 않는다 — 그러면 같이 스폰된 좀비가 전부 첫 프레임에 합창한다.
        if (firstSlot) return;

        sfxEventChannel.RaisePlayEvent(PickVariant(pool, slot), transform.position);
    }

    /// <summary>상태별 주기 발성 후보. 포효·타격은 전이 단발이 이미 울리므로 겹쳐 울지 않는다.</summary>
    /// <param name="kind">현재 좀비 상태.</param>
    /// <returns>재생 후보 목록. 주기 발성이 없는 상태면 null.</returns>
    private AudioId[] VocalizationPool(ZombieStateKind kind)
    {
        switch (kind)
        {
            case ZombieStateKind.Chase:
                return chaseAudioIds;
            case ZombieStateKind.RoarTransition:
            case ZombieStateKind.Attack:
                return null;
            default:
                return idleAudioIds; // Wander·Alert·Investigate·Subside
        }
    }

    /// <summary>
    /// 서버 시간을 간격으로 나눈 격자 칸 번호. 개체마다 위상을 어긋나게 둔다 —
    /// 같은 격자를 그대로 쓰면 무리 좀비(ZombieCrowd)가 한 순간에 합창한다.
    /// </summary>
    /// <param name="interval">발성 간격(초).</param>
    /// <returns>현재 칸 번호.</returns>
    private int VocalizationSlot(float interval)
    {
        double phase = NetworkObjectId % 16UL / 16d * interval;
        return (int)((NetworkManager.ServerTime.Time + phase) / interval);
    }

    /// <summary>칸 번호와 개체 id 로 변종을 고른다. 로컬 난수로 고르면 같은 울음이 클라마다 다른 소리로 들린다.</summary>
    /// <param name="pool">재생 후보 목록.</param>
    /// <param name="slot">현재 칸 번호.</param>
    /// <returns>이 칸에 울릴 사운드 식별자.</returns>
    private AudioId PickVariant(AudioId[] pool, int slot)
    {
        if (pool.Length == 1) return pool[0];

        uint hash = (uint)(NetworkObjectId * 2654435761UL + (uint)slot);
        hash ^= hash >> 15;
        hash *= 2246822519u;
        hash ^= hash >> 13;
        return pool[hash % (uint)pool.Length];
    }
}
