using System.Collections.Generic;
using Border.Core;
using EmptyHouse.MapGen.Runtime;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 안내도 패널(EH-62) — M 토글로 절차 생성 맵의 도식(방 = 사각형)을 오버레이로 보여준다.
/// 이동 비차단(조작상호작용UI 키맵 M — 오버레이, 시야 가림은 의도된 리스크).
/// 데이터는 <see cref="MapOverviewEventChannelSO"/> 로만 받는다 — 씬 오브젝트(드라이버) 직참조 금지.
/// 마커: 플레이어(자기+팀원) = 흰 원 전원 동일 · 시야 내 좀비만 빨간 원(ZombieSightProbe).
/// 밝힘: 현재 바라보는 방향 부채꼴만(UIMapViewCone) — 방문 기억·탐험 안개 없음(상태 저장 0).
/// 다층은 **내가 서 있는 층만** 그린다 — 층 전환 조작(S8)은 이후 카드.
/// 화면 룩은 Figma 정본 — 여기는 규칙·구조만.
/// </summary>
public sealed class UIMapOverview : MonoBehaviour
{
    private const int NoFloor = int.MinValue; // 층 미확정 — 첫 LateUpdate 가 플레이어 층을 잡아 재구축하게 만드는 표식

    [Header("Input / Channels")]
    [SerializeField] private InputReader inputReader; // M 토글 이벤트 원천(프로젝트 에셋)
    [SerializeField] private MapOverviewEventChannelSO onMapOverviewReady; // 조립 완료 시 모델 수신(클라 로컬)

    [Header("Layout")]
    [SerializeField] private GameObject panelRoot; // 개폐 대상 컨테이너(자식) — 이 컴포넌트는 항상 활성이어야 토글 입력을 받는다
    [SerializeField] private RectTransform roomsRoot; // 방 사각형 컨테이너(자식) — 셀→패널 스케일 적용 대상
    [SerializeField] private Image roomRectTemplate; // 방 사각형 템플릿(자식, 비활성) — 풀 원본
    [SerializeField] private Color roomColor = Color.gray; // 방 사각형 색 ⚪(Figma 확정 전 임시)
    [SerializeField] private Color corridorColor = new Color(0.32f, 0.32f, 0.32f, 1f); // 복도 사각형 색 ⚪ — 방보다 어둡게, 단 불투명(반투명이면 어둠 위에서 사라진다)

    [Header("Markers")]
    [SerializeField] private RectTransform markersRoot; // 마커 컨테이너(자식) — 어둠 마스크 위 레이어(항상 보임)
    [SerializeField] private Image markerTemplate; // 원형 마커 템플릿(자식, 비활성) — 풀 원본
    [SerializeField] private Color playerColor = Color.white; // 플레이어(자기+팀원 동일) — 요구 3·8
    [SerializeField] private Color zombieColor = Color.red; // 시야 내 좀비 — 요구 2·3
    [SerializeField] private ZombieSightProbe sightProbe; // 시야 내 좀비 판정(자식 컴포넌트)

    [Header("View Cone")]
    [SerializeField] private UIMapViewCone viewCone; // 방향 밝힘 부채꼴(자식 컴포넌트) — 요구 5

    [Header("Tuning ⚪ — 안내도 파라미터 단일 소유(자식 컴포넌트에 주입)")]
    [SerializeField] private float sightRangeMeters = 30f; // 좀비 표시 사거리(m) = 밝힘 부채꼴 반경. 밝은 영역이 곧 "좀비가 보이는 범위"라 한 값으로 묶는다
    [SerializeField] private float sightLingerSeconds = 0.5f; // 좀비 마커 잔상(s) — 시야를 벗어나도 이만큼 유지(경계 깜빡임 방지). 키우면 늦게 사라진다
    [SerializeField] private float zombieChestHeightMeters = 1.2f; // 차폐 Linecast 목표 높이(m) — 발밑은 바닥에 먹혀 항상 가려진다
    [SerializeField] private LayerMask sightOcclusionMask; // 차폐 레이어(Wall·Door) — 벽 뒤 좀비 미표시(요구 2)
    [SerializeField] private float viewConeAngleDegrees = 70f; // 밝힘 부채꼴 각도(도)
    [SerializeField] private float outsideVisibility = 0.1f; // 부채꼴 밖 잔여 가시성(0 = 완전 암전)

