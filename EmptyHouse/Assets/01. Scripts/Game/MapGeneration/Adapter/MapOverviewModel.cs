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
        private readonly List<MapOverviewRoomRect> rooms = new List<MapOverviewRoomRect>(); // 전 층 방 사각형(리스트 인덱스 = 블루프린트 방 번호)

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
            // TODO(impl):
            Log.D($"[MapOverviewModel] Build 시드={blueprint.Meta.Seed}");
            return default;
        }

        /// <summary>월드 좌표(XZ)를 맵 셀 공간 좌표로 변환한다 — 플레이어·좀비 마커 배치용.</summary>
        /// <param name="worldPosition">월드 좌표.</param>
        /// <returns>맵 셀 공간 좌표.</returns>
        public Vector2 WorldToCell(Vector3 worldPosition)
        {
            // TODO(impl):
            Log.D("[MapOverviewModel] WorldToCell");
            return default;
        }

        /// <summary>월드 요(도)를 맵 회전각(도)으로 변환한다 — 시야 부채꼴 회전용(셀 좌표계 +X=East·+Y=North 정합).</summary>
        /// <param name="worldYawDegrees">카메라 월드 요(도).</param>
        /// <returns>맵 공간 회전각(도).</returns>
        public float WorldYawToCellAngle(float worldYawDegrees)
        {
            // TODO(impl):
            Log.D("[MapOverviewModel] WorldYawToCellAngle");
            return default;
        }
    }
}
