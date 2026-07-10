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

    [Header("UI")]
    [SerializeField] private UIInteractPrompt promptUI;

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

    // 디버그 로그 도배 방지 — 직전 프레임에 조준하고 있던 오브젝트 이름. 없으면 null.
    private string lastHitName;

    /// <summary>현재 프레임의 프롬프트 정보. HUD가 이 값을 그대로 그린다(3-2 계약, 3-3 표시 규칙).</summary>
    public InteractPromptInfo CurrentPromptInfo { get; private set; }

    /// <summary>입력 이벤트 구독을 시작한다.</summary>
    private void OnEnable()
    {
        inputReader.InteractPressedEvent += OnInteractPressed;
        inputReader.InteractCanceledEvent += OnInteractCanceled;
    }

    /// <summary>입력 이벤트 구독을 해제한다.</summary>
    private void OnDisable()
    {
        inputReader.InteractPressedEvent -= OnInteractPressed;
        inputReader.InteractCanceledEvent -= OnInteractCanceled;
    }

    /// <summary>
    /// 매 프레임 화면 중심 레이캐스트로 후보를 갱신하고, 사거리·비활성 조건·전역 예외를 판정해
    /// <see cref="CurrentPromptInfo"/> 를 갱신한 뒤 HUD 에 넘긴다(3-1 판정 파이프라인).
    /// 1인칭이라 화면 중심 == 카메라 forward 이므로 단일 Raycast 로 충분하며,
    /// 이때 가장 가까운 히트 1개만 돌아오므로 "조준점 최근접 1개"(3-8 E4)가 자동 충족된다 — 후보 정렬·순환 로직 불필요.
    /// </summary>
    private void Update()
    {
        // TODO(impl): hit.collider.GetComponentInParent<InteractableBase>() 로 후보 조회 — 콜라이더가 자식에 달릴 수 있다.
        // TODO(impl): hit.collider.ClosestPoint(distanceOrigin.position) 까지의 거리로 interactRangeMeters 재검증
        //             (카메라 기준 거리가 아니다, 3-1).
        // TODO(impl): E1(위장 중)·E5(사망/관전 중) 전역 예외면 후보를 버린다.
        // TODO(impl): currentCandidate 갱신. 후보가 없으면 CurrentPromptInfo 를 Hidden 으로, 있으면 후보의 GetPromptInfo().
        // TODO(impl): promptUI.Render(CurrentPromptInfo).

        // 벽·문까지 포함한 raycastLayers 로 쏘므로 가장 가까운 히트 1개만 돌아온다 = 관통하지 않는다(3-1).
        if (!Physics.Raycast(rayOrigin.position, rayOrigin.forward, out RaycastHit hit, rayMaxDistance, raycastLayers))
        {
            ReportHit(null);
            return;
        }

        // 히트한 것이 interactableLayer 밖이면 벽 등에 가려진 것이다.
        bool isInteractable = (interactableLayer.value & (1 << hit.collider.gameObject.layer)) != 0;
        ReportHit(isInteractable ? hit.collider.gameObject.name : null);
    }

    /// <summary>
    /// 현재 조준 중인 상호작용 오브젝트 이름을 로그로 남긴다. 직전 프레임과 같으면 아무것도 하지 않는다.
    /// 판정 파이프라인이 채워지기 전까지 조준선 정합성을 확인하기 위한 임시 구현이다.
    /// </summary>
    /// <param name="hitName">조준 중인 오브젝트 이름. 후보가 없으면 null.</param>
    private void ReportHit(string hitName)
    {
        if (hitName == lastHitName) return;

        lastHitName = hitName;
        Log.D(hitName == null
            ? "[PlayerInteractor] Hit: (none)"
            : $"[PlayerInteractor] Hit: {hitName}");
    }

    /// <summary>
    /// E 입력이 시작됐을 때 호출된다. 후보의 입력 방식에 따라 Tap은 즉시 실행, Hold는 진행을 시작한다.
    /// </summary>
    private void OnInteractPressed()
    {
        // TODO(impl): currentCandidate 없거나 비활성/점유 중이면 return.
        // TODO(impl): InputMethod==Tap → (SingleClickInteractableBase)currentCandidate 로 TryActivate().
        // TODO(impl): InputMethod==Hold → (HoldInteractableBase)currentCandidate 로 BeginHold().
        Log.D("[PlayerInteractor] Interact pressed");
    }

    /// <summary>
    /// E 입력이 취소(해제)됐을 때 호출된다. 진행 중인 Hold형 상호작용이 있으면 취소한다(3-8 E2).
    /// </summary>
    private void OnInteractCanceled()
    {
        // TODO(impl): currentCandidate가 HoldInteractableBase 이고 진행 중이면 CancelHold().
        Log.D("[PlayerInteractor] Interact canceled");
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
