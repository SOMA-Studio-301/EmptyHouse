using System.Collections.Generic;
using Border.Core;

namespace EmptyHouse.MapGen.Core
{
    /// <summary>
    /// 층 순서 확정·샤프트 승격·이격 판정(M8 SSA). <see cref="LayoutGenerator"/> 비대화를 막기 위해 분리했다.
    /// **이 클래스는 난수를 소비하지 않는다** — 층 순서가 rng 를 먹으면 층 1개 구성에서도 스트림이 어긋나
    /// v1 하위호환 골든이 깨진다. 샤프트 수·삽입 시점 롤은 <see cref="LayoutGenerator"/> 소관이다.
    /// </summary>
    public static class FloorSequencer
    {
        /// <summary>
        /// 층 생성 순서를 확정한다 — 시드 층(입구 앵커 보유)이 먼저, 나머지는 |서수| 오름차순,
        /// 동률이면 큰 쪽(위층) 먼저. 파라미터 배열 순서만 보고 결정하며 난수를 쓰지 않는다.
        /// </summary>
        /// <param name="plan">생성 계획.</param>
        /// <returns>층 슬롯 인덱스의 처리 순서.</returns>
        public static int[] Order(MapGenPlan plan)
        {
            // TODO(impl):
            Log.D("[FloorSequencer] Order");
            return default;
        }

        /// <summary>
        /// 시드 층에 배치된 계단실 방들을 <see cref="StairShaft"/> 로 승격한다 —
        /// 좌표·회전을 전 층이 공유할 형태로 굳히고 관통 범위(최하~최상 서수)를 채운다.
        /// </summary>
        /// <param name="plan">생성 계획.</param>
        /// <param name="blueprint">시드 층 레이아웃이 끝난 블루프린트.</param>
        /// <param name="stairRoomIndices">시드 층에서 계단실로 배치된 방 인덱스 목록.</param>
        /// <returns>승격된 샤프트 목록.</returns>
        public static List<StairShaft> PromoteShafts(MapGenPlan plan, MapBlueprint blueprint, IReadOnlyList<int> stairRoomIndices)
        {
            // TODO(impl):
            Log.D("[FloorSequencer] PromoteShafts");
            return default;
        }

        /// <summary>
        /// 후보 계단실 좌표가 기존 샤프트들과 최소 이격(<see cref="MapGenParams.ShaftMinSeparationCells"/>,
        /// 체비셰프 거리)을 지키는지 판정한다 — 샤프트가 몰리면 층간 루프가 생기지 않는다. 난수 미소비 순수 술어.
        /// </summary>
        /// <param name="shafts">기존 샤프트 목록.</param>
        /// <param name="candidate">후보 좌표.</param>
        /// <param name="minSeparationCells">최소 이격(셀).</param>
        /// <returns>이격을 만족하면 true.</returns>
        public static bool IsSeparated(IReadOnlyList<StairShaft> shafts, CellCoord candidate, int minSeparationCells)
        {
            // TODO(impl):
            Log.D("[FloorSequencer] IsSeparated");
            return default;
        }
    }
}