    private MapOverviewModel model; // 수신한 도식 모델(맵마다 교체)
    private readonly List<Image> roomPool = new List<Image>(); // 방 사각형 풀(방 60+ GC 0)
    private readonly List<Image> markerPool = new List<Image>(); // 마커 풀(플레이어+좀비 공용)
    private readonly List<Vector3> zombieBuffer = new List<Vector3>(); // 시야 내 좀비 좌표 수신 버퍼(재사용 — GC 0)
    private bool isOpen; // 패널 개폐 상태 — 열림 중에만 마커 갱신(Update 게이트)
    private int currentFloor = NoFloor; // 지금 그리고 있는 층 — 플레이어가 층을 옮기면 재구축한다
    private float cellToPanel; // 셀 → 패널 픽셀 배율(등방) — 전 층 공통(XZ 정규화가 전역이라 층을 갈아도 안 바뀐다)
    private Vector2 mapCenterCells; // 맵 중심(셀) — 패널 중앙 정렬 기준점
    private Camera cachedCamera; // 로컬 메인 카메라 캐시 — 관전 전환으로 갈리면 다시 찾는다

    /// <summary>토글 입력·모델 채널 구독.</summary>
    private void OnEnable()
    {
        Log.D("[UIMapOverview] OnEnable");

        inputReader.MapOverviewEvent += HandleToggle;
        onMapOverviewReady.OnEventRaised += HandleMapOverviewReady;
        PushTuning();
        roomRectTemplate.gameObject.SetActive(false);
        markerTemplate.gameObject.SetActive(false);
        SetOpen(false);
    }

    /// <summary>구독 해제.</summary>
    private void OnDisable()
    {
        Log.D("[UIMapOverview] OnDisable");

        inputReader.MapOverviewEvent -= HandleToggle;
        onMapOverviewReady.OnEventRaised -= HandleMapOverviewReady;
    }

    /// <summary>모델 수신 — 방 사각형을 재구축하고 패널은 닫힌 상태로 초기화한다(외출마다 맵이 바뀐다).</summary>
    /// <param name="readyModel">빌드된 안내도 모델.</param>
    private void HandleMapOverviewReady(MapOverviewModel readyModel)
    {
        Log.D($"[UIMapOverview] HandleMapOverviewReady 방={readyModel.Rooms.Count}");

        model = readyModel;
        currentFloor = NoFloor; // 층은 플레이어 위치가 정한다 — 첫 갱신에서 잡힌다
        RebuildRooms();
        SetOpen(false);
    }

    /// <summary>M 토글(요구 6) — 열기/닫기. 모델 미수신 상태(맵 조립 전)면 무시한다.</summary>
    private void HandleToggle()
    {
        Log.D("[UIMapOverview] HandleToggle");

        if (model == null)
        {
            return; // 맵 조립 전 — 그릴 것이 없다
        }

        SetOpen(!isOpen);
    }

    /// <summary>패널 개폐를 적용한다 — 이 컴포넌트 자신은 계속 활성(토글 입력 수신).</summary>
    /// <param name="open">열림 여부.</param>
    private void SetOpen(bool open)
    {
        isOpen = open;
        panelRoot.SetActive(open);
    }

