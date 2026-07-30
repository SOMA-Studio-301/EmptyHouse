using UnityEngine;

/// <summary>
/// 투척 조준 궤적 표시 — 우클릭을 누르는 동안 포물선과 착지 마커를 그린다.
///
/// 순수 로컬 표시물이라 네트워크에 태우지 않는다. 남이 어디를 조준 중인지는 알 필요가 없고,
/// 매 프레임 갱신되는 궤적을 복제하면 대역폭만 먹는다.
///
/// 발사 파라미터(초속·반지름)를 이 컴포넌트가 소유하고 <see cref="ResolveLaunchVelocity"/> 로 내주는 이유는,
/// 궤적선과 실제 발사가 서로 다른 값을 쓰면 조준이 그대로 거짓말이 되기 때문이다 — 단일 진실 소스로 둔다.
/// 그래서 조준 없이 던지는 경로(<see cref="PlayerItemUser"/>)도 이 값을 거쳐 간다.
/// </summary>
public class ThrowAimIndicator : MonoBehaviour
{
    [Header("Launch")]
    [SerializeField] private Transform throwOrigin; // 발사 시작점. cameraPivot 아래에 두어 시선 pitch 를 따라가게 한다
    [SerializeField] private float throwSpeed = 10f; // ⚪ 투척 초속(m/s). 스펙 미기재 — 플레이테스트 튜닝값
    [SerializeField] private float projectileRadius = 0.12f; // 비행체 콜라이더 반지름과 맞춰야 착지점이 맞는다

    [Header("Trajectory")]
    [SerializeField] private LineRenderer line; // 점선 타일링 머티리얼을 물린 궤적선. useWorldSpace = true
    [SerializeField] private Transform landingMarker; // 착지 지점 마커. 충돌면 법선에 맞춰 눕힌다
    [SerializeField] private LayerMask hitMask; // 궤적이 끊길 레이어. Ground · Wall · Door 를 선택한다
    [SerializeField] private int sampleCount = 40; // 궤적 샘플 수. 늘리면 더 멀리·매끄럽게 그린다
    [SerializeField] private float sampleStepSeconds = 0.05f; // 샘플 간 시간 간격(초)

    [Header("Style")]
    [SerializeField] private float scrollSpeed = 1.5f; // 점선 텍스처가 진행 방향으로 흐르는 속도. 0 이면 정지

    [Header("Fallback")]
    [Tooltip("line · landingMarker 를 비워 두면 이 머티리얼로 임시 표시물을 런타임에 만든다. 정식 궤적 텍스처가 나오면 위 두 칸을 채워 대체한다")]
    [SerializeField] private Material fallbackMaterial;
    [SerializeField] private float fallbackLineWidth = 0.03f;
    [SerializeField] private float fallbackMarkerSize = 0.35f;

    private Vector3[] points;

    /// <summary>지금 궤적을 그리고 있는지 여부. 좌클릭 투척이 조준 여부를 묻는 데 쓴다.</summary>
    public bool IsAiming { get; private set; }

    /// <summary>발사 시작점(월드). 조준 여부와 무관하게 투척은 항상 이 지점에서 나간다.</summary>
    public Vector3 LaunchOrigin => throwOrigin.position;

    /// <summary>
    /// 지금 던지면 적용될 초속도(월드). 궤적선이 쓰는 값과 동일하다 —
    /// 조준 중이면 화면의 곡선이 그대로 실현되고, 미조준이면 곡선만 안 보일 뿐 같은 힘으로 나간다.
    /// </summary>
    /// <returns>발사 초속도 벡터.</returns>
    public Vector3 ResolveLaunchVelocity()
    {
        return throwOrigin.forward * throwSpeed;
    }

    /// <summary>궤적 버퍼를 잡고, 표시물이 비어 있으면 임시로 만든 뒤 꺼 둔 상태로 시작한다.</summary>
    private void Awake()
    {
        points = new Vector3[Mathf.Max(2, sampleCount)];

        SetVisible(false);
    }

    /// <summary>
    /// 표시물이 비어 있으면 임시로 만든다. Awake 가 아니라 첫 조준 시점에 만드는 이유는,
    /// Awake 가 비소유자 인스턴스에서도 돌기 때문이다 — 남의 캐릭터마다 쓰지도 않을
    /// LineRenderer 와 Quad 가 하나씩 생긴다. 조준은 소유자만 하므로 여기가 맞는 자리다.
    /// </summary>
    private void EnsureVisuals()
    {
        if (line == null) line = BuildFallbackLine();
        if (landingMarker == null) landingMarker = BuildFallbackMarker();
    }

