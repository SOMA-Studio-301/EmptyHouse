using Border.Core;
using Border.Localization;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 절차 스폰 자물쇠 상호작용부 — 잠긴 문의 LockPos 에 서버가 스폰하는 자물쇠 변종 프리팹의 루트 컴포넌트.
/// 판정·프롬프트·소음만 갖고 상태를 소유하지 않는다 — 확정(해정·열쇠 소멸)은 문 루트의 서버 RPC(M7 요구 1),
/// 해정 승인 시 자물쇠 오브젝트는 문이 소멸시킨다. 문 참조는 스폰 직후 서버가 주입해 복제한다(늦은 합류 포함).
/// 독립형 LockInteractable(수동 배치 맵)과 별개 — 절차 생성 자물쇠는 이 컴포넌트를 쓴다.
/// </summary>
public sealed class DoorLockFace : SingleClickInteractableBase
{
    [Header("Noise")]
    [SerializeField] private float openNoiseDb = 45f; // 해정 소음(조작상호작용UI 3-10 lock_open_noise_db ⚪)

    [Header("Localization")]
    [LocalizeKey][SerializeField] private string openVerbKey = "INT_PROMPT_OPEN"; // 쌍 열쇠 보유 시 활성 행위명
    [LocalizeKey][SerializeField] private string needsKeyReasonKey = "INT_MSG_KEY_REQUIRED"; // 쌍 열쇠 부재 비활성 사유

    private readonly NetworkVariable<NetworkBehaviourReference> doorRef = new NetworkVariable<NetworkBehaviourReference>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server); // 상태 단일 소스인 문 루트 참조 — 별도 NetworkObject 라 복제로 연결

    /// <summary>스폰 직후 서버가 이 자물쇠가 걸린 문 루트를 연결한다(MapStateObjectSpawner 전용 경로).</summary>
    /// <param name="door">문 루트.</param>
    public void ServerAttachDoor(DoorInteractable door)
    {
        Log.D($"[DoorLockFace] ServerAttachDoor {NetworkObjectId}");
        doorRef.Value = door; // 서버 외 호출은 NetworkVariable 쓰기 예외로 즉시 표면화(§4 — 가드 없음)
    }

    /// <summary>
    /// 프롬프트를 반환한다: 문 미연결·개방·비잠금 → 미표시 / 손에 쌍 열쇠(door.PairId) → 활성 / 그 외 → 비활성.
    /// 판정 기준은 손에 든 것뿐(조작상호작용UI 3-6 — 인벤 소지 여부는 보지 않는다).
    /// </summary>
    /// <param name="interactor">이 자물쇠를 조준 중인 상호작용 주체.</param>
    /// <returns>문·손 상태에 따른 프롬프트 정보.</returns>
    public override InteractPromptInfo GetPromptInfo(PlayerInteractor interactor)
    {
        // 매 프레임 호출 — 진입 트레이스를 두지 않는다(LockInteractable 동일 규칙)
        if (!doorRef.Value.TryGet(out DoorInteractable door) || door.IsOpen || !door.IsLocked)
        {
            return new InteractPromptInfo(InteractPromptState.Hidden, null, InteractInputMethod.Tap, 0f, null);
        }

        if (!HasMatchingKeyInHand(interactor, door))
        {
            return new InteractPromptInfo(InteractPromptState.Inactive, null, InteractInputMethod.Tap, 0f, needsKeyReasonKey);
        }

        return new InteractPromptInfo(InteractPromptState.Active, openVerbKey, InteractInputMethod.Tap, 0f, null);
    }

    /// <summary>해정 시도 효과 — 소음 1회 발행 후 문 루트에 해정+개방을 요청한다. 열쇠 소멸·자물쇠 소멸은 서버 승인 후(루트 소관).</summary>
    /// <param name="interactor">E 를 입력한 상호작용 주체.</param>
    protected override void OnActivate(PlayerInteractor interactor)
    {
        Log.D($"[DoorLockFace] OnActivate on {name}");

        // 프롬프트가 Active 였어도 실행 프레임에 상태가 변했을 수 있다 — 클라 이중 가드(서버 재검증은 잠금 상태만 가능)
        if (!doorRef.Value.TryGet(out DoorInteractable door) || door.IsOpen || !door.IsLocked || !HasMatchingKeyInHand(interactor, door))
        {
            return;
        }

        RaiseNoise(openNoiseDb);
        door.RequestUnlockOpen(interactor);
    }

    /// <summary>손에 든 것이 이 문과 짝맞는 열쇠(Key · door.PairId 일치)인지 판정한다. 맨손이면 false.</summary>
    /// <param name="interactor">판정할 상호작용 주체.</param>
    /// <param name="door">쌍 번호 기준 문 루트.</param>
    /// <returns>짝맞는 열쇠를 손에 들었는지 여부.</returns>
    private static bool HasMatchingKeyInHand(PlayerInteractor interactor, DoorInteractable door)
    {
        InventorySlot held = interactor.Inventory.HeldSlot;

        // 맨손이면 HeldSlot 이 빈 값이라 Data 가 null 이다 — 여기서 걸러진다.
        return !held.IsEmpty && held.Data.Kind == ItemKind.Key && held.PairId == door.PairId;
    }
}
