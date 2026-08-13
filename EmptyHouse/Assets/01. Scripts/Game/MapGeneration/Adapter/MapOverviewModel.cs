using System.Collections.Generic;
using Border.Core;
using EmptyHouse.MapGen.Core;
using UnityEngine;

namespace EmptyHouse.MapGen.Runtime
{
    /// <summary>안내도에 그릴 방 사각형 1개(EH-62) — 맵 셀 공간(정규화 원점) 기준.</summary>
    public sealed class MapOverviewRoomRect
    {
        public Rect CellRect; // 방 풋프린트(셀 단위, 회전 적용 후) — 원점 = 맵 최소 셀(MinCellBounds 정규화)
        public bool IsCorridor; // 복도 여부 — UI 가 얇은/어두운 사각형으로 구분 표현할 수 있게
        public int FloorIndex; // 층 서수 — v1 은 단층(전부 0), 층 전환 UI(S8)가 이 필드로 필터한다
    }

    /// <summary>
    /// 안내도 도식 모델(EH-62) — 블루프린트의 방 배치를 층별 사각형 목록으로 변환하고,
    /// **월드 ↔ 맵 셀 공간 변환식을 단일 소유**한다(UI 에서 좌표 계산 중복 금지 — 어긋나면 마커가 벽을 뚫는다).
    /// 좌표 정규화는 조립기와 같은 MinCellBounds 규칙 · 셀 크기는 빈 집 정의(CellMeters)에서 얻는다.
    /// 순수 표시용 데이터 — 게임플레이 판정에 관여하지 않는다.
    /// </summary>
    public sealed class MapOverviewModel
    {
        private const float FloorSnapTolerance = 0.5f; // 층 판정 여유(m) — 계단 경사·턱에서 발이 층 평면보다 살짝 낮아도 그 층으로 본다

        private readonly List<MapOverviewRoomRect> rooms = new List<MapOverviewRoomRect>(); // 전 층 방 사각형(리스트 인덱스 = 블루프린트 방 번호)
        private readonly List<int> floorIndices = new List<int>(); // 층 서수(평면 Y 오름차순) — WorldToFloor 조회표
        private readonly List<float> floorPlaneYs = new List<float>(); // 층 바닥면 월드 Y(오름차순) — floorIndices 와 같은 순서
        private Vector3 worldOrigin; // 맵 루트 월드 원점 — 정규화 최소 셀(셀 공간 0,0)이 놓인 위치
        private float cellMeters; // 셀 실측(m) — 월드↔셀 배율

        public IReadOnlyList<MapOverviewRoomRect> Rooms => rooms; // UI 사각형 생성 원천
        public Vector2 CellSize { get; private set; } // 맵 전체 크기(셀) — UI 패널 맞춤 스케일 기준

        /// <summary>
        /// 블루프린트에서 모델을 1회 빌드한다 — 방·복도 전부 사각형으로(내부 묘사 없음, 요구 4).
        /// 회전 적용 후 풋프린트와 최소 셀 정규화는 조립기 규칙과 동일해야 한다(드리프트 금지).
        /// </summary>
        /// <param name="blueprint">로컬 재생성 블루프린트.</param>
        /// <param name="templates">생성에 쓴 평탄화 템플릿(풋프린트 조회).</param>
        /// <param name="definition">빈 집 정의(CellMeters — 월드 변환 배율).</param>
        /// <param name="mapOrigin">로컬 맵 루트 월드 원점(정규화 최소 셀이 놓인 위치).</param>
        /// <returns>빌드된 모델.</returns>
        public static MapOverviewModel Build(MapBlueprint blueprint, IReadOnlyList<RoomTemplateDef> templates, MapDefinitionSO definition, Vector3 mapOrigin)
        {
            Log.D($"[MapOverviewModel] Build 시드={blueprint.Meta.Seed}");

            var model = new MapOverviewModel
            {
                worldOrigin = mapOrigin,
                cellMeters = definition.Floors[0].CellMeters, // 계단 연결 층 쌍은 린트가 동일성을 강제 — 조립기와 같은 전역 하나
            };

            // 조립기와 동일한 최소 셀 정규화 — 여기서 갈라지면 마커가 벽을 뚫는다.
            // XZ 정규화는 전 층 공통(층 루트는 Y 만 이동)이라 층을 갈아도 도식 좌표계·배율이 유지된다
            (int minX, int minY) = MapRuntimeAssembler.MinCellBounds(blueprint);
            int maxX = 0;
            int maxY = 0;
            for (int r = 0; r < blueprint.Rooms.Count; r++)
            {
                BlueprintRoom room = blueprint.Rooms[r];
                RoomTemplateDef template = MapRuntimeAssembler.FindTemplate(templates, room.TemplateId);
                (int width, int height) = CellMath.RotatedSize(template.WidthCells, template.HeightCells, room.Rotation);

                int x = room.Cell.X - minX;
                int y = room.Cell.Y - minY;
                model.rooms.Add(new MapOverviewRoomRect
                {
                    CellRect = new Rect(x, y, width, height),
                    IsCorridor = template.IsCorridor,
                    FloorIndex = room.FloorIndex,
                });

                maxX = Mathf.Max(maxX, x + width);
                maxY = Mathf.Max(maxY, y + height);
            }

            model.CellSize = new Vector2(maxX, maxY);
            model.BuildFloorTable(blueprint, definition, mapOrigin.y);
            return model;
        }

