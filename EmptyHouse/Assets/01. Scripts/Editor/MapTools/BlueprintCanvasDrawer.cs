using System;
using System.Collections.Generic;
using Border.Core;
using EmptyHouse.MapGen.Core;
using UnityEditor;
using UnityEngine;

namespace EmptyHouse.MapGen.Editor
{
    /// <summary>
    /// 블루프린트 캔버스 렌더러(10절) — 그래프 뷰(방 노드·간선)·위험 등급 그라데이션·스폰 오버레이·
    /// 배회/청각 반경 원을 셀 격자 위에 그린다. 팬·줌 뷰 상태를 보유한다.
    /// 좌표 변환은 CellMath 공유 — 생성기와 다른 해석 금지(AC-21 과 같은 근거).
    /// 깊이(DangerGradeCalculator)·템플릿 조회는 블루프린트 단위로 캐시한다 — 리페인트마다 재계산 금지.
    /// </summary>
    [Serializable]
    public sealed class BlueprintCanvasDrawer
    {
        /// <summary>오버레이 토글·근사 파라미터 묶음 — 윈도우가 리페인트마다 구성해 넘긴다.</summary>
        public struct OverlayOptions
        {
            public bool ShowDanger; // 위험 등급 그라데이션(10절)
            public bool ShowSpawns; // 스폰 오버레이(10절)
            public bool ShowWanderRadii; // 배회 반경 원(10절)
            public bool ShowHearingRadii; // 청각 반경 근사 원(10절 — 시각화 전용)
            public int SpawnKindFilterMask; // SpawnKind 비트 필터(~0 = 전체)
            public float AssumedCellMeters; // 셀 실측 m 가정치(G1 미확정 — 근사 전용)
            public float HearingRefDb; // 기준 소음 dB(10절 예시 = 투척물 50)
            public int FloorFilter; // 층 필터 — int.MinValue = 전체, 그 외 = 해당 층 서수만 표시(M9 다층)
            public bool ShowFloorLabels; // 층 섬 라벨(1F/2F/B1) 표시 — 다층 펼침 전용
        }

        private const float minZoom = 3f; // 줌 하한(px/셀)
        private const float maxZoom = 80f; // 줌 상한(px/셀)
        private const float gridLabelZoom = 8f; // 이 줌 이상에서만 격자·방 라벨 표시(저줌 노이즈 방지)
        private const float hearingThresholdDb = 30f; // 가청 임계 근사(Walker 30 ⚪) — 정본은 Data/zombie.csv
        private const float falloffDbPerMeter = 1.5f; // 거리 감쇠(소음시스템 D2) — 정본은 Data/noise.csv

        [SerializeField] private Vector2 panOffset; // 캔버스 팬 오프셋(픽셀)
        [SerializeField] private float zoom = 1f; // 줌 배율(셀 → 픽셀 스케일 계수)

        [NonSerialized] private MapBlueprint cachedBlueprint; // 캐시 기준 블루프린트(참조 비교)
        [NonSerialized] private int[] cachedDepths; // 방별 입구 깊이 캐시
        [NonSerialized] private int cachedMaxDepth; // 최대 깊이(그라데이션 분모)
        [NonSerialized] private List<RoomTemplateDef> cachedRoomTemplates; // 방 인덱스 → 템플릿 캐시
        [NonSerialized] private bool fitPending = true; // 다음 리페인트에서 팬·줌 맞춤 실행
        [NonSerialized] private GUIStyle badgeStyle; // 스폰 배지 라벨 스타일(지연 생성)
        [NonSerialized] private GUIStyle roomLabelStyle; // 방 인덱스 라벨 스타일(지연 생성)
        [NonSerialized] private GUIStyle tooltipStyle; // 호버 툴팁 스타일(지연 생성)

        /// <summary>캔버스 한 프레임을 그린다 — 격자 → 방(위험 색) → 간선 → 스폰 → 반경 원 → 호버 툴팁 순.</summary>
        /// <param name="canvasRect">캔버스 영역(윈도우 좌표).</param>
        /// <param name="blueprint">그릴 블루프린트(null 이면 빈 격자와 안내만).</param>
        /// <param name="templates">생성에 사용한 템플릿 목록 — 풋프린트·소켓·마커 조회.</param>
        /// <param name="options">오버레이 토글·근사 파라미터.</param>
        public void Draw(Rect canvasRect, MapBlueprint blueprint, IReadOnlyList<RoomTemplateDef> templates, OverlayOptions options)
        {
            HandleNavigation(canvasRect);
            EnsureStyles();

            GUI.BeginClip(canvasRect);
            var localRect = new Rect(0f, 0f, canvasRect.width, canvasRect.height);
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(localRect, new Color(0.12f, 0.12f, 0.14f));
            }

            if (blueprint == null || blueprint.Rooms.Count == 0 || templates == null)
            {
                if (Event.current.type == EventType.Repaint)
                {
                    DrawGrid(localRect);
                    GUI.Label(localRect, "생성 버튼을 눌러 미리보기를 만드세요", EditorStyles.centeredGreyMiniLabel);
                }

                GUI.EndClip();
                return;
            }

