using Border.Core;
using UnityEngine;

/// <summary>
/// 상호작용 판정 파이프라인의 단일 소유자 (조작상호작용UI.md 3-1).
/// 매 프레임 화면 중심에서 레이캐스트해 후보 Interactable을 선정하고, 사거리·활성 조건·전역 예외(3-8 E1·E5)를
/// 여기서 한 번만 판정한다 — 개별 Interactable 스크립트는 이 규칙을 복제하지 않는다(3-1 경고).
/// 입력 방식별 실행(탭 즉시 / 홀드 진행)은 후보의 <see cref="InteractInputMethod"/> 로 분기해 위임한다.
/// </summary>
public class PlayerInteractor : MonoBehaviour
{
    [Header("Input")]
    [SerializeField] private InputReader inputReader;

    /// <summary>같은 플레이어 프리팹 안의 인벤토리 — 프리팹 내부 참조이므로 자기완결 원칙에 어긋나지 않는다.</summary>
    [Header("Player")]
    [SerializeField] private PlayerInventory inventory;

    [Header("UI")]
    [SerializeField] private UIInteractPrompt promptUI;
    [SerializeField] private UIHoldGauge holdGaugeUI;

    [Header("Raycast")]
    [SerializeField] private Transform rayOrigin;

    /// <summary>레이가 충돌할 전체 레이어 — Interactable 에 더해 벽·문 등 시야 차단물을 반드시 포함한다(3-1: 레이는 벽을 관통하지 않는다).</summary>
    [SerializeField] private LayerMask raycastLayers;

    /// <summary>위 레이어 중 실제 상호작용 대상으로 인정할 레이어. 히트한 것이 이 마스크 밖이면 무언가에 가려진 것이다.</summary>
    [SerializeField] private LayerMask interactableLayer;

    [Header("Range")]
    [SerializeField] private Transform distanceOrigin;
    [SerializeField] private float interactRangeMeters = 3.0f;

    [Header("Debug")]
    [SerializeField] private bool drawDebugGizmos = true;
    [SerializeField] private float rayMaxDistance = 5.0f;

    private InteractableBase currentCandidate;

    // 지금 진행 중인 홀드 대상. null 이면 홀드 중이 아니다. 취소 경로(버튼 해제·이동·조준 이탈)가 공유하는 단일 참조다.
    private HoldInteractableBase activeHold;

    // 후보가 없을 때 쓰는 불변 프롬프트. 매 프레임 struct 를 새로 만들 이유가 없다.
    private static readonly InteractPromptInfo HiddenPrompt =
        new InteractPromptInfo(InteractPromptState.Hidden, null, InteractInputMethod.Tap, 0f, null);

    /// <summary>현재 프레임의 프롬프트 정보. HUD가 이 값을 그대로 그린다(3-2 계약, 3-3 표시 규칙).</summary>
    public InteractPromptInfo CurrentPromptInfo { get; private set; }

    /// <summary>
    /// 이 주체의 인벤토리. Interactable 이 "손에 든 것 × 빈 슬롯" 판정(3-5, 3-6)에 조회한다.
    /// 위장 게이지 등 주체 상태가 더 필요해지면 여기에 프로퍼티를 추가해 컨텍스트를 넓힌다.
    /// </summary>
    public PlayerInventory Inventory => inventory;

    /// <summary>진행 중인 홀드의 진행률(0~1). 홀드 중이 아니거나 대상이 소멸했으면 0이다. 조준선 게이지가 이 값을 그린다.</summary>
    public float HoldProgress01 => activeHold == null ? 0f : activeHold.Progress01;

    /// <summary>입력 이벤트 구독을 시작한다. 이동 입력은 홀드 취소 조건이라 함께 구독한다(3-7).</summary>
    private void OnEnable()
    {
        inputReader.InteractPressedEvent += OnInteractPressed;
        inputReader.InteractCanceledEvent += OnInteractCanceled;
        inputReader.MoveEvent += OnMove;
    }

    /// <summary>입력 이벤트 구독을 해제한다.</summary>
    private void OnDisable()
    {
        inputReader.InteractPressedEvent -= OnInteractPressed;
        inputReader.InteractCanceledEvent -= OnInteractCanceled;
        inputReader.MoveEvent -= OnMove;
    }