        /// <summary>층 바닥면 월드 Y 조회표를 평면 오름차순으로 만든다 — WorldToFloor 의 재료.</summary>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="definition">빈 집 정의(층고 원천).</param>
        /// <param name="originY">맵 루트 월드 Y.</param>
        private void BuildFloorTable(MapBlueprint blueprint, MapDefinitionSO definition, float originY)
        {
            for (int f = 0; f < blueprint.Floors.Count; f++)
            {
                int floorIndex = blueprint.Floors[f].FloorIndex;
                float planeY = originY + FloorGeometry.FloorPlaneY(definition, floorIndex);

                // 삽입 정렬 — 층 수가 한 자리라 단순 삽입이 정렬보다 싸고 결정적이다
                int slot = floorPlaneYs.Count;
                while (slot > 0 && floorPlaneYs[slot - 1] > planeY)
                {
                    slot--;
                }

                floorPlaneYs.Insert(slot, planeY);
                floorIndices.Insert(slot, floorIndex);
            }
        }

        /// <summary>월드 좌표(XZ)를 맵 셀 공간 좌표로 변환한다 — 플레이어·좀비 마커 배치용.</summary>
        /// <param name="worldPosition">월드 좌표.</param>
        /// <returns>맵 셀 공간 좌표.</returns>
        public Vector2 WorldToCell(Vector3 worldPosition)
        {
            // 마커 수만큼 매 프레임 호출되므로 진입 트레이스를 두지 않는다.
            // 조립기 불변식 "방 = 루트 위치 + (셀−최소셀)×셀m" 의 역함수 — 층(Y)은 WorldToFloor 소관
            return new Vector2(
                (worldPosition.x - worldOrigin.x) / cellMeters,
                (worldPosition.z - worldOrigin.z) / cellMeters);
        }

        /// <summary>월드 좌표가 속한 층 서수를 구한다 — 발밑 바닥면 기준(그 아래로 가장 가까운 층). 최하층 아래는 최하층.</summary>
        /// <param name="worldPosition">월드 좌표.</param>
        /// <returns>층 서수(B1 = -1 · 1F = 0 · 2F = +1).</returns>
        public int WorldToFloor(Vector3 worldPosition)
        {
            // 매 프레임 호출되므로 진입 트레이스를 두지 않는다.
            int result = floorIndices[0];
            for (int f = 0; f < floorPlaneYs.Count; f++)
            {
                if (worldPosition.y + FloorSnapTolerance < floorPlaneYs[f])
                {
                    break; // 오름차순 — 여기부터는 전부 머리 위 층이다
                }

                result = floorIndices[f];
            }

            return result;
        }

        /// <summary>월드 요(도)를 맵 회전각(도)으로 변환한다 — 시야 부채꼴 회전용(셀 좌표계 +X=East·+Y=North 정합).</summary>
        /// <param name="worldYawDegrees">카메라 월드 요(도).</param>
        /// <returns>맵 공간 회전각(도).</returns>
        public float WorldYawToCellAngle(float worldYawDegrees)
        {
            // 매 프레임 호출되므로 진입 트레이스를 두지 않는다.
            // 셀 +Y(북) = 월드 +Z, 셀 +X(동) = 월드 +X. 월드 요는 북 기준 시계방향,
            // UI 의 Z 회전은 반시계방향이 양수라 부호만 뒤집으면 정합한다(요 0 = 위, 요 90 = 오른쪽).
            return -worldYawDegrees;
        }
    }
}
