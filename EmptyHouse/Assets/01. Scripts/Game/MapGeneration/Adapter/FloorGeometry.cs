using Border.Core;
using UnityEngine;

namespace EmptyHouse.MapGen.Runtime
{
    /// <summary>
    /// 층 기하 계산(M8) — 조립기·에디터 빌더·NavMesh 베이커·상태 오브젝트 스포너가 **공용**으로 쓴다.
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
        /// 부호 오프바이원이 나오는 유일한 식이라 <c>FloorPlaneY(-1)/(0)/(+1)</c> 단위 테스트를 반드시 둔다.
        /// </summary>
        /// <param name="stack">층 스택(아래→위 정렬 전제 아님 — 서수로 조회한다).</param>
        /// <param name="floorIndex">층 서수(B1 = -1 · 1F = 0 · 2F = +1).</param>
        /// <returns>바닥면 Y 오프셋(m).</returns>
        public static float FloorPlaneY(MapFloorStackSO stack, int floorIndex)
        {
            // TODO(impl):
            Log.D($"[FloorGeometry] FloorPlaneY floor={floorIndex}");
            return default;
        }

        /// <summary>층 f 계단의 총 라이즈(= 층 f 의 층고)를 구한다.</summary>
        /// <param name="stack">층 스택.</param>
        /// <param name="floorIndex">층 서수.</param>
        /// <returns>라이즈(m).</returns>
        public static float StairRise(MapFloorStackSO stack, int floorIndex)
        {
            // TODO(impl):
            Log.D($"[FloorGeometry] StairRise floor={floorIndex}");
            return default;
        }

        /// <summary>
        /// 맵 루트 아래에 층별 루트 Transform 을 만든다 — 각 루트는 XZ 는 그대로 두고 Y 만 <see cref="FloorPlaneY"/> 로 이동한다.
        /// (계단 연결 층 쌍은 CellMeters 가 동일하도록 X4 가 강제하므로 XZ 정규화는 전역 하나로 충분하다.)
        /// </summary>
        /// <param name="mapRoot">맵 루트.</param>
        /// <param name="stack">층 스택.</param>
        /// <returns>층 서수 → 층 루트 Transform 매핑.</returns>
        public static System.Collections.Generic.Dictionary<int, Transform> CreateFloorRoots(Transform mapRoot, MapFloorStackSO stack)
        {
            // TODO(impl):
            Log.D("[FloorGeometry] CreateFloorRoots");
            return default;
        }
    }
}