            RefreshCaches(blueprint, templates);
            if (Event.current.type == EventType.Repaint)
            {
                if (fitPending)
                {
                    FitToContent(localRect, blueprint);
                    fitPending = false;
                }

                DrawGrid(localRect);
                DrawRooms(localRect, blueprint, templates, options, cachedDepths);
                DrawEdges(localRect, blueprint, templates, options);
                if (options.ShowFloorLabels)
                {
                    DrawFloorLabels(localRect, blueprint, options);
                }

                if (options.ShowWanderRadii)
                {
                    DrawWanderCircles(localRect, blueprint, templates, options);
                }

                if (options.ShowHearingRadii)
                {
                    DrawHearingCircles(localRect, blueprint, templates, options);
                }

                if (options.ShowSpawns)
                {
                    DrawSpawnOverlay(localRect, blueprint, templates, options);
                }

                DrawHoverTooltip(localRect, blueprint, templates, cachedDepths);
                DrawLegend(localRect);
            }

            GUI.EndClip();
        }

        /// <summary>다음 Draw 에서 블루프린트 전체가 화면에 들어오도록 팬·줌 맞춤을 예약한다 — 생성 직후 호출.</summary>
        public void RequestFit()
        {
            Log.D("[BlueprintCanvasDrawer] RequestFit");
            fitPending = true;
        }

        /// <summary>드래그 팬·휠 줌 입력을 처리한다.</summary>
        /// <param name="canvasRect">캔버스 영역.</param>
        private void HandleNavigation(Rect canvasRect)
        {
            Event evt = Event.current;
            if (!canvasRect.Contains(evt.mousePosition))
            {
                return;
            }

            if (evt.type == EventType.MouseDrag && (evt.button == 1 || evt.button == 2))
            {
                panOffset += evt.delta;
                evt.Use();
            }
            else if (evt.type == EventType.ScrollWheel)
            {
                float oldZoom = zoom;
                zoom = Mathf.Clamp(zoom * (1f - evt.delta.y * 0.04f), minZoom, maxZoom);

                // 커서 아래 셀이 고정되도록 팬 보정(클립 로컬 좌표 기준)
                Vector2 local = evt.mousePosition - canvasRect.position;
                panOffset = local - (local - panOffset) * (zoom / oldZoom);
                evt.Use();
            }
        }

        /// <summary>블루프린트가 바뀌었으면 깊이·템플릿 캐시를 재구축한다(참조 비교 — 리페인트마다 재계산 금지).</summary>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="templates">템플릿 목록.</param>
        private void RefreshCaches(MapBlueprint blueprint, IReadOnlyList<RoomTemplateDef> templates)
        {
            if (ReferenceEquals(cachedBlueprint, blueprint) && cachedRoomTemplates != null)
            {
                return;
            }

            cachedBlueprint = blueprint;
            cachedDepths = DangerGradeCalculator.ComputeHopDistances(blueprint);
            cachedMaxDepth = 1;
            for (int r = 0; r < cachedDepths.Length; r++)
            {
                if (cachedDepths[r] > cachedMaxDepth)
                {
                    cachedMaxDepth = cachedDepths[r];
                }
            }

            cachedRoomTemplates = new List<RoomTemplateDef>(blueprint.Rooms.Count);
            for (int r = 0; r < blueprint.Rooms.Count; r++)
            {
                RoomTemplateDef found = null;
                for (int t = 0; t < templates.Count; t++)
                {
                    if (templates[t].TemplateId == blueprint.Rooms[r].TemplateId)
                    {
                        found = templates[t];
                        break;
                    }
                }

                cachedRoomTemplates.Add(found);
            }
        }

        /// <summary>블루프린트 전체 바운드에 맞춰 팬·줌을 재설정한다(여백 10%).</summary>
        /// <param name="localRect">클립 로컬 캔버스 영역.</param>
        /// <param name="blueprint">대상 블루프린트.</param>
        private void FitToContent(Rect localRect, MapBlueprint blueprint)
        {
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            for (int r = 0; r < blueprint.Rooms.Count; r++)
            {
                BlueprintRoom room = blueprint.Rooms[r];
                (int w, int h) = CellMath.RotatedSize(cachedRoomTemplates[r].WidthCells, cachedRoomTemplates[r].HeightCells, room.Rotation);
                minX = Mathf.Min(minX, room.Cell.X);
                minY = Mathf.Min(minY, room.Cell.Y);
                maxX = Mathf.Max(maxX, room.Cell.X + w);
                maxY = Mathf.Max(maxY, room.Cell.Y + h);
            }

            float boundsW = Mathf.Max(1, maxX - minX);
            float boundsH = Mathf.Max(1, maxY - minY);
            zoom = Mathf.Clamp(Mathf.Min(localRect.width / boundsW, localRect.height / boundsH) * 0.9f, minZoom, maxZoom);
            float contentW = boundsW * zoom;
            float contentH = boundsH * zoom;
            panOffset = new Vector2(
                (localRect.width - contentW) * 0.5f - minX * zoom,
                (localRect.height - contentH) * 0.5f + maxY * zoom);
        }