    /// <summary>
    /// 방 사각형 재구축 — 모델의 셀 사각형을 패널 크기에 맞춰 스케일해 풀에서 배치한다(요구 1·4).
    /// 그리는 대상은 <see cref="currentFloor"/> 한 층뿐 — 층 전환 UI(S8)는 이후 카드.
    /// </summary>
    private void RebuildRooms()
    {
        // 등방 스케일 — 가로/세로 중 더 빡빡한 쪽에 맞춰야 맵이 패널 밖으로 나가지 않는다.
        // 배율은 전 층 공통 좌표계 기준이라 층을 갈아도 계단·방 위치가 층 사이에서 어긋나지 않는다
        Vector2 panelSize = roomsRoot.rect.size;
        mapCenterCells = model.CellSize * 0.5f;
        cellToPanel = Mathf.Min(panelSize.x / model.CellSize.x, panelSize.y / model.CellSize.y);

        // 밝힘 반경 = 좀비 표시 사거리(같은 길이) — 도식 축척으로 환산해 내려보낸다
        viewCone.SetRadius(model.MetersToCells(sightRangeMeters) * cellToPanel);

        int used = 0;
        int corridors = 0;
        for (int i = 0; i < model.Rooms.Count; i++)
        {
            MapOverviewRoomRect room = model.Rooms[i];
            if (room.FloorIndex != currentFloor)
            {
                continue; // 다른 층 — 내가 선 층만 그린다
            }

            Image rect = TakeFromPool(roomPool, roomRectTemplate, roomsRoot, used++);
            rect.rectTransform.sizeDelta = room.CellRect.size * cellToPanel;
            rect.rectTransform.anchoredPosition = CellToPanelPosition(room.CellRect.center);
            rect.color = room.IsCorridor ? corridorColor : roomColor;
            if (room.IsCorridor)
            {
                corridors++;
            }
        }

        HidePoolFrom(roomPool, used);
        Log.D($"[UIMapOverview] RebuildRooms 층={currentFloor} 사각형={used}(복도 {corridors})");
    }

    /// <summary>튜닝 파라미터를 자식 컴포넌트에 주입한다 — 값의 단일 소유자는 이 컴포넌트다.</summary>
    private void PushTuning()
    {
        sightProbe.Configure(sightRangeMeters, sightLingerSeconds, zombieChestHeightMeters, sightOcclusionMask);
        viewCone.Configure(viewConeAngleDegrees, outsideVisibility);
    }

    /// <summary>인스펙터에서 값을 바꾸면 자식에 즉시 반영한다 — 플레이 중이 아니어도 확인할 수 있다.</summary>
    private void OnValidate()
    {
        if (sightProbe == null || viewCone == null)
        {
            return; // 프리팹 조립 중 — 참조가 채워지기 전
        }

        PushTuning();
    }

    /// <summary>셀 공간 좌표를 패널 로컬 좌표로 옮긴다 — 맵 중심이 컨테이너 중앙에 오게 정렬한다.</summary>
    /// <param name="cell">셀 공간 좌표.</param>
    /// <returns>패널 로컬 좌표.</returns>
    private Vector2 CellToPanelPosition(Vector2 cell)
    {
        // 마커 수만큼 매 프레임 호출되므로 진입 트레이스를 두지 않는다.
        return (cell - mapCenterCells) * cellToPanel;
    }

    /// <summary>풀에서 index 번째 요소를 꺼낸다 — 모자라면 템플릿을 복제해 채운다.</summary>
    /// <param name="pool">대상 풀.</param>
    /// <param name="template">복제 원본(비활성 자식).</param>
    /// <param name="parent">복제본 부모.</param>
    /// <param name="index">요청 인덱스.</param>
    /// <returns>활성화된 풀 요소.</returns>
    private Image TakeFromPool(List<Image> pool, Image template, RectTransform parent, int index)
    {
        // 매 프레임 호출되므로 진입 트레이스를 두지 않는다.
        while (pool.Count <= index)
        {
            Image created = Instantiate(template, parent);
            created.name = $"{template.name}_{pool.Count}";
            pool.Add(created);
        }

        Image element = pool[index];
        element.gameObject.SetActive(true);
        return element;
    }

