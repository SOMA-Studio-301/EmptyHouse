using Border.Core;

namespace EmptyHouse.MapGen.Core
{
    /// <summary>
    /// 방 위험 등급 산출(3절) — 단일 층에서는 입구(방 0)로부터의 그래프 깊이가 위험 등급이다.
    /// 열쇠·지름길·스폰 등 이후 모든 배치 규칙의 입력값. 층 가중치(B1 &gt; 2F &gt; 1F)는 다층 v2(G6).
    /// </summary>
    public static class DangerGradeCalculator
    {
        /// <summary>
        /// 방별 입구 깊이를 BFS 로 계산한다. 잠긴 문도 간선으로 취급한다(AC-06 과 같은 기준).
        /// </summary>
        /// <param name="blueprint">Rooms/Edges 가 채워진 블루프린트.</param>
        /// <returns>방 인덱스 → 입구로부터의 그래프 깊이.</returns>
        public static int[] ComputeDepths(MapBlueprint blueprint)
        {
            // TODO(impl):
            Log.D("[DangerGradeCalculator] ComputeDepths");
            return default;
        }
    }
}
