using Border.Core;
using UnityEngine;

/// <summary>
/// 회수 가능한 열쇠 오브젝트 (조작상호작용UI.md 3-5, 3-6).
/// 자물쇠_NN과 1:1로 짝지어지며, 회수 시 인벤토리에 편입되고 오브젝트는 소멸한다.
/// 인벤토리 시스템(4장, 미착수) 확정 전까지 실제 편입 로직은 TODO로 남는다 — 확정 후 이 스켈레톤에
/// 필드 추가가 필요하면 오케스트레이터 승인 사항이다(시그니처 변경 금지 원칙).
/// </summary>
public class KeyInteractable : SingleClickInteractableBase
{
    [Header("Pairing")]
    [SerializeField] private int pairId;

    [Header("Noise")]
    [SerializeField] private float pickupNoiseDb = 5f;

    /// <summary>항상 활성 상태의 "회수" 프롬프트를 반환한다(3-5: 비활성 조건 없음).</summary>
    /// <returns>회수 프롬프트 정보.</returns>
    public override InteractPromptInfo GetPromptInfo()
    {
        // 매 프레임 호출되므로 진입 트레이스를 두지 않는다.
        return new InteractPromptInfo(InteractPromptState.Active, "회수", InteractInputMethod.Tap, 0f, null);
    }

    /// <summary>인벤토리에 pairId를 열쇠로 편입하고 오브젝트를 소멸시킨 뒤 소음을 발행한다(3-4: 실행 시점 발행).</summary>
    protected override void OnActivate()
    {
        // TODO(impl): 인벤토리 시스템 확정 후 열쇠(pairId) 편입 API 호출.
        Log.D($"[KeyInteractable] OnActivate pairId={pairId}");

        // Destroy 이후에는 transform·name 접근이 위험하므로 소음을 먼저 발행한다.
        RaiseNoise(pickupNoiseDb);
        Destroy(gameObject);
    }
}
