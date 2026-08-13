using System.Collections.Generic;
using Border.Core;
using EmptyHouse.MapGen.Runtime;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 안내도 패널(EH-62) — M 토글로 절차 생성 맵의 도식(방 = 사각형)을 오버레이로 보여준다.
/// 이동 비차단(조작상호작용UI 키맵 M — 오버레이, 시야 가림은 의도된 리스크).
/// 데이터는 <see cref="MapOverviewEventChannelSO"/> 로만 받는다 — 씬 오브젝트(드라이버) 직참조 금지.
/// 마커: 플레이어(자기+팀원) = 흰 원 전원 동일 · 시야 내 좀비만 빨간 원(ZombieSightProbe).
/// 밝힘: 현재 바라보는 방향 부채꼴만(UIMapViewCone) — 방문 기억·탐험 안개 없음(상태 저장 0).
/// 화면 룩은 Figma 정본 — 여기는 규칙·구조만.
/// </summary>
public sealed class UIMapOverview : MonoBehaviour
{
    [Header("Input / Channels")]
    [SerializeField] private InputReader inputReader; // M 토글 이벤트 원천(프로젝트 에셋)
    [SerializeField] private MapOverviewEventChannelSO onMapOverviewReady; // 조립 완료 시 모델 수신(클라 로컬)

    [Header("Layout")]
    [SerializeField] private RectTransform roomsRoot; // 방 사각형 컨테이너(자식) — 셀→패널 스케일 적용 대상
    [SerializeField] private Image roomRectTemplate; // 방 사각형 템플릿(자식, 비활성) — 풀 원본
    [SerializeField] private Color roomColor = Color.gray; // 방 사각형 색 ⚪(Figma 확정 전 임시)
    [SerializeField] private Color corridorColor = Color.gray * 0.6f; // 복도 사각형 색 ⚪

    [Header("Markers")]
    [SerializeField] private RectTransform markersRoot; // 마커 컨테이너(자식) — 어둠 마스크 위 레이어(항상 보임)
    [SerializeField] private Image markerTemplate; // 원형 마커 템플릿(자식, 비활성) — 풀 원본
    [SerializeField] private Color playerColor = Color.white; // 플레이어(자기+팀원 동일) — 요구 3·8
    [SerializeField] private Color zombieColor = Color.red; // 시야 내 좀비 — 요구 2·3
    [SerializeField] private ZombieSightProbe sightProbe; // 시야 내 좀비 판정(자식 컴포넌트)

    [Header("View Cone")]
    [SerializeField] private UIMapViewCone viewCone; // 방향 밝힘 부채꼴(자식 컴포넌트) — 요구 5

    private MapOverviewModel model; // 수신한 도식 모델(맵마다 교체)
    private readonly List<Image> roomPool = new List<Image>(); // 방 사각형 풀(방 60+ GC 0)
    private readonly List<Image> markerPool = new List<Image>(); // 마커 풀(플레이어+좀비 공용)
    private bool isOpen; // 패널 개폐 상태 — 열림 중에만 마커 갱신(Update 게이트)

    /// <summary>토글 입력·모델 채널 구독.</summary>
    private void OnEnable()
    {
        // TODO(impl):
        Log.D("[UIMapOverview] OnEnable");
    }

    /// <summary>구독 해제.</summary>
    private void OnDisable()
    {
        // TODO(impl):
        Log.D("[UIMapOverview] OnDisable");
    }

    /// <summary>모델 수신 — 방 사각형을 재구축하고 패널은 닫힌 상태로 초기화한다(외출마다 맵이 바뀐다).</summary>
    /// <param name="readyModel">빌드된 안내도 모델.</param>
    private void HandleMapOverviewReady(MapOverviewModel readyModel)
    {
        // TODO(impl):
        Log.D("[UIMapOverview] HandleMapOverviewReady");
    }

    /// <summary>M 토글(요구 6) — 열기/닫기. 모델 미수신 상태(맵 조립 전)면 무시한다.</summary>
    private void HandleToggle()
    {
        // TODO(impl):
        Log.D("[UIMapOverview] HandleToggle");
    }

    /// <summary>
    /// 방 사각형 재구축 — 모델의 셀 사각형을 패널 크기에 맞춰 스케일해 풀에서 배치한다(요구 1·4).
    /// v1 은 단층 — 층 필터는 S8(층 전환 UI)에서.
    /// </summary>
    private void RebuildRooms()
    {
        // TODO(impl):
        Log.D("[UIMapOverview] RebuildRooms");
    }

    /// <summary>
    /// 열림 중 매 프레임 마커·부채꼴 갱신 — 플레이어(자기+팀원) 흰 원, 시야 내 좀비 빨간 원,
    /// 부채꼴은 로컬 카메라 요를 따라 회전(요구 2·3·5·8). 닫힘 중에는 아무것도 하지 않는다.
    /// </summary>
    private void LateUpdate()
    {
        // TODO(impl): isOpen 게이트 → 플레이어 열거·WorldToCell 변환·마커 풀 배치 → sightProbe 결과 배치 → viewCone.SetPose
        // 플레이어(팀원) 열거 경로는 구현 시 확정 — 클라 전용 플레이어 레지스트리 부재 시 보고(§3.5)
        if (!isOpen)
        {
            return;
        }

        Log.D("[UIMapOverview] LateUpdate(open)");
    }
}