        /// <summary>전역 셀 좌표를 캔버스 픽셀 좌표로 변환한다(팬·줌 적용).</summary>
        /// <param name="cell">전역 셀 좌표.</param>
        /// <param name="canvasRect">캔버스 영역.</param>
        /// <returns>캔버스 픽셀 좌표(셀 원점 기준).</returns>
        private Vector2 CellToCanvas(CellCoord cell, Rect canvasRect)
        {
            // 셀 좌표계는 +Y=North(위) — 캔버스 Y(아래 증가)와 반대라 부호를 뒤집는다
            return new Vector2(panOffset.x + cell.X * zoom, panOffset.y - cell.Y * zoom);
        }

        /// <summary>전역 셀의 중심 캔버스 픽셀 좌표.</summary>
        /// <param name="cell">전역 셀 좌표.</param>
        /// <param name="canvasRect">캔버스 영역.</param>
        /// <returns>셀 중심 픽셀 좌표.</returns>
        private Vector2 CellCenter(CellCoord cell, Rect canvasRect)
        {
            Vector2 corner = CellToCanvas(cell, canvasRect);
            return new Vector2(corner.x + zoom * 0.5f, corner.y - zoom * 0.5f);
        }

        /// <summary>방 풋프린트(회전 적용)를 캔버스 픽셀 사각형으로 변환한다 — CellMath.RotatedSize 사용.</summary>
        /// <param name="room">배치된 방.</param>
        /// <param name="template">그 방의 템플릿.</param>
        /// <param name="canvasRect">캔버스 영역.</param>
        /// <returns>캔버스 픽셀 사각형.</returns>
        private Rect RoomCanvasRect(BlueprintRoom room, RoomTemplateDef template, Rect canvasRect)
        {
            (int w, int h) = CellMath.RotatedSize(template.WidthCells, template.HeightCells, room.Rotation);
            Vector2 topLeft = CellToCanvas(new CellCoord(room.Cell.X, room.Cell.Y + h), canvasRect);
            return new Rect(topLeft.x, topLeft.y, w * zoom, h * zoom);
        }

        /// <summary>배경 셀 격자를 그린다.</summary>
        /// <param name="canvasRect">캔버스 영역.</param>
        private void DrawGrid(Rect canvasRect)
        {
            if (zoom < gridLabelZoom)
            {
                return;
            }

            var lineColor = new Color(1f, 1f, 1f, 0.04f);
            int firstX = Mathf.FloorToInt(-panOffset.x / zoom);
            int lastX = Mathf.CeilToInt((canvasRect.width - panOffset.x) / zoom);
            for (int x = firstX; x <= lastX; x++)
            {
                EditorGUI.DrawRect(new Rect(panOffset.x + x * zoom, 0f, 1f, canvasRect.height), lineColor);
            }

            int firstY = Mathf.FloorToInt((panOffset.y - canvasRect.height) / zoom);
            int lastY = Mathf.CeilToInt(panOffset.y / zoom);
            for (int y = firstY; y <= lastY; y++)
            {
                EditorGUI.DrawRect(new Rect(0f, panOffset.y - y * zoom, canvasRect.width, 1f), lineColor);
            }
        }

        /// <summary>방 노드를 그린다 — 사이즈·태그·복도·입구 앵커 표기, ShowDanger 시 입구 깊이 그라데이션(10절).</summary>
        /// <param name="canvasRect">캔버스 영역.</param>
        /// <param name="blueprint">블루프린트.</param>
        /// <param name="templates">템플릿 목록.</param>
        /// <param name="options">오버레이 옵션.</param>
        /// <param name="depths">방별 입구 깊이(DangerGradeCalculator.ComputeHopDistances 결과 캐시).</param>
        private void DrawRooms(Rect canvasRect, MapBlueprint blueprint, IReadOnlyList<RoomTemplateDef> templates, OverlayOptions options, int[] depths)
        {
            for (int r = 0; r < blueprint.Rooms.Count; r++)
            {
                if (!RoomVisible(blueprint, r, options.FloorFilter))
                {
                    continue;
                }

                RoomTemplateDef template = cachedRoomTemplates[r];
                Rect rect = RoomCanvasRect(blueprint.Rooms[r], template, canvasRect);
                if (!rect.Overlaps(canvasRect))
                {
                    continue;
                }

                Color fill;
                if (r == 0)
                {
                    fill = new Color(0.15f, 0.35f, 0.6f); // 입구 앵커 = 파랑
                }
                else if (depths[r] < 0)
                {
                    fill = Color.magenta; // 미도달(고립) — 결함 신호
                }
                else if (options.ShowDanger)
                {
                    fill = Color.HSVToRGB(Mathf.Lerp(0.34f, 0.0f, (float)depths[r] / cachedMaxDepth), 0.6f, 0.5f);
                }
                else
                {
                    fill = new Color(0.32f, 0.32f, 0.34f);
                }

                if (template.IsCorridor)
                {
                    fill *= 0.7f;
                    fill.a = 1f;
                }

                EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.8f));
                EditorGUI.DrawRect(new Rect(rect.x + 1f, rect.y + 1f, rect.width - 2f, rect.height - 2f), fill);

