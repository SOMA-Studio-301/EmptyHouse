using Border.Core;
using EmptyHouse.NoiseSystem;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 던져진 투척물의 비행·착지·소음. 물리는 서버에서만 돌고 위치는 NetworkTransform 이 복제한다 —
/// 각 클라가 따로 시뮬레이션하면 착지 지점이 사람마다 달라져, 좀비가 모이는 곳과
/// 화면에서 병이 깨진 곳이 어긋난다.
///
/// 착지 소음의 SourceId 는 반드시 이 투척물 자신의 NetworkObjectId 다. 던진 사람의 ID 를 쓰면
/// <see cref="ZombieSensorySystem"/> 의 ResolveNetworkSource 가 그를 지각 소스로 찾아내 타겟으로 물어버려,
/// 유인이 아니라 자기 위치를 알리는 꼴이 된다. 플레이어가 아닌 SourceId 는 null 로 해석되므로
/// 좀비는 발각(latch) 없이 착지 지점 조사만 하게 된다 — 그게 투척의 목적이다.
///
/// 조준 궤적(<see cref="ThrowAimIndicator"/>)이 예측한 곡선과 실제 낙하가 일치하려면
/// 이 프리팹의 Rigidbody 는 linearDamping = 0 · useGravity = true 여야 한다.
/// </summary>
[RequireComponent(typeof(Rigidbody), typeof(NetworkObject))]
public class ThrownProjectile : NetworkBehaviour
{
    [Header("Noise")]
    [Tooltip("착지 시 발행할 소음(dB) ⚪. 현재 전파 설정에서 45dB 면 개활지 유효 반경 약 22m, 벽 하나당 -20dB")]
    [SerializeField] private float impactNoiseDb = 45f;

    [Tooltip("서버 소음 파이프라인. NoisePropagationSystem 이 구독하는 것과 같은 에셋을 물린다")]
    [SerializeField] private NoiseEmittedEventChannelSO emittedChannel;

    [Header("Impact")]
    [Tooltip("착지로 칠 레이어. Ground · Wall · Door 를 선택한다 — 좀비나 플레이어 몸에 맞은 건 착지가 아니다")]
    [SerializeField] private LayerMask impactMask;

    [Tooltip("착지 후 소멸까지의 유예(초). 0 이면 원격 클라에서 보간이 따라오기 전에 사라져 벽 앞에서 증발한 것처럼 보인다")]
    [SerializeField] private float despawnDelaySeconds = 0.15f;

    [Tooltip("아무것도 맞히지 못했을 때 강제 소멸까지의 시간(초). 맵 밖으로 떨어진 투척물이 영원히 남지 않게 한다")]
    [SerializeField] private float maxLifeSeconds = 15f;

    [Header("Audio")]
    [SerializeField] private SFXEventChannelSO sfxEventChannel;
    [SerializeField] private AudioId impactAudioId = AudioId.Sfx_Item_Drop; // ⚪ 전용 착지음이 생기면 교체한다

    private Rigidbody body;

    // 착지 판정은 한 번뿐이다 — 튕기면서 두 번째 접촉이 들어와도 소음을 다시 내지 않는다.
    private bool hasLanded;

    // 착지 후 소멸까지 남은 시간(초). hasLanded 인 동안에만 의미가 있다.
    private float despawnTimer;

    // 스폰 후 경과 시간(초). maxLifeSeconds 초과 시 소음 없이 정리한다.
    private float aliveSeconds;

    /// <summary>Rigidbody 참조를 캐시한다.</summary>
    private void Awake()
    {
        body = GetComponent<Rigidbody>();
    }

    /// <summary>
    /// 비서버 인스턴스의 물리를 잠근다. 진실은 서버에만 있고 위치는 NetworkTransform 이 밀어 주므로,
    /// 클라의 Rigidbody 를 살려 두면 복제 좌표와 로컬 시뮬레이션이 서로를 밀어내며 떨린다.
    /// </summary>
    public override void OnNetworkSpawn()
    {
        if (!IsServer)
        {
            body.isKinematic = true;
        }
    }