    /// <summary>
    /// 매 프레임 화면 중심 레이캐스트로 후보를 갱신하고, 사거리·비활성 조건·전역 예외를 판정해
    /// <see cref="CurrentPromptInfo"/> 를 갱신한 뒤 HUD 에 넘긴다(3-1 판정 파이프라인).
    /// 1인칭이라 화면 중심 == 카메라 forward 이므로 단일 Raycast 로 충분하며,
    /// 이때 가장 가까운 히트 1개만 돌아오므로 "조준점 최근접 1개"(3-8 E4)가 자동 충족된다 — 후보 정렬·순환 로직 불필요.
    /// </summary>
    private void Update()
    {
        // TODO(impl): E1(위장 중)·E5(사망/관전 중) 전역 예외면 후보를 버린다.

        currentCandidate = FindCandidate();
        CurrentPromptInfo = currentCandidate == null ? HiddenPrompt : currentCandidate.GetPromptInfo(this);

        SyncActiveHold();

        promptUI.Render(CurrentPromptInfo);
        holdGaugeUI.Render(HoldProgress01);
    }

    /// <summary>
    /// 진행 중인 홀드를 이번 프레임의 후보와 맞춘다. 완료·소멸했으면 참조를 놓고,
    /// 조준이 대상에서 벗어났으면 취소한다 — 벽 뒤·사거리 밖을 보면서 게이지가 차오르는 것을 막는다.
    /// </summary>
    private void SyncActiveHold()
    {
        if (activeHold == null) return; // Destroy 된 대상도 Unity 의 == 오버로드로 여기서 걸린다(기름 회수 완료).

        // 대상이 스스로 완료·초기화했으면(진행 상태 종료) 참조만 놓는다 — CancelHold 를 다시 부를 필요가 없다.
        if (!activeHold.IsHolding)
        {
            activeHold = null;
            return;
        }

        if (currentCandidate == activeHold) return;

        CancelActiveHold();
    }

    /// <summary>
    /// 화면 중심 레이캐스트로 이번 프레임의 후보 Interactable 을 찾는다.
    /// 히트가 없거나, 벽 등에 가려졌거나, Interactable 이 아니거나, 사거리 밖이면 후보 없음이다.
    /// </summary>
    /// <returns>이번 프레임의 후보. 없으면 null.</returns>
    private InteractableBase FindCandidate()
    {
        // 벽·문까지 포함한 raycastLayers 로 쏘므로 가장 가까운 히트 1개만 돌아온다 = 관통하지 않는다(3-1).
        if (!Physics.Raycast(rayOrigin.position, rayOrigin.forward, out RaycastHit hit, rayMaxDistance, raycastLayers))
        {
            return null;
        }

        // 히트한 것이 interactableLayer 밖이면 벽 등에 가려진 것이다.
        if ((interactableLayer.value & (1 << hit.collider.gameObject.layer)) == 0)
        {
            return null;
        }

        // 콜라이더는 Interactable 의 자식에 달릴 수 있다.
        InteractableBase interactable = hit.collider.GetComponentInParent<InteractableBase>();
        if (interactable == null)
        {
            return null;
        }

        // 사거리는 카메라(rayOrigin)가 아니라 distanceOrigin ↔ 콜라이더 최근접점 기준이다(3-1).
        // 그래야 큰 오브젝트를 먼 쪽 끝에서 조준해도 몸이 붙어 있으면 상호작용이 된다.
        Vector3 closestPoint = hit.collider.ClosestPoint(distanceOrigin.position);
        if ((closestPoint - distanceOrigin.position).sqrMagnitude > interactRangeMeters * interactRangeMeters)
        {
            return null;
        }

        return interactable;
    }

