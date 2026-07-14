using Border.Core;
using UnityEngine;

/// <summary>
/// 홀드(클릭 유지) 입력으로 실행되는 Interactable의 공통 부모.
/// 진행률 상태머신과 취소 조건(조작상호작용UI.md 3-7 홀드 다이어그램, 3-8 E2)을 여기서 관리하며,
/// 리프 클래스는 완료 시점의 효과(<see cref="InteractableBase.OnActivate"/>)만 구현하면 된다.
/// 진행률을 매 프레임 누적하는 구현(Update/Coroutine 선택)은 틀에 없는 private 헬퍼로 자유롭게 추가한다.
/// </summary>
public abstract class HoldInteractableBase : InteractableBase
{
    public sealed override InteractInputMethod InputMethod => InteractInputMethod.Hold; // 홀드 입력 방식으로 고정.
    public abstract float HoldDurationSeconds { get; } // 완료까지 필요한 유지 시간(초). 대상마다 달라(기름 2.0s·사체 3.0s, 3-10) 인스펙터 노출 필드로 구현.
    public float Progress01 { get; private set; } // 현재 진행률(0~1). 월드 UI 게이지 표시용. 진행 중이 아니면 0.
    public bool IsHolding => holder != null; // 홀드 진행 중 여부. PlayerInteractor 의 취소 대상 판단용.
    
    private PlayerInteractor holder; // 홀드를 시작한 주체. null 이면 대기 상태다 = 진행 중 여부의 단일 소스
    private float elapsedSeconds; // 누적 유지 시간(초)

    /// <summary>
    /// PlayerInteractor가 E 입력 시작(Started phase)을 받았을 때 호출한다. 진행률을 0에서부터 누적하기 시작한다.
    /// 다른 플레이어가 점유 중(<see cref="InteractableBase.IsOccupied"/>)이면 호출되지 않아야 한다.
    /// </summary>
    /// <param name="interactor">홀드를 시작한 상호작용 주체. 완료 시점의 OnActivate 에 그대로 전달한다.</param>
    public void BeginHold(PlayerInteractor interactor)
    {
        Log.D($"[HoldInteractableBase] BeginHold on {name}");

        holder = interactor;
        elapsedSeconds = 0f;
        Progress01 = 0f;
    }

    /// <summary>
    /// 홀드 취소 조건 발생 시 PlayerInteractor가 호출한다 — 버튼 해제·이동 입력·피격/발각 (3-8 E2).
    /// 진행률은 저장하지 않으므로 재시도 시 0부터 다시 시작한다.
    /// </summary>
    public void CancelHold()
    {
        Log.D($"[HoldInteractableBase] CancelHold on {name}");

        ResetHold();
    }

    /// <summary>
    /// 진행 중일 때 매 프레임 시간을 누적해 Progress01을 갱신하고, HoldDurationSeconds에 도달하면
    /// 보관해 둔 홀더를 인자로 OnActivate 를 1회 호출한 뒤 진행률을 초기화한다.
    /// 소음 발행은 베이스가 하지 않는다 — 강도(dB)는 리프의 데이터이므로 리프의 OnActivate 안에서
    /// RaiseNoise 를 호출한다(3-4: 홀드는 완료 시점 1회 발행, 매 프레임 발행 금지).
    /// </summary>
    private void Update()
    {
        // 매 프레임 호출되므로 진입 트레이스를 두지 않는다.
        if (holder == null) return;

        elapsedSeconds += Time.deltaTime;
        Progress01 = Mathf.Clamp01(elapsedSeconds / HoldDurationSeconds);

        if (Progress01 < 1f) return;

        // 리프의 OnActivate 가 자기 자신을 Destroy 할 수 있으므로(기름 회수) 상태를 먼저 되돌린 뒤 호출한다.
        PlayerInteractor completedBy = holder;
        ResetHold();

        OnActivate(completedBy);
    }

    /// <summary>진행 상태를 대기로 되돌린다. 완료·취소가 같은 초기화 경로를 쓰도록 한 곳에 둔다.</summary>
    private void ResetHold()
    {
        holder = null;
        elapsedSeconds = 0f;
        Progress01 = 0f;
    }
}