                if (zoom >= gridLabelZoom && !template.IsCorridor)
                {
                    string label = template.Tags.HasFlag(RoomTagMask.Dark) ? $"{r}·D" : r.ToString();
                    GUI.Label(rect, label, roomLabelStyle);
                }
            }
        }

        /// <summary>방이 층 필터를 통과하는지 판정한다 — int.MinValue = 전체 표시(M9 다층).</summary>
        /// <param name="blueprint">블루프린트.</param>
        /// <param name="roomIndex">방 인덱스.</param>
        /// <param name="floorFilter">층 필터 값.</param>
        /// <returns>표시 대상이면 true.</returns>
        private static bool RoomVisible(MapBlueprint blueprint, int roomIndex, int floorFilter)
        {
            return floorFilter == int.MinValue || blueprint.Rooms[roomIndex].FloorIndex == floorFilter;
        }

        /// <summary>간선을 그린다 — 통로/열린 문/잠긴 문(🔒 번호)/막힌 벽 구분, LockKind 로 지름길·중요 물품 문 구분 표시(10절).</summary>
        /// <param name="canvasRect">캔버스 영역.</param>
        /// <param name="blueprint">블루프린트.</param>
        /// <param name="templates">템플릿 목록 — 소켓 위치는 CellMath.WorldCell 로 조회.</param>
        /// <param name="options">오버레이 옵션(층 필터 적용).</param>
        private void DrawEdges(Rect canvasRect, MapBlueprint blueprint, IReadOnlyList<RoomTemplateDef> templates, OverlayOptions options)
        {
            for (int e = 0; e < blueprint.Edges.Count; e++)
            {
                BlueprintEdge edge = blueprint.Edges[e];
                RoomTemplateDef templateA = cachedRoomTemplates[edge.RoomA];

                if (edge.SocketA < 0)
                {
                    // 수직 간선(계단, SocketA/B = -2)은 실소켓이 없다 — FindSocket 은 null(NRE). 방 중심끼리 잇는다(M9).
                    // 층 펼침(FloorSpread) 표시에서 층 섬 사이를 가로지르는 하늘색 선이 이것.
                    // 층 필터 시 어느 한쪽이라도 보이면 그린다 — 그 층의 계단 연결 방향을 남기기 위해
                    if (!RoomVisible(blueprint, edge.RoomA, options.FloorFilter) && !RoomVisible(blueprint, edge.RoomB, options.FloorFilter))
                    {
                        continue;
                    }

                    Rect stairA = RoomCanvasRect(blueprint.Rooms[edge.RoomA], templateA, canvasRect);
                    Rect stairB = RoomCanvasRect(blueprint.Rooms[edge.RoomB], cachedRoomTemplates[edge.RoomB], canvasRect);
                    Handles.color = new Color(0.3f, 0.85f, 0.95f);
                    Handles.DrawAAPolyLine(3f, stairA.center, stairB.center);
                    continue;
                }

                if (!RoomVisible(blueprint, edge.RoomA, options.FloorFilter))
                {
                    continue; // 수평 간선·봉인은 양끝이 같은 층 — RoomA 만 보면 된다
                }

                SocketDef socketA = FindSocket(templateA, edge.SocketA);
                CellCoord worldA = CellMath.WorldCell(blueprint.Rooms[edge.RoomA], templateA, socketA.LocalCell);
                Vector2 centerA = CellCenter(worldA, canvasRect);

                if (edge.RoomB < 0)
                {
                    SocketDirection dir = CellMath.RotateDirection(socketA.Direction, blueprint.Rooms[edge.RoomA].Rotation);
                    if (edge.State == EdgeState.ReturnExit)
                    {
                        // 탈출문 — 잎 방 바깥 벽의 출구. 초록 배지로 크게 표기(맵에서 즉시 찾을 수 있어야 한다)
                        var exitBadge = new Rect(centerA.x - 11f, centerA.y - 8f, 22f, 16f);
                        EditorGUI.DrawRect(exitBadge, new Color(0.2f, 0.85f, 0.35f));
                        GUI.Label(exitBadge, "EXIT", badgeStyle);
                        continue;
                    }

                    // 봉인(빈 소켓 막힌 벽) — 소켓 자리 바깥쪽 경계에 어두운 눈금만 표기
                    DrawSealTick(worldA, dir, canvasRect);
                    continue;
                }

                RoomTemplateDef templateB = cachedRoomTemplates[edge.RoomB];
                SocketDef socketB = FindSocket(templateB, edge.SocketB);
                CellCoord worldB = CellMath.WorldCell(blueprint.Rooms[edge.RoomB], templateB, socketB.LocalCell);
                Vector2 centerB = CellCenter(worldB, canvasRect);

                Color lineColor;
                switch (edge.State)
                {
                    case EdgeState.DoorOpen: lineColor = new Color(0.8f, 0.65f, 0.3f); break;
                    case EdgeState.DoorLocked: lineColor = edge.LockKind == LockKind.Shortcut ? new Color(0.75f, 0.4f, 0.95f) : new Color(1f, 0.45f, 0.15f); break;
                    case EdgeState.BlockedWall: lineColor = new Color(0.2f, 0.2f, 0.2f); break;
                    default: lineColor = new Color(0.85f, 0.85f, 0.85f, 0.75f); break;
                }

                Handles.color = lineColor;
                Handles.DrawAAPolyLine(edge.State == EdgeState.DoorLocked ? 4f : 2f, centerA, centerB);

                if (edge.State == EdgeState.DoorLocked)
                {
                    // 자물쇠 배지 — 번호 + 용도 색(ItemDoor 주황 / Shortcut 보라)
                    Vector2 mid = (centerA + centerB) * 0.5f;
                    var badge = new Rect(mid.x - 9f, mid.y - 7f, 18f, 14f);
                    EditorGUI.DrawRect(badge, lineColor);
                    GUI.Label(badge, $"🔒{edge.LockNumber}", badgeStyle);
                }
            }
        }

        /// <summary>층 섬 라벨을 그린다 — 각 층 방 무리의 좌상단 위에 층 이름(1F/2F/B1) 배지(M9 다층 펼침).</summary>
        /// <param name="canvasRect">캔버스 영역.</param>
        /// <param name="blueprint">블루프린트.</param>
        /// <param name="options">오버레이 옵션 — 층 필터 시 그 층 라벨만.</param>
        private void DrawFloorLabels(Rect canvasRect, MapBlueprint blueprint, OverlayOptions options)
        {
            // 층별 방 무리 좌상단 실측(펼침 사본 좌표 기준)
            var topLeft = new Dictionary<int, (int minX, int maxY)>();
            for (int r = 0; r < blueprint.Rooms.Count; r++)
            {
                BlueprintRoom room = blueprint.Rooms[r];
                if (!RoomVisible(blueprint, r, options.FloorFilter))
                {
                    continue;
                }

                if (!topLeft.TryGetValue(room.FloorIndex, out (int minX, int maxY) bounds))
                {
                    bounds = (int.MaxValue, int.MinValue);
                }

                topLeft[room.FloorIndex] = (Mathf.Min(bounds.minX, room.Cell.X), Mathf.Max(bounds.maxY, room.Cell.Y + cachedRoomTemplates[r].HeightCells));
            }

            foreach (KeyValuePair<int, (int minX, int maxY)> entry in topLeft)
            {
                Vector2 corner = CellToCanvas(new CellCoord(entry.Value.minX, entry.Value.maxY + 1), canvasRect);
                var badge = new Rect(corner.x, corner.y - 22f, 44f, 18f);
                EditorGUI.DrawRect(badge, new Color(0.15f, 0.35f, 0.6f, 0.9f));
                GUI.Label(badge, FloorName(entry.Key), badgeStyle);
            }
        }

        /// <summary>층 서수를 표시명으로 바꾼다 — 0 = 1F, 양수 = {n+1}F, 음수 = B{n}. 윈도우 층 필터 툴바도 공유.</summary>
        /// <param name="floorIndex">층 서수.</param>
        /// <returns>층 표시명.</returns>
        public static string FloorName(int floorIndex)
        {
            return floorIndex >= 0 ? $"{floorIndex + 1}F" : $"B{-floorIndex}";
        }

        /// <summary>봉인 소켓의 막힌 벽 눈금을 그린다 — 소켓 셀의 봉인 방향 경계선.</summary>
        /// <param name="cell">소켓 전역 셀.</param>
        /// <param name="direction">봉인 방향(회전 적용).</param>
        /// <param name="canvasRect">캔버스 영역.</param>
        private void DrawSealTick(CellCoord cell, SocketDirection direction, Rect canvasRect)
        {
            Vector2 corner = CellToCanvas(new CellCoord(cell.X, cell.Y + 1), canvasRect); // 셀 좌상단
            const float thick = 3f;
            Rect tick;
            switch (direction)
            {
                case SocketDirection.North: tick = new Rect(corner.x, corner.y - thick * 0.5f, zoom, thick); break;
                case SocketDirection.South: tick = new Rect(corner.x, corner.y + zoom - thick * 0.5f, zoom, thick); break;
                case SocketDirection.West: tick = new Rect(corner.x - thick * 0.5f, corner.y, thick, zoom); break;
                default: tick = new Rect(corner.x + zoom - thick * 0.5f, corner.y, thick, zoom); break;
            }

            EditorGUI.DrawRect(tick, new Color(0.05f, 0.05f, 0.05f));
        }

        /// <summary>스폰 오버레이를 그린다 — 종류별 아이콘/라벨(열쇠는 KeyNumber 표기), 위치는 마커 전역 셀(CellMath.WorldCell).</summary>
        /// <param name="canvasRect">캔버스 영역.</param>
        /// <param name="blueprint">블루프린트.</param>
        /// <param name="templates">템플릿 목록.</param>
        /// <param name="options">오버레이 옵션(SpawnKindFilterMask 적용).</param>
        private void DrawSpawnOverlay(Rect canvasRect, MapBlueprint blueprint, IReadOnlyList<RoomTemplateDef> templates, OverlayOptions options)
        {
            // 같은 마커 셀에 겹치는 배지는 세로로 쌓는다
            var stackCount = new Dictionary<long, int>();
            for (int s = 0; s < blueprint.Spawns.Count; s++)
            {
                BlueprintSpawn spawn = blueprint.Spawns[s];
                if ((options.SpawnKindFilterMask & (1 << (int)spawn.Kind)) == 0 || !RoomVisible(blueprint, spawn.RoomIndex, options.FloorFilter))
                {
                    continue;
                }

                RoomTemplateDef template = cachedRoomTemplates[spawn.RoomIndex];
                MarkerDef marker = FindMarker(template, spawn.MarkerId);
                if (marker == null)
                {
                    continue; // 앵커 전용 종류(벽장 — MarkerId -1)는 코어 마커가 없다 — 좌표는 방 프리팹 앵커 소관
                }

                CellCoord world = CellMath.WorldCell(blueprint.Rooms[spawn.RoomIndex], template, marker.LocalCell);
                long key = ((long)world.X << 32) ^ (uint)world.Y;
                stackCount.TryGetValue(key, out int stack);
                stackCount[key] = stack + 1;

                Vector2 center = CellCenter(world, canvasRect);
                (string label, Color color) = SpawnBadge(spawn);
                float width = label.Length > 1 ? 20f : 14f;
                var badge = new Rect(center.x - width * 0.5f, center.y - 6f + stack * 13f, width, 12f);
                EditorGUI.DrawRect(badge, color);
                GUI.Label(badge, label, badgeStyle);
            }
        }

        /// <summary>스폰 종류별 배지 라벨·색을 정한다(열쇠는 KeyNumber 포함).</summary>
        /// <param name="spawn">대상 스폰.</param>
        /// <returns>(라벨, 배경색).</returns>
        private static (string, Color) SpawnBadge(BlueprintSpawn spawn)
        {
            switch (spawn.Kind)
            {
                case SpawnKind.ZombieWalker: return ("Z", new Color(0.7f, 0.15f, 0.15f));
                case SpawnKind.ZombieListener: return ("L", new Color(0.85f, 0.3f, 0.1f));
                case SpawnKind.ZombieWatcher: return ("W", new Color(0.55f, 0.1f, 0.4f));
                case SpawnKind.VaccineAntigen: return ("V1", new Color(0.1f, 0.55f, 0.25f));
                case SpawnKind.VaccineSerum: return ("V2", new Color(0.1f, 0.55f, 0.25f));
                case SpawnKind.VaccineStabilizer: return ("V3", new Color(0.1f, 0.55f, 0.25f));
                case SpawnKind.Key: return ($"K{spawn.KeyNumber}", new Color(0.8f, 0.7f, 0.1f));
                case SpawnKind.Fuel: return ("O", new Color(0.5f, 0.35f, 0.1f));
                case SpawnKind.Scrap: return ("S", new Color(0.45f, 0.45f, 0.45f));
                case SpawnKind.Throwable: return ("T", new Color(0.1f, 0.5f, 0.6f));
                case SpawnKind.CorpseStation: return ("C", new Color(0.4f, 0.25f, 0.55f));
                case SpawnKind.Generator: return ("G", new Color(0.15f, 0.35f, 0.7f));
                case SpawnKind.Wardrobe: return ("B", new Color(0.2f, 0.6f, 0.75f));
                default: return ("H", new Color(0.45f, 0.1f, 0.1f)); // HerdArea
            }
        }

        /// <summary>
        /// 범례 — 캔버스 좌상단에 배지 문자·간선 색의 뜻을 표기한다(오버레이 토글과 무관하게 항상 표시).
        /// 배지 색은 SpawnBadge 와 같은 값을 쓴다 — 갈라지면 범례가 거짓말이 된다.
        /// </summary>
        /// <param name="localRect">클립 로컬 캔버스 영역.</param>
        private void DrawLegend(Rect localRect)
        {
            (string label, string desc, Color color)[] entries =
            {
                ("Z", "Walker", new Color(0.7f, 0.15f, 0.15f)),
                ("L", "Listener", new Color(0.85f, 0.3f, 0.1f)),
                ("W", "Watcher", new Color(0.55f, 0.1f, 0.4f)),
                ("H", "무리 구역", new Color(0.45f, 0.1f, 0.1f)),
                ("V1~3", "백신 3종", new Color(0.1f, 0.55f, 0.25f)),
                ("K#", "열쇠(번호)", new Color(0.8f, 0.7f, 0.1f)),
                ("O", "연료", new Color(0.5f, 0.35f, 0.1f)),
                ("S", "스크랩", new Color(0.45f, 0.45f, 0.45f)),
                ("T", "투척물", new Color(0.1f, 0.5f, 0.6f)),
                ("C", "사체 충전소", new Color(0.4f, 0.25f, 0.55f)),
                ("G", "발전기", new Color(0.15f, 0.35f, 0.7f)),
                ("B", "벽장(앵커 배치)", new Color(0.2f, 0.6f, 0.75f)),
                ("EXIT", "탈출문", new Color(0.2f, 0.85f, 0.35f)),
                ("─", "통로", new Color(0.85f, 0.85f, 0.85f, 0.75f)),
                ("─", "문(열림)", new Color(0.8f, 0.65f, 0.3f)),
                ("━", "자물쇠(물품)", new Color(1f, 0.45f, 0.15f)),
                ("━", "자물쇠(지름길)", new Color(0.75f, 0.4f, 0.95f)),
            };

            const float rowHeight = 14f;
            const float width = 132f;
            var panel = new Rect(6f, 6f, width, entries.Length * rowHeight + 20f);
            EditorGUI.DrawRect(panel, new Color(0.08f, 0.08f, 0.1f, 0.88f));
            GUI.Label(new Rect(panel.x + 6f, panel.y + 2f, width, 14f), "범례", EditorStyles.miniBoldLabel);

            for (int i = 0; i < entries.Length; i++)
            {
                float y = panel.y + 18f + i * rowHeight;
                var swatch = new Rect(panel.x + 6f, y + 1f, 26f, 11f);
                EditorGUI.DrawRect(swatch, entries[i].color);
                GUI.Label(swatch, entries[i].label, badgeStyle);
                GUI.Label(new Rect(panel.x + 36f, y - 1f, width - 40f, 14f), entries[i].desc, EditorStyles.miniLabel);
            }
        }

        /// <summary>좀비 스폰의 배회 반경 원을 그린다(BlueprintSpawn.WanderRadiusCells — 10절 편집 대상의 시각화).</summary>
        /// <param name="canvasRect">캔버스 영역.</param>
        /// <param name="blueprint">블루프린트.</param>
        /// <param name="templates">템플릿 목록.</param>
        /// <param name="options">오버레이 옵션(층 필터 적용).</param>
        private void DrawWanderCircles(Rect canvasRect, MapBlueprint blueprint, IReadOnlyList<RoomTemplateDef> templates, OverlayOptions options)
        {
            Handles.color = new Color(1f, 0.8f, 0.3f, 0.35f);
            for (int s = 0; s < blueprint.Spawns.Count; s++)
            {
                BlueprintSpawn spawn = blueprint.Spawns[s];
                if (spawn.WanderRadiusCells <= 0f || !IsZombie(spawn.Kind) || !RoomVisible(blueprint, spawn.RoomIndex, options.FloorFilter))
                {
                    continue;
                }

                RoomTemplateDef template = cachedRoomTemplates[spawn.RoomIndex];
                MarkerDef marker = FindMarker(template, spawn.MarkerId);
                if (marker == null)
                {
                    continue; // 앵커 전용 종류(벽장 — MarkerId -1)는 코어 마커가 없다 — 좌표는 방 프리팹 앵커 소관
                }

                CellCoord world = CellMath.WorldCell(blueprint.Rooms[spawn.RoomIndex], template, marker.LocalCell);
                Vector2 center = CellCenter(world, canvasRect);
                Handles.DrawWireDisc(new Vector3(center.x, center.y, 0f), Vector3.forward, spawn.WanderRadiusCells * zoom);
            }
        }

        /// <summary>좀비 청각 반경 근사 원을 그린다 — 시각화 전용(10절, 편집 불가). 반경은 HearingRadiusCells 로 계산.</summary>
        /// <param name="canvasRect">캔버스 영역.</param>
        /// <param name="blueprint">블루프린트.</param>
        /// <param name="templates">템플릿 목록.</param>
        /// <param name="options">오버레이 옵션.</param>
        private void DrawHearingCircles(Rect canvasRect, MapBlueprint blueprint, IReadOnlyList<RoomTemplateDef> templates, OverlayOptions options)
        {
            float radiusCells = HearingRadiusCells(options);
            if (radiusCells <= 0f)
            {
                return;
            }

            Handles.color = new Color(0.3f, 0.8f, 1f, 0.25f);
            for (int s = 0; s < blueprint.Spawns.Count; s++)
            {
                BlueprintSpawn spawn = blueprint.Spawns[s];
                if (!IsZombie(spawn.Kind) || !RoomVisible(blueprint, spawn.RoomIndex, options.FloorFilter))
                {
                    continue;
                }

                RoomTemplateDef template = cachedRoomTemplates[spawn.RoomIndex];
                MarkerDef marker = FindMarker(template, spawn.MarkerId);
                if (marker == null)
                {
                    continue; // 앵커 전용 종류(벽장 — MarkerId -1)는 코어 마커가 없다 — 좌표는 방 프리팹 앵커 소관
                }

                CellCoord world = CellMath.WorldCell(blueprint.Rooms[spawn.RoomIndex], template, marker.LocalCell);
                Vector2 center = CellCenter(world, canvasRect);
                Handles.DrawWireDisc(new Vector3(center.x, center.y, 0f), Vector3.forward, radiusCells * zoom);
            }
        }

        /// <summary>
        /// 기준 소음이 가청 임계까지 감쇠하는 거리를 셀 단위로 근사한다 — 소음시스템 D2 의 감쇠 규칙 기반,
        /// 셀 실측 m 는 가정치(G1 미확정)라 근사 표시 전용이다(10절 — 정본 편집은 zombie/noise Data).
        /// </summary>
        /// <param name="options">기준 dB·셀 m 가정치.</param>
        /// <returns>청각 반경(셀).</returns>
        private float HearingRadiusCells(OverlayOptions options)
        {
            if (options.AssumedCellMeters <= 0f)
            {
                return 0f;
            }

            float radiusMeters = Mathf.Max(0f, (options.HearingRefDb - hearingThresholdDb) / falloffDbPerMeter);
            return radiusMeters / options.AssumedCellMeters;
        }

        /// <summary>마우스가 올라간 방의 요약 툴팁을 그린다 — 템플릿 ID·태그·입구 깊이·스폰 요약.</summary>
        /// <param name="canvasRect">캔버스 영역.</param>
        /// <param name="blueprint">블루프린트.</param>
        /// <param name="templates">템플릿 목록.</param>
        /// <param name="depths">방별 입구 깊이.</param>
        private void DrawHoverTooltip(Rect canvasRect, MapBlueprint blueprint, IReadOnlyList<RoomTemplateDef> templates, int[] depths)
        {
            Vector2 mouse = Event.current.mousePosition;
            if (!canvasRect.Contains(mouse))
            {
                return;
            }

            int hoverRoom = -1;
            for (int r = 0; r < blueprint.Rooms.Count; r++)
            {
                if (RoomCanvasRect(blueprint.Rooms[r], cachedRoomTemplates[r], canvasRect).Contains(mouse))
                {
                    hoverRoom = r;
                    break;
                }
            }

            if (hoverRoom < 0)
            {
                return;
            }

            RoomTemplateDef template = cachedRoomTemplates[hoverRoom];
            var summary = new System.Text.StringBuilder();
            summary.Append($"방 {hoverRoom} — {template.TemplateId}");
            if (template.IsCorridor)
            {
                summary.Append(" (복도)");
            }

            summary.Append($"\n입구 깊이 {depths[hoverRoom]}");
            if (template.Tags != RoomTagMask.None)
            {
                summary.Append($" · 태그 {template.Tags}");
            }

            var kindCounts = new Dictionary<SpawnKind, int>();
            for (int s = 0; s < blueprint.Spawns.Count; s++)
            {
                if (blueprint.Spawns[s].RoomIndex == hoverRoom)
                {
                    kindCounts.TryGetValue(blueprint.Spawns[s].Kind, out int c);
                    kindCounts[blueprint.Spawns[s].Kind] = c + 1;
                }
            }

            foreach (KeyValuePair<SpawnKind, int> pair in kindCounts)
            {
                summary.Append($"\n{pair.Key} ×{pair.Value}");
            }

            var content = new GUIContent(summary.ToString());
            Vector2 size = tooltipStyle.CalcSize(content);
            var box = new Rect(
                Mathf.Min(mouse.x + 14f, canvasRect.width - size.x - 6f),
                Mathf.Min(mouse.y + 14f, canvasRect.height - size.y - 6f),
                size.x + 6f, size.y + 4f);
            EditorGUI.DrawRect(box, new Color(0f, 0f, 0f, 0.85f));
            GUI.Label(new Rect(box.x + 3f, box.y + 2f, size.x, size.y), content, tooltipStyle);
        }

        /// <summary>좀비 스폰 종류 여부.</summary>
        /// <param name="kind">스폰 종류.</param>
        /// <returns>Walker/Listener/Watcher 면 참.</returns>
        private static bool IsZombie(SpawnKind kind)
        {
            return kind == SpawnKind.ZombieWalker || kind == SpawnKind.ZombieListener || kind == SpawnKind.ZombieWatcher;
        }

        /// <summary>템플릿에서 소켓 Id 로 소켓을 찾는다(레이아웃이 보장 — 없으면 데이터 결함, NRE 표면화).</summary>
        /// <param name="template">대상 템플릿.</param>
        /// <param name="socketId">소켓 Id.</param>
        /// <returns>소켓 정의.</returns>
        private static SocketDef FindSocket(RoomTemplateDef template, int socketId)
        {
            for (int s = 0; s < template.Sockets.Length; s++)
            {
                if (template.Sockets[s].Id == socketId)
                {
                    return template.Sockets[s];
                }
            }

            return null;
        }

        /// <summary>템플릿에서 마커 Id 로 마커를 찾는다(분배기가 보장 — 없으면 데이터 결함, NRE 표면화).</summary>
        /// <param name="template">대상 템플릿.</param>
        /// <param name="markerId">마커 Id.</param>
        /// <returns>마커 정의.</returns>
        private static MarkerDef FindMarker(RoomTemplateDef template, int markerId)
        {
            for (int m = 0; m < template.Markers.Length; m++)
            {
                if (template.Markers[m].Id == markerId)
                {
                    return template.Markers[m];
                }
            }

            return null;
        }

        /// <summary>라벨 스타일을 지연 생성한다(EditorStyles 는 정적 초기화 시점에 접근 불가).</summary>
        private void EnsureStyles()
        {
            if (badgeStyle != null)
            {
                return;
            }

            badgeStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 9,
                normal = { textColor = Color.white },
            };
            roomLabelStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(1f, 1f, 1f, 0.9f) },
            };
            tooltipStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = Color.white },
                richText = false,
            };
        }
    }
}
