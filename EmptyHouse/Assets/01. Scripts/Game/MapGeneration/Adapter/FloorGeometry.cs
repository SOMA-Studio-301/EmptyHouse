using UnityEngine;

namespace EmptyHouse.MapGen.Runtime
{
    /// <summary>
    /// 층 기하 계산(M9-8) — 조립기·에디터 빌더·NavMesh 베이커·상태 오브젝트 스포너가 **공용**으로 쓴다.
    /// 층 Y 를 계산하는 코드가 두 곳으로 갈라지면 미리보기와 실제 맵이 어긋난다(AC-21 이 막으려는 사고).
    ///
    /// 규약: <c>FloorHeight(f)</c> = 층 f 바닥면에서 층 f+1 바닥면까지의 거리(m). **항상 아래 층이 보유**한다.
    /// <code>
    /// FloorPlaneY(0)   = 0
    /// FloorPlaneY(f&gt;0) = FloorPlaneY(f-1) + FloorHeight(f-1)
    /// FloorPlaneY(f&lt;0) = FloorPlaneY(f+1) - FloorHeight(f)
    /// StairRise(f)     = FloorHeight(f)
    /// </code>
    /// **층 루트 Y 는 누적합이며 <c>f × H</c> 가 아니다** — 층마다 층고가 다를 수 있다.
    /// 2026-08-11 실측 기준값: 층고 6m(방 프리팹 벽 `Hall_Wall_6M_1Side` 통일), 계단은 기성 3m × 2단 스위치백.
    /// </summary>
    public static class FloorGeometry
    {
        /// <summary>
        /// 층 서수의 바닥면 월드 Y 오프셋(맵 원점 기준)을 구한다. 0층은 항상 0.
        /// 부호 오프바이원이 나오는 유일한 식이라 FloorPlaneY(-1)/(0)/(+1) 검증을 린트가 수행한다.
        /// 순수 계산 — 로그 없음(CellMath 규약).
        /// </summary>
        /// <param name="definition">빈 집 정의.</param>
        /// <param name="floorIndex">층 서수(B1 = -1 · 1F = 0 · 2F = +1).</param>
        /// <returns>바닥면 Y 오프셋(m).</returns>
        public static float FloorPlaneY(MapDefinitionSO definition, int floorIndex)
        {
            float y = 0f;
            if (floorIndex > 0)
            {
                // 위층 — 아래 층들의 층고 누적합
                for (int f = 0; f < floorIndex; f++)
                {
                    y += definition.FloorOf(f).FloorHeight;
                }
            }
            else if (floorIndex < 0)
            {
                // 아래층 — 자기 층고를 빼며 내려간다(층고는 항상 아래 층 보유)
                for (int f = -1; f >= floorIndex; f--)
                {
                    y -= definition.FloorOf(f).FloorHeight;
                }
            }

            return y;
        }

        /// <summary>층 f 계단의 총 라이즈(= 층 f 의 층고)를 구한다. 순수 계산 — 로그 없음(CellMath 규약).</summary>
        /// <param name="definition">빈 집 정의.</param>
        /// <param name="floorIndex">층 서수.</param>
        /// <returns>라이즈(m).</returns>
        public static float StairRise(MapDefinitionSO definition, int floorIndex)
        {
            return definition.FloorOf(floorIndex).FloorHeight;
        }
    }
}
