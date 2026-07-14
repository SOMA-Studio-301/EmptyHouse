using Border.Core;
using UnityEngine;

/// <summary>
/// 좌클릭(Attack 액션) 입력을 손에 든 아이템의 사용으로 라우팅한다 (조작상호작용UI.md 2장: 좌클릭 = 손에 든 아이템 사용).
/// MVP 에서 능동 사용이 있는 것은 투척 오브젝트뿐이며(4-3), 맨손·그 외 종류는 무반응이다.
/// 상태 게이팅(위장·홀드 중·손 전환 중·관전, 2-1 표)은 플레이어 상태 머신 도입(후속 단계) 후 여기에 연결한다.
/// </summary>
public class PlayerItemUser : MonoBehaviour
{
    [Header("Input")]
    [SerializeField] private InputReader inputReader;

    /// <summary>같은 플레이어 프리팹 안의 인벤토리 — 프리팹 내부 참조이므로 자기완결 원칙에 어긋나지 않는다.</summary>
    [Header("Player")]
    [SerializeField] private PlayerInventory inventory;

    [Header("Throw")]
    [SerializeField] private Transform throwOrigin; // 투척 시작점(카메라/손 위치) — 프리팹 내부 참조
    [SerializeField] private float throwSpeed = 10f; // ⚪ 투척 초속(m/s). 스펙 미기재 — 플레이테스트 튜닝값

    /// <summary>입력 이벤트 구독을 시작한다.</summary>
    private void OnEnable()
    {
        inputReader.AttackEvent += OnAttack;
    }

    /// <summary>입력 이벤트 구독을 해제한다.</summary>
    private void OnDisable()
    {
        inputReader.AttackEvent -= OnAttack;
    }

    /// <summary>
    /// 좌클릭 입력을 손에 든 아이템 종류로 분기한다. 맨손이거나(2장: 손이 비어 있으면 무반응)
    /// 손 전환 중이면(3-8 E6) 무반응이고, MVP 에서는 Throwable 만 사용된다.
    /// </summary>
    private void OnAttack()
    {
        // TODO(impl): inventory.IsSwapping 이면 return(E6). inventory.HeldSlot 이 비었으면 return.
        // TODO(impl): kind == Throwable → Throw(). 그 외 종류는 무반응(4-3: 능동 사용 없음).
        Log.D("[PlayerItemUser] OnAttack");
    }

    /// <summary>
    /// 손에 든 투척 오브젝트를 던진다: 비행체(ProjectilePrefab)를 스폰·발사하고 슬롯에서 소멸시킨다(4-3: 투척 즉시 소멸).
    /// 슬롯이 비면 자동 맨손이 된다 — 다음 슬롯이 손에 딸려 오지 않는다(4-2 자동 맨손 규칙).
    /// </summary>
    private void Throw()
    {
        // TODO(impl): HeldSlot.Data.ProjectilePrefab 을 throwOrigin 에 스폰 → ThrownProjectile.Launch(전방 * throwSpeed).
        // TODO(impl): inventory.ConsumeHeld() — 버리기(DropHeld)가 아니다: 월드에 픽업을 남기지 않는다.
        Log.D("[PlayerItemUser] Throw");
    }
}