    /// <summary>이번 프레임에 쓰지 않은 풀 요소를 비활성화한다.</summary>
    /// <param name="pool">대상 풀.</param>
    /// <param name="usedCount">사용한 개수.</param>
    private void HidePoolFrom(List<Image> pool, int usedCount)
    {
        // 매 프레임 호출되므로 진입 트레이스를 두지 않는다.
        for (int i = usedCount; i < pool.Count; i++)
        {
            pool[i].gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// 열림 중 매 프레임 마커·부채꼴 갱신 — 플레이어(자기+팀원) 흰 원, 시야 내 좀비 빨간 원,
    /// 부채꼴은 로컬 카메라 요를 따라 회전(요구 2·3·5·8). 닫힘 중에는 아무것도 하지 않는다.
    /// </summary>
    private void LateUpdate()
    {
        // 매 프레임 호출되므로 진입 트레이스를 두지 않는다.
        if (!isOpen)
        {
            return;
        }

        if (cachedCamera == null)
        {
            cachedCamera = Camera.main; // 관전 전환·리스폰으로 카메라가 갈릴 때만 재탐색
        }

        // 기준점 = 자기 폰(관전 중이면 관전 카메라). 그리는 층도 마커 필터도 이 지점이 정한다
        NetworkObject localPlayer = NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject();
        Vector3 focusWorld = localPlayer != null ? localPlayer.transform.position : cachedCamera.transform.position;

        int floor = model.WorldToFloor(focusWorld);
        if (floor != currentFloor)
        {
            currentFloor = floor; // 계단으로 층을 옮겼다 — 그 층 도식으로 갈아끼운다
            RebuildRooms();
        }

        int used = 0;

        // 플레이어 — 자기·팀원 구분 없이 전원 흰 원(요구 3·8). 클라에서도 채워지는 PlayerObjects 가 유일한 열거 경로(UISpectator 와 동일)
        System.Collections.Generic.IReadOnlyList<NetworkObject> players = NetworkManager.Singleton.SpawnManager.PlayerObjects;
        for (int i = 0; i < players.Count; i++)
        {
            NetworkObject player = players[i];
            if (player == null || player.GetComponent<PlayerDeathHandler>().IsDead.Value)
            {
                continue; // despawn 경합 · 사망자는 맵에 없는 것으로 본다
            }

            TryPlaceMarker(ref used, player.transform.position, playerColor);
        }

        // 좀비 — 시야 판정을 통과한 것만(요구 2). 벽 뒤·시야 밖은 프로브가 이미 걸렀다
        sightProbe.CollectVisible(zombieBuffer);
        for (int i = 0; i < zombieBuffer.Count; i++)
        {
            TryPlaceMarker(ref used, zombieBuffer[i], zombieColor);
        }

        HidePoolFrom(markerPool, used);

        viewCone.SetPose(
            CellToPanelPosition(model.WorldToCell(focusWorld)),
            model.WorldYawToCellAngle(cachedCamera.transform.eulerAngles.y));
    }

    /// <summary>대상이 지금 그리는 층에 있으면 마커를 배치한다 — 다른 층이면 아무것도 하지 않는다.</summary>
    /// <param name="used">소비한 마커 수(배치 시 증가).</param>
    /// <param name="worldPosition">대상 월드 좌표.</param>
    /// <param name="color">마커 색(흰 = 플레이어 · 빨강 = 좀비).</param>
    private void TryPlaceMarker(ref int used, Vector3 worldPosition, Color color)
    {
        // 마커 수만큼 매 프레임 호출되므로 진입 트레이스를 두지 않는다.
        if (model.WorldToFloor(worldPosition) != currentFloor)
        {
            return; // 다른 층 대상 — 이 층 도식에 찍으면 위치를 오해한다
        }

        Image marker = TakeFromPool(markerPool, markerTemplate, markersRoot, used++);
        marker.rectTransform.anchoredPosition = CellToPanelPosition(model.WorldToCell(worldPosition));
        marker.color = color;
    }
}
