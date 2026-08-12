using System.Collections.Generic;

namespace EmptyHouse.MapGen.Core
{
    /// <summary>
    /// 층 순서 확정·샤프트 승격·이격 판정(M9 SSA). <see cref="LayoutGenerator"/> 비대화를 막기 위해 분리했다.
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
            var rest = new List<int>();
            for (int i = 0; i < plan.Floors.Length; i++)
            {
                if (i != plan.SeedFloorSlot)
                {
                    rest.Add(i);
                }
            }

            // |서수| 오름차순, 동률이면 서수 큰 쪽(위층) 먼저 — 안정 삽입 정렬(입력 순서 무관 결정론)
            rest.Sort((a, b) =>
            {
                int fa = plan.Floors[a].FloorIndex;
                int fb = plan.Floors[b].FloorIndex;
                int absCompare = System.Math.Abs(fa).CompareTo(System.Math.Abs(fb));
                return absCompare != 0 ? absCompare : fb.CompareTo(fa);
            });

            var order = new int[plan.Floors.Length];
            order[0] = plan.SeedFloorSlot;
            for (int i = 0; i < rest.Count; i++)
            {
                order[i + 1] = rest[i];
            }

            return order;
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
            int bottom = int.MaxValue;
            int top = int.MinValue;
            for (int i = 0; i < plan.Floors.Length; i++)
            {
                bottom = System.Math.Min(bottom, plan.Floors[i].FloorIndex);
                top = System.Math.Max(top, plan.Floors[i].FloorIndex);
            }

            var shafts = new List<StairShaft>(stairRoomIndices.Count);
            for (int i = 0; i < stairRoomIndices.Count; i++)
            {
                BlueprintRoom room = blueprint.Rooms[stairRoomIndices[i]];
                shafts.Add(new StairShaft
                {
                    ShaftId = i,
                    Cell = room.Cell,
                    Rotation = room.Rotation,
                    BottomFloor = bottom,
                    TopFloor = top,
                });
            }

            return shafts;
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
            for (int i = 0; i < shafts.Count; i++)
            {
                int dx = System.Math.Abs(shafts[i].Cell.X - candidate.X);
                int dy = System.Math.Abs(shafts[i].Cell.Y - candidate.Y);
                if (System.Math.Max(dx, dy) < minSeparationCells)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
