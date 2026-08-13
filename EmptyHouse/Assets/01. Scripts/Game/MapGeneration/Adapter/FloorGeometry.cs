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
        /// 부호 오프바이원이 나오는 유일한 식이라 FloorPlaneY(-1)/(0)/(+1) 검증을 셋업 린트가 수행한다.
        /// </summary>
        /// <param name="stack">층 스택(아래→위 정렬 전제 아님 — 서수로 조회한다).</param>
        /// <param name="floorIndex">층 서수(B1 = -1 · 1F = 0 · 2F = +1).</param>
        /// <returns>바닥면 Y 오프셋(m).</returns>
        public static float FloorPlaneY(MapFloorStackSO stack, int floorIndex)
        {
            float y = 0f;
            if (floorIndex > 0)
            {
                // 위층 — 아래 층들의 층고 누적합
                for (int f = 0; f < floorIndex; f++)
                {
                    y += stack.Find(f).FloorHeight;
                }
            }
            else if (floorIndex < 0)
            {
                // 아래층 — 자기 층고를 빼며 내려간다(층고는 항상 아래 층 보유)
                for (int f = -1; f >= floorIndex; f--)
                {
                    y -= stack.Find(f).FloorHeight;
                }
            }

            return y;
        }

        /// <summary>층 f 계단의 총 라이즈(= 층 f 의 층고)를 구한다.</summary>
        /// <param name="stack">층 스택.</param>
        /// <param name="floorIndex">층 서수.</param>
        /// <returns>라이즈(m).</returns>
        public static float StairRise(MapFloorStackSO stack, int floorIndex)
        {
            return stack.Find(floorIndex).FloorHeight;
        }

        /// <summary>층 서수의 바닥면 Y 오프셋 — 빈 집 정의판(M10-1). 규약은 스택판과 동일(누적합·아래 층 보유). 순수 계산 — 로그 없음(CellMath 규약).</summary>
        /// <param name="definition">빈 집 정의.</param>
        /// <param name="floorIndex">층 서수(B1 = -1 · 1F = 0 · 2F = +1).</param>
        /// <returns>바닥면 Y 오프셋(m).</returns>
        public static float FloorPlaneY(MapDefinitionSO definition, int floorIndex)
        {
            // TODO(impl): 스택판과 같은 누적합 — definition.FloorOf(f).FloorHeight 소비
            return default;
        }

        /// <summary>층 f 계단의 총 라이즈 — 빈 집 정의판(M10-1). 순수 계산 — 로그 없음(CellMath 규약).</summary>
        /// <param name="definition">빈 집 정의.</param>
        /// <param name="floorIndex">층 서수.</param>
        /// <returns>라이즈(m).</returns>
        public static float StairRise(MapDefinitionSO definition, int floorIndex)
        {
            // TODO(impl): definition.FloorOf(floorIndex).FloorHeight
            return default;
        }

        /// <summary>
        /// 맵 루트 아래에 층별 루트 Transform 을 만든다 — 각 루트는 XZ 는 그대로 두고 Y 만 <see cref="FloorPlaneY"/> 로 이동한다.
        /// (계단 연결 층 쌍은 CellMeters 가 동일하도록 린트가 강제하므로 XZ 정규화는 전역 하나로 충분하다.)
        /// 현행 조립기는 방 Y 를 직접 가산해 단일 루트를 유지한다 — 이 헬퍼는 층 단위 토글·연출이 필요해질 때의 공용 경로다.
        /// </summary>
        /// <param name="mapRoot">맵 루트.</param>
        /// <param name="stack">층 스택.</param>
        /// <returns>층 서수 → 층 루트 Transform 매핑.</returns>
        public static System.Collections.Generic.Dictionary<int, Transform> CreateFloorRoots(Transform mapRoot, MapFloorStackSO stack)
        {
            var roots = new System.Collections.Generic.Dictionary<int, Transform>();
            for (int i = 0; i < stack.Floors.Length; i++)
            {
                int floorIndex = stack.Floors[i].FloorIndex;
                var root = new GameObject($"Floor_{floorIndex}");
                root.transform.SetParent(mapRoot, false);
                root.transform.localPosition = new Vector3(0f, FloorPlaneY(stack, floorIndex), 0f);
                roots[floorIndex] = root.transform;
            }

            return roots;
        }
    }
}
