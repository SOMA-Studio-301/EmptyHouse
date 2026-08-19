using Border.Core;
using EmptyHouse.NoiseSystem;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 데시벨 미터 게이지 (소음시스템.md 9-1 ① 바). 씬 레벨 Canvas-HUD 의 게임플레이 패널 밑에 붙는다.
/// 세그먼트를 아래에서 위로 채우며 초록 → 노랑 → 빨강으로 물든다.
/// 판단을 하지 않는 순수 표시자다 — 채널이 밀어넣는 발생 dB 를 세그먼트에 반영할 뿐, 네트워크도 소음 규칙도 알지 못한다
/// (<see cref="UIDisguiseGauge"/> 와 같은 형태).
/// <b>표시 스무딩은 두지 않는다.</b> 스펙 미결 항목 Q4(지속 소음 감쇠 곡선)를 "즉시"로 구현한 것이다.
/// 상승·하강 모두 채널이 준 값을 그 프레임에 그대로 그린다 —
/// 서버가 이동을 지속 레벨로 다뤄 발생 dB 자체가 이미 즉시 오르내리므로, 여기서 완만하게 만들면 그 즉시성을 도로 깎을 뿐이다.
/// 색 경계는 임의의 3등분이 아니라 규칙에 앵커한다 — 20 = 위장 해제 임계(disguise_break_db) · 45 = Watcher 가청 임계(hear_min_watcher, 모든 타입이 듣기 시작하는 선).
/// ⚠ 이 색이 뜻하는 것은 <b>절대 소음 크기</b>지 위험도가 아니다. 실제 위험선은 좀비 거리·차폐로 실시간 움직이는 가청선(9-1 ②)이며 아직 미구현이다 —
///   가청선·위장 해제선·무전기 강조를 얹을 때 이 램프 위에 마커로 올리고, 램프 자체를 위험도로 바꾸지 말 것.
/// </summary>
public class UINoiseMeter : MonoBehaviour
{
    [Header("Channels")]
    [SerializeField] private NoiseMeterLevelChangedEventChannelSO noiseMeterLevelChanged; // 구독: 로컬 플레이어 발생 dB 방송. 늦게 켜졌을 때의 초기값은 CurrentDb 로 받는다

    [Header("Segments")]
    [SerializeField] private Image segmentPrefab; // 칸 하나의 원본. segmentParent 밑에 segmentCount 개 복제된다
    [SerializeField] private Transform segmentParent; // 칸이 쌓이는 부모. 인덱스 0 이 맨 아래가 되도록 레이아웃을 역순으로 설정해 둔다
    [Min(1)] [SerializeField] private int segmentCount = 24; // 칸 수. 시안 기준 24

    [Header("Scale")]
    [Min(1f)] [SerializeField] private float maxDb = 70f; // 만땅 기준. 고함(70) = 플레이어 단독 최대. 무전 합산 초과분은 최상단에서 클램프된다
    [Min(0f)] [SerializeField] private float cautionDb = 20f; // 초록 → 노랑 경계 = 위장 해제 임계
    [Min(0f)] [SerializeField] private float dangerDb = 45f; // 노랑 → 빨강 경계 = Watcher 가청 임계

    [Header("Colors")]
    [SerializeField] private Color safeColor = new Color(0.35f, 0.82f, 0.36f, 1f); // 초록 — cautionDb 미만 구간
    [SerializeField] private Color cautionColor = new Color(0.95f, 0.82f, 0.25f, 1f); // 노랑 — cautionDb ~ dangerDb 구간
    [SerializeField] private Color dangerColor = new Color(0.90f, 0.25f, 0.22f, 1f); // 빨강 — dangerDb 이상 구간
    [SerializeField] private Color emptyColor = new Color(1f, 1f, 1f, 0.15f); // 아직 차오르지 않은 칸의 바탕색

    // 생성된 칸 뷰. 인덱스 0 이 최하단이다
    private Image[] segments;

    // 직전에 그린 채움 칸 수. 값이 실제로 바뀐 프레임에만 위젯을 건드린다. -1 은 "아직 한 번도 안 그림"
    private int renderedFilledCount = -1;

    /// <summary>칸을 만들어 둔다. 구독보다 먼저 실행돼야 초기 표시가 빈 배열에 부딪히지 않는다.</summary>
    private void Awake()
    {
        BuildSegments();
    }

