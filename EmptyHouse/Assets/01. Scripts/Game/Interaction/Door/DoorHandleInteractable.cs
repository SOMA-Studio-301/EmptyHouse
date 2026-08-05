using Border.Core;
using Border.Localization;
using UnityEngine;

/// <summary>
/// 문 손잡이 면 — 문 개방 상호작용부. 잠긴 동안에는 프롬프트 비활성 + 실행 차단(M7 요구 3 —
/// 서버도 RequestOpen 을 거부하므로 클라 게이팅은 UX, 권위는 서버). 개방된 문은 프롬프트를 숨긴다(영구 개방).
/// </summary>
public sealed class DoorHandleInteractable : SingleClickInteractableBase
{
    [Header("Door")]
    [SerializeField] private DoorInteractable door; // 상태 단일 소스인 문 루트(프리팹 내부 부모 참조)

    [Header("Noise")]
    [SerializeField] private float openNoiseDb = 20f; // 문 개방 소음(⚪ — 소음시스템 Data/noise.csv 연동은 구현 시 확인)

    [Header("Localization")]
    [LocalizeKey][SerializeField] private string openVerbKey = "INT_PROMPT_OPEN"; // 활성 프롬프트 행위명
    [LocalizeKey][SerializeField] private string lockedReasonKey = "INT_MSG_LOCKED"; // 잠김 비활성 사유(신규 키 — /localize-sheet 등재 필요)

    /// <summary>프롬프트를 반환한다: 개방됨 → 미표시 / 잠김 → 비활성(잠김 사유) / 그 외 → 활성 '열기'.</summary>
    /// <param name="interactor">이 문을 조준 중인 상호작용 주체.</param>
    /// <returns>문 상태에 따른 프롬프트 정보.</returns>
    public override InteractPromptInfo GetPromptInfo(PlayerInteractor interactor)
    {
        // 매 프레임 호출 — 진입 트레이스를 두지 않는다
        if (door.IsOpen)
        {
            return new InteractPromptInfo(InteractPromptState.Hidden, null, InteractInputMethod.Tap, 0f, null);
        }

        if (door.IsLocked)
        {
            return new InteractPromptInfo(InteractPromptState.Inactive, null, InteractInputMethod.Tap, 0f, lockedReasonKey);
        }

        return new InteractPromptInfo(InteractPromptState.Active, openVerbKey, InteractInputMethod.Tap, 0f, null);
    }

    /// <summary>개방 시도 효과 — 소음 1회 발행 후 문 루트에 개방을 요청한다(잠금 거부는 서버 소관).</summary>
    /// <param name="interactor">E 를 입력한 상호작용 주체.</param>
    protected override void OnActivate(PlayerInteractor interactor)
    {
        Log.D($"[DoorHandleInteractable] OnActivate on {name}");

        // 잠김·기개방이면 클라 게이팅으로 무동작(UX) — 권위 거부는 서버 RPC 소관(M7 요구 3)
        if (door.IsOpen || door.IsLocked)
        {
            return;
        }

        RaiseNoise(openNoiseDb);
        door.RequestOpen(interactor);
    }
}