    /// <summary>
    /// 서버에서 초속도를 실어 발사한다. <see cref="PlayerThrower"/> 가 Spawn 직후 호출한다.
    ///
    /// 던진 사람과의 충돌은 꺼 둔다 — 플레이어도 투척체도 Default 레이어라, 발사 지점이 캡슐 안에
    /// 걸리는 순간(웅크림·벽에 붙어 던지기) 자기 몸에 튕겨 발밑에 떨어진다. 레이어를 나누는 대신
    /// 이 쌍만 무시하는 이유는, 던진 뒤 굴러온 병이 다른 플레이어에게는 정상적으로 부딪혀야 하기 때문이다.
    /// </summary>
    /// <param name="velocity">발사 초속도(월드).</param>
    /// <param name="thrower">던진 플레이어의 루트. 이 오브젝트의 콜라이더와는 충돌하지 않는다.</param>
    public void ServerLaunch(Vector3 velocity, GameObject thrower)
    {
        if (!IsServer) return;

        if (thrower != null)
        {
            Collider self = GetComponent<Collider>();
            Collider[] throwerColliders = thrower.GetComponentsInChildren<Collider>();
            for (int i = 0; i < throwerColliders.Length; i++)
            {
                Physics.IgnoreCollision(self, throwerColliders[i], true);
            }
        }

        body.linearVelocity = velocity;
    }

    /// <summary>착지 유예와 수명을 서버에서만 센다.</summary>
    private void Update()
    {
        if (!IsServer || !IsSpawned) return;

        if (hasLanded)
        {
            despawnTimer -= Time.deltaTime;
            if (despawnTimer <= 0f) NetworkObject.Despawn();
            return;
        }

        aliveSeconds += Time.deltaTime;
        if (aliveSeconds < maxLifeSeconds) return;

        // 어디에도 닿지 못한 투척물 — 맵 밖으로 떨어졌을 가능성이 크다. 소음 없이 조용히 정리한다.
        Log.W($"[ThrownProjectile] {name} 이 {maxLifeSeconds}초 동안 착지하지 못해 소멸시킨다.");
        NetworkObject.Despawn();
    }

    /// <summary>
    /// 착지를 판정해 소음을 발행하고 소멸을 예약한다. 물리가 서버에서만 돌므로 이 콜백도 서버에서만 유효하다.
    /// </summary>
    /// <param name="collision">충돌 정보.</param>
    private void OnCollisionEnter(Collision collision)
    {
        if (!IsServer || !IsSpawned || hasLanded) return;

        // 좀비 몸에 맞고 튕긴 것은 착지가 아니다 — 그대로 굴러 바닥에 닿을 때 소음이 난다.
        if ((impactMask.value & (1 << collision.gameObject.layer)) == 0) return;

        hasLanded = true;
        despawnTimer = despawnDelaySeconds;

        Vector3 point = collision.GetContact(0).point;

        // SourceId 는 이 투척물 자신이다. 던진 사람 ID 를 넘기면 좀비가 그를 타겟으로 물어 유인이 성립하지 않는다.
        emittedChannel.RaiseEvent(new NoiseEmittedEvent(NetworkObjectId, point, impactNoiseDb));
        Log.D($"[ThrownProjectile] 착지 {impactNoiseDb}dB at {point}, source={NetworkObjectId}");

        PlayImpactClientRpc(point);
    }

    /// <summary>
    /// 착지음을 전 클라에서 재생한다. <see cref="ItemWorldSfx"/> 처럼 OnNetworkDespawn 에 맡기지 않는 이유는,
    /// 원격 클라의 인스턴스가 보간 지연 탓에 소멸 시점에 실제 착지점보다 뒤에 있어 소리가 엉뚱한 곳에서 나기 때문이다.
    /// 좌표를 실어 보내면 서버가 판정한 지점에서 정확히 울린다.
    /// </summary>
    /// <param name="point">착지 지점(월드).</param>
    [ClientRpc]
    private void PlayImpactClientRpc(Vector3 point)
    {
        sfxEventChannel.RaisePlayEvent(impactAudioId, point);
    }
}
