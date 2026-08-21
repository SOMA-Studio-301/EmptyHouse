using Border.Core;
using UnityEngine;

/// <summary>
/// S2_3 성공 판정 트리거 — 통과 복도 끝의 통과선 (튜토리얼.md 5장 tut_pass_line).
/// 플레이어가 들어오면 TutorialDirector 에 통과를 보고한다. 튜토리얼 씬 전용 배선이라 씬 참조를 허용한다.
/// </summary>
[RequireComponent(typeof(Collider))]
public sealed class TutorialPassLineTrigger : MonoBehaviour
{
    [SerializeField] private TutorialDirector director; // 통과 보고 대상. 튜토리얼 씬 안에서만 배선된다

    /// <summary>플레이어 진입 판정 — 플레이어면 director 에 통과를 보고한다.</summary>
    /// <param name="other">트리거에 들어온 콜라이더.</param>
    private void OnTriggerEnter(Collider other)
    {
        Log.D($"[TutorialPassLineTrigger] OnTriggerEnter {other.name}");

        // 플레이어 루트가 PlayerController 를 들고 있다 — 자식 콜라이더로 들어와도 잡히도록 GetComponentInParent 로 판정한다.
        if (other.GetComponentInParent<PlayerController>() == null) return;

        // 단계 게이팅은 디렉터의 몫 — 여기서는 통과 사실만 보고한다.
        director.NotifyPassLineReached();
    }
}
