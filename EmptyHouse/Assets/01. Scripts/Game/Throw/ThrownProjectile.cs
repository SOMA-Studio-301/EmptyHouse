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
    [Tooltip("발사 지점에서 이 거리(m)를 벗어나기 전의 충돌은 착지로 치지 않는다. 발사 지점이 던진 사람의 머리 안이라, 손 근처에서 스친 것까지 착지로 잡으면 발밑에서 즉시 끝난다")]
    [SerializeField] private float armingDistanceMeters = 0.5f;

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

    // 발사 지점(월드). 착지 판정을 무장시키는 기준점이다.
    private Vector3 launchOrigin;

    /// <summary>Rigidbody 참조와 무장 기준점을 캐시한다.</summary>
    private void Awake()
    {
        body = GetComponent<Rigidbody>();

        // ServerLaunch 이전에 부딪혀도 기준점이 월드 원점이 되지 않게 스폰 좌표로 먼저 채운다.
        launchOrigin = transform.position;
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

        launchOrigin = transform.position;
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
    /// 첫 충돌을 착지로 보고 소음을 발행한 뒤 소멸을 예약한다. 물리가 서버에서만 돌므로 이 콜백도 서버에서만 유효하다.
    ///
    /// 무엇에 맞았는지는 가리지 않는다(기획 확정 2026-08-07) — 바닥이든 벽이든 좀비 몸이든, 부딪힌 그 지점에서 소리가 난다.
    /// 레이어로 거르던 시절에는 맵 지오메트리가 Ground/Wall 로 칠해져 있어야만 소리가 났고, 하나라도 빠지면
    /// 투척물이 무음으로 굴러다니다 수명으로 사라졌다. 판정 대상을 없애 그 실패 모드 자체를 지운다.
    ///
    /// 대신 <see cref="armingDistanceMeters"/> 만큼 날아가기 전에는 착지로 치지 않는다. 발사 지점이 던진 사람의
    /// 머리 안이라, 대상을 안 가리는 판정과 겹치면 손 근처의 무엇이든(동료 몸·문짝·방금 놓은 픽업) 스치는 순간
    /// 발밑에서 끝나 버린다 — 던진 사람 본인은 <see cref="ServerLaunch"/> 가 IgnoreCollision 으로 빼지만
    /// 그 예외는 딱 본인까지다. 무시된 충돌도 물리적으로는 튕기므로 병은 그대로 날아간다.
    /// </summary>
    /// <param name="collision">충돌 정보.</param>
    private void OnCollisionEnter(Collision collision)
    {
        if (!IsServer || !IsSpawned || hasLanded) return;

        if ((transform.position - launchOrigin).sqrMagnitude < armingDistanceMeters * armingDistanceMeters)
        {
            Log.D($"[ThrownProjectile] 손 근처({armingDistanceMeters}m 이내) 충돌 무시 — {collision.gameObject.name}(layer {collision.gameObject.layer})");
            return;
        }

        hasLanded = true;
        despawnTimer = despawnDelaySeconds;

        Vector3 point = collision.GetContact(0).point;

        // SourceId 는 이 투척물 자신이다. 던진 사람 ID 를 넘기면 좀비가 그를 타겟으로 물어 유인이 성립하지 않는다.
        emittedChannel.RaiseEvent(new NoiseEmittedEvent(NetworkObjectId, point, impactNoiseDb));
        Log.D($"[ThrownProjectile] 착지 {impactNoiseDb}dB at {point} — {collision.gameObject.name}(layer {collision.gameObject.layer}), source={NetworkObjectId}");

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
