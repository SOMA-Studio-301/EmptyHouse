using Border.Core;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 우클릭(Aim)·좌클릭(Attack) 입력을 손에 든 아이템의 사용으로 라우팅한다.
/// MVP 에서 능동 사용이 있는 것은 투척 오브젝트뿐이며(4-3), 맨손·그 외 종류는 무반응이다.
///
/// 조작(기획 확정 2026-07-30): 투척물을 든 채 우클릭을 **누르는 동안** 궤적이 표시되고,
/// 좌클릭이 실제 투척이다. 조준하지 않은 좌클릭도 정면으로 즉시 던진다 — 궤적선만 없을 뿐
/// 발사 파라미터는 같다(<see cref="ThrowAimIndicator.ResolveLaunchVelocity"/> 를 양쪽이 공유한다).
///
/// InputReader 는 프로세스 전역 SO 라 소유자만 구독해야 한다 —
/// 이 컴포넌트는 <see cref="PlayerController"/> 가 IsOwner 로 켜고 끈다.
/// </summary>
public class PlayerItemUser : MonoBehaviour
{
    [Header("Input")]
    [SerializeField] private InputReader inputReader;

    /// <summary>같은 플레이어 프리팹 안의 인벤토리 — 프리팹 내부 참조이므로 자기완결 원칙에 어긋나지 않는다.</summary>
    [Header("Player")]
    [SerializeField] private PlayerInventory inventory;

    /// <summary>같은 프리팹의 조준 표시. 발사 지점·초속도의 단일 진실 소스이기도 하다.</summary>
    [SerializeField] private ThrowAimIndicator aimIndicator;

    /// <summary>같은 프리팹의 서버 권위 스폰 창구. 투척물 스폰은 서버만 할 수 있다.</summary>
    [SerializeField] private PlayerThrower thrower;

    // 우클릭을 누르고 있는지 여부. 실제 궤적 표시 여부(IsAiming)와 구분한다 —
    // 누른 채로 투척해 맨손이 되면 궤적은 꺼지지만, 손에 다시 투척물이 오면 재조준돼야 한다.
    private bool aimHeld;

    /// <summary>입력 이벤트 구독을 시작한다.</summary>
    private void OnEnable()
    {
        inputReader.AttackEvent += OnAttack;
        inputReader.AimPressedEvent += OnAimPressed;
        inputReader.AimCanceledEvent += OnAimCanceled;
    }

    /// <summary>입력 이벤트 구독을 해제하고 조준 상태를 정리한다.</summary>
    private void OnDisable()
    {
        inputReader.AttackEvent -= OnAttack;
        inputReader.AimPressedEvent -= OnAimPressed;
        inputReader.AimCanceledEvent -= OnAimCanceled;

        aimHeld = false;
        aimIndicator.EndAim();
    }

    /// <summary>
    /// 우클릭을 누른 상태와 손에 든 것을 맞춰 궤적 표시를 켜고 끈다.
    /// 입력 콜백에서만 처리하면 조준 중에 휠로 열쇠로 바꿔도 궤적이 남으므로, 매 프레임 상태를 맞춘다.
    /// </summary>
    private void Update()
    {
        bool shouldAim = aimHeld && CanThrow();

        if (shouldAim) aimIndicator.BeginAim();
        else aimIndicator.EndAim();
    }

    /// <summary>우클릭 누름. 조준 의사만 세우고 실제 표시 여부는 <see cref="Update"/> 가 판정한다.</summary>
    private void OnAimPressed()
    {
        aimHeld = true;
    }

    /// <summary>우클릭 뗌. 홀드 방식이므로 즉시 조준 취소다.</summary>
    private void OnAimCanceled()
    {
        aimHeld = false;
    }

    /// <summary>
    /// 좌클릭 입력을 손에 든 아이템 종류로 분기한다. MVP 에서는 Throwable 만 사용된다 —
    /// 맨손이거나 손 전환 중이면(3-8 E6) 무반응이고, 그 외 종류도 능동 사용이 없다(4-3).
    /// </summary>
    private void OnAttack()
    {
        if (!CanThrow()) return;

        Throw();
    }

    /// <summary>
    /// 지금 투척이 가능한 상태인지 판정한다. 조준 표시 조건과 투척 조건이 같아야
    /// "궤적이 보이는데 안 던져진다"가 생기지 않으므로 한 곳에 둔다.
    /// </summary>
    /// <returns>손에 투척 오브젝트를 들고 있고 전환 중이 아니면 true.</returns>
    private bool CanThrow()
    {
        if (inventory.IsSwapping) return false; // E6: 손 전환 중에는 사용할 수 없다
        if (inventory.IsBareHanded) return false;

        return inventory.HeldSlot.Data.Kind == ItemKind.Throwable;
    }

    /// <summary>
    /// 손에 든 투척 오브젝트를 던진다: 비행체(ProjectilePrefab)를 서버에 스폰 요청하고 슬롯에서 소멸시킨다
    /// (4-3: 투척 즉시 소멸). 슬롯이 비면 자동 맨손이 된다 — 다음 슬롯이 손에 딸려 오지 않는다(4-2).
    /// </summary>
    private void Throw()
    {
        ItemDataSO data = inventory.HeldSlot.Data;
        if (data.ProjectilePrefab == null)
        {
            Log.E($"[PlayerItemUser] {data.NameKey} 는 Throwable 인데 ProjectilePrefab 이 비어 있다.");
            return;
        }

        Log.D($"[PlayerItemUser] Throw {data.NameKey}");

        thrower.RequestThrow(
            data.ProjectilePrefab.GetComponent<NetworkObject>().PrefabIdHash,
            aimIndicator.LaunchOrigin,
            aimIndicator.ResolveLaunchVelocity());

        // 버리기(DropHeld)가 아니다 — 월드에 픽업을 남기지 않는다.
        inventory.ConsumeHeld();
    }
}