    /// <summary>
    /// E 입력이 시작됐을 때 호출된다. 후보의 입력 방식에 따라 Tap은 즉시 실행, Hold는 진행을 시작한다.
    /// </summary>
    private void OnInteractPressed()
    {
        Log.D("[PlayerInteractor] Interact pressed");

        if (currentCandidate == null) return;

        // Active 가 아니면(사거리 밖·조건 미충족·개방 완료 등) 프롬프트가 안내한 그대로 무반응이다.
        if (CurrentPromptInfo.State != InteractPromptState.Active) return;

        // 다른 플레이어가 점유 중이면 실행하지 않는다(3-9 M1).
        if (currentCandidate.IsOccupied) return;

        if (currentCandidate.InputMethod == InteractInputMethod.Tap)
        {
            ((SingleClickInteractableBase)currentCandidate).TryActivate(this);
            return;
        }

        activeHold = (HoldInteractableBase)currentCandidate;
        activeHold.BeginHold(this);
    }

    /// <summary>
    /// E 입력이 취소(해제)됐을 때 호출된다. 진행 중인 Hold형 상호작용이 있으면 취소한다(3-8 E2).
    /// </summary>
    private void OnInteractCanceled()
    {
        Log.D("[PlayerInteractor] Interact canceled");

        CancelActiveHold();
    }

    /// <summary>
    /// 이동 입력 콜백. 홀드 중 이동은 취소 조건이다(3-7: 홀드 중 이동 불가 = 취약 구간).
    /// 입력이 끊길 때도 zero 로 한 번 들어오므로 실제 입력값이 있을 때만 취소한다.
    /// </summary>
    /// <param name="move">이번에 갱신된 이동 입력값.</param>
    private void OnMove(Vector2 move)
    {
        if (move.sqrMagnitude <= 0f) return;

        CancelActiveHold();
    }

    /// <summary>진행 중인 홀드가 있으면 취소하고 참조를 놓는다. 진행도는 저장하지 않는다(3-7).</summary>
    private void CancelActiveHold()
    {
        if (activeHold == null) return;

        activeHold.CancelHold();
        activeHold = null;
    }

    /// <summary>
    /// 조준선 ↔ 상호작용 레이의 정합성을 검증하기 위한 디버그 기즈모를 그린다.
    /// 히트하면 초록 라인 + 히트 지점 구 + 표면 노멀을, 미스하면 <see cref="rayMaxDistance"/> 길이의 빨강 라인을 그린다.
    /// distanceOrigin 기준 <see cref="interactRangeMeters"/> 사거리는 노란 와이어 스피어로 표시한다.
    /// 플레이 중에는 실제 조준선인 화면 중심 레이를, 에디트 모드에서는 <see cref="rayOrigin"/> 의 forward 를 사용한다 —
    /// 둘이 어긋나면 rayOrigin 의 정렬이 잘못된 것이다.
    /// </summary>
    /// <remarks>
    /// 에디트 모드에서 인스펙터 할당 전에도 매 프레임 호출되므로, 이 메서드 한정으로 null 가드를 둔다(§4 예외).
    /// </remarks>
    private void OnDrawGizmos()
    {
        if (!drawDebugGizmos || rayOrigin == null) return;

        Ray ray = GetDebugRay();

        if (Physics.Raycast(ray, out RaycastHit hit, rayMaxDistance, raycastLayers))
        {
            Gizmos.color = Color.green;
            Gizmos.DrawLine(ray.origin, hit.point);
            Gizmos.DrawWireSphere(hit.point, 0.05f);

            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(hit.point, hit.point + hit.normal * 0.3f);
        }
        else
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(ray.origin, ray.origin + ray.direction * rayMaxDistance);
        }

        if (distanceOrigin != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(distanceOrigin.position, interactRangeMeters);
        }
    }

    /// <summary>
    /// 디버그 기즈모용 레이를 만든다. 플레이 중이면 메인 카메라의 화면 중심(뷰포트 0.5, 0.5) 레이를,
    /// 그 외에는 <see cref="rayOrigin"/> 의 위치·forward 로 구성한 레이를 반환한다.
    /// </summary>
    /// <returns>기즈모를 그릴 레이.</returns>
    private Ray GetDebugRay()
    {
        if (Application.isPlaying && Camera.main != null)
        {
            return Camera.main.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        }

        return new Ray(rayOrigin.position, rayOrigin.forward);
    }
}