    /// <summary>
    /// 궤적선을 코드로 만든다. 정식 궤적 텍스처가 나오기 전까지 쓰는 단색 임시물이며,
    /// 인스펙터에 LineRenderer 를 물리면 이 경로는 타지 않는다.
    /// </summary>
    /// <returns>새로 만든 궤적선.</returns>
    private LineRenderer BuildFallbackLine()
    {
        GameObject host = new GameObject("ThrowLine (temp)");
        host.transform.SetParent(transform, false);

        LineRenderer created = host.AddComponent<LineRenderer>();
        created.useWorldSpace = true; // 좌표를 월드로 넣으므로 부모 변환을 타면 안 된다
        created.widthMultiplier = fallbackLineWidth;
        created.numCapVertices = 2;
        created.textureMode = LineTextureMode.Tile; // 나중에 점선 텍스처를 물리면 그대로 반복된다
        created.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        created.receiveShadows = false;
        created.material = fallbackMaterial;
        return created;
    }

    /// <summary>착지 마커를 코드로 만든다. 양면 머티리얼이라 어느 쪽에서 봐도 보인다.</summary>
    /// <returns>새로 만든 마커의 Transform.</returns>
    private Transform BuildFallbackMarker()
    {
        GameObject created = GameObject.CreatePrimitive(PrimitiveType.Quad);
        created.name = "ThrowLandingMarker (temp)";

        // 마커는 순수 표시물이다 — 콜라이더가 남으면 자기 궤적 SphereCast 에 걸려 착지점이 마커 위로 접힌다.
        Destroy(created.GetComponent<Collider>());

        MeshRenderer renderer = created.GetComponent<MeshRenderer>();
        renderer.material = fallbackMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        created.transform.SetParent(transform, false);
        created.transform.localScale = Vector3.one * fallbackMarkerSize;
        return created.transform;
    }

    /// <summary>컴포넌트가 꺼지면(사망·귀환 게이팅) 조준 표시가 화면에 남지 않도록 정리한다.</summary>
    private void OnDisable()
    {
        EndAim();
    }

    /// <summary>궤적 표시를 시작한다. 이미 조준 중이면 아무 일도 하지 않는다.</summary>
    public void BeginAim()
    {
        if (IsAiming) return;

        EnsureVisuals();
        IsAiming = true;
        SetVisible(true);
    }

    /// <summary>궤적 표시를 끝낸다. 조준 중이 아니면 아무 일도 하지 않는다.</summary>
    public void EndAim()
    {
        if (!IsAiming) return;

        IsAiming = false;
        SetVisible(false);
    }

    /// <summary>
    /// 궤적을 매 프레임 다시 그린다. Update 가 아니라 LateUpdate 인 이유는
    /// <see cref="PlayerController"/> 가 Update 에서 시선을 회전시키기 때문이다 —
    /// Update 에서 그리면 궤적이 카메라보다 한 프레임 늦게 따라와 흔들려 보인다.
    /// </summary>
    private void LateUpdate()
    {
        if (!IsAiming) return;

        int count = ThrowTrajectory.Simulate(
            LaunchOrigin, ResolveLaunchVelocity(), projectileRadius, hitMask,
            points, sampleStepSeconds, out RaycastHit impact);

        // 버퍼는 최대 길이로 잡혀 있고 count 만큼만 유효하다. SetPositions 는 배열 전체를 요구하므로
        // 길이가 어긋나면 에러가 나거나 쓰레기 좌표가 그려진다 — 유효 구간만 개별로 밀어 넣는다.
        line.positionCount = count;
        for (int i = 0; i < count; i++)
        {
            line.SetPosition(i, points[i]);
        }

        if (scrollSpeed != 0f)
        {
            // 점선이 진행 방향으로 흐르게 한다. sharedMaterial 이 아니라 material 인 이유는 그쪽을 건드리면
            // 에셋 자체가 바뀌어 다른 플레이어의 궤적까지 함께 흐르고, 에디터에서는 .mat 파일이 더러워지기 때문이다.
            // 임시 단색 머티리얼에는 텍스처가 없어 지금은 눈에 띄는 변화가 없다 — 점선 텍스처를 물리면 그때부터 흐른다.
            line.material.mainTextureOffset = new Vector2(-Time.time * scrollSpeed, 0f);
        }

        // 벽을 넘겨 하늘로 날아가는 조준이면 착지점이 없다 — 마커만 감추고 궤적선은 그대로 둔다.
        bool hasImpact = impact.collider != null;
        landingMarker.gameObject.SetActive(hasImpact);
        if (!hasImpact) return;

        landingMarker.SetPositionAndRotation(
            impact.point + impact.normal * 0.02f, // 바닥과 z-fighting 하지 않도록 살짝 띄운다
            Quaternion.LookRotation(impact.normal));
    }

    /// <summary>궤적선과 착지 마커의 표시 여부를 한 번에 바꾼다.</summary>
    /// <param name="visible">표시할지 여부.</param>
    private void SetVisible(bool visible)
    {
        // 표시물은 첫 조준 때 만들어진다(EnsureVisuals) — 그 전에 불리는 Awake·OnDisable 경로에서는 아직 없다.
        if (line == null || landingMarker == null) return;

        line.enabled = visible;
        landingMarker.gameObject.SetActive(false); // 마커는 착지점을 찾은 프레임에만 켠다
        if (!visible) line.positionCount = 0;
    }
}