    /// <summary>채널을 구독하고, 이미 발행된 마지막 dB 로 초기 표시를 맞춘다(늦은 구독자 동기화).</summary>
    private void OnEnable()
    {
        noiseMeterLevelChanged.OnEventRaised += HandleLevelChanged;

        // 늦게 켜진 HUD 는 다음 표본까지 기다리지 않고 마지막 발행값에서 시작한다.
        Render(noiseMeterLevelChanged.CurrentDb);
    }

    /// <summary>구독을 해제한다. 채널은 SO 라 씬 밖에서 살아남으므로 죽은 델리게이트를 남기지 않는다.</summary>
    private void OnDisable()
    {
        noiseMeterLevelChanged.OnEventRaised -= HandleLevelChanged;
    }

    /// <summary>방송된 발생 dB 를 그 자리에서 그린다. 상승·하강 모두 지연을 두지 않는다.</summary>
    /// <param name="db">현재 발생 dB(합산, 무전 수신 포함).</param>
    private void HandleLevelChanged(float db)
    {
        // 발생 dB 가 바뀔 때마다 호출되므로 진입 트레이스를 두지 않는다.
        Render(db);
    }

    /// <summary>
    /// segmentPrefab 을 segmentCount 개 복제해 <see cref="segments"/> 를 채운다. 원본 프리팹 인스턴스는 화면에 남기지 않는다.
    /// 부모에 미리 놓인 칸을 먼저 비우는 이유: 저작 시안용으로 남겨둔 칸은 <see cref="segments"/> 밖이라
    /// <see cref="Render"/> 가 색을 칠하지 않고, 역순 레이아웃에서 게이지 아래를 차지해 기준선을 밀어 올린다.
    /// </summary>
    private void BuildSegments()
    {
        ClearAuthoredSegments();

        segments = new Image[segmentCount];
        for (int i = 0; i < segmentCount; i++)
        {
            Image segment = Instantiate(segmentPrefab, segmentParent);
            segment.name = $"Segment_{i}";
            segments[i] = segment;
        }

        // 씬에 놓인 칸을 원본으로 지정했다면 그 원본이 칸 하나로 더 세어진다 — 프로젝트 프리팹이면 씬에 없으므로 해당 없다.
        if (segmentPrefab.gameObject.scene.IsValid()) segmentPrefab.gameObject.SetActive(false);
    }

    /// <summary>부모에 미리 놓여 있던 칸을 모두 지운다. 뒤에서부터 도는 이유는 파괴가 인덱스를 밀지 않게 하기 위해서다.</summary>
    private void ClearAuthoredSegments()
    {
        for (int i = segmentParent.childCount - 1; i >= 0; i--)
        {
            GameObject authored = segmentParent.GetChild(i).gameObject;

            // Destroy 는 프레임 끝에 처리돼 이번 프레임 레이아웃이 아직 이 칸을 센다 — 먼저 꺼서 자리부터 뺀다.
            authored.SetActive(false);
            Destroy(authored);
        }
    }

    /// <summary>
    /// 발생 dB 를 게이지에 반영한다. 채울 칸 수 = round(db / maxDb × segmentCount) 이며 maxDb 초과분은 만땅으로 클램프된다.
    /// </summary>
    /// <param name="db">표시할 발생 dB. 채널이 준 값을 가공 없이 받는다.</param>
    private void Render(float db)
    {
        // 발생 dB 가 바뀔 때마다 호출되므로 진입 트레이스를 두지 않는다.
        int filledCount = Mathf.Clamp(Mathf.RoundToInt(db / maxDb * segmentCount), 0, segmentCount);
        if (filledCount == renderedFilledCount) return;

        renderedFilledCount = filledCount;

        for (int i = 0; i < segments.Length; i++)
        {
            segments[i].color = i < filledCount ? ResolveSegmentColor(i) : emptyColor;
        }
    }

    /// <summary>칸 인덱스가 차지하는 dB 구간을 색 경계와 비교해 그 칸의 색을 고른다.</summary>
    /// <param name="segmentIndex">칸 인덱스(0 = 최하단).</param>
    /// <returns>그 칸이 채워졌을 때 칠할 색.</returns>
    private Color ResolveSegmentColor(int segmentIndex)
    {
        // 칸 수만큼 호출되므로 진입 트레이스를 두지 않는다.
        float segmentTopDb = (segmentIndex + 1) / (float)segmentCount * maxDb;

        if (segmentTopDb >= dangerDb) return dangerColor;
        if (segmentTopDb >= cautionDb) return cautionColor;
        return safeColor;
    }
}
