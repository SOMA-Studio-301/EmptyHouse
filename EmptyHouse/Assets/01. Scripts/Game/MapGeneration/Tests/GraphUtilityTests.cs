using System.Collections.Generic;
using NUnit.Framework;

namespace EmptyHouse.MapGen.Core.Tests
{
    /// <summary>
    /// M1 — ReachabilityAnalyzer · DangerGradeCalculator 기대값 테스트.
    /// 재료: BlueprintFixtures.CreateMiniBlueprint (수기 계산 가능한 미니 그래프).
    /// </summary>
    public sealed class GraphUtilityTests
    {
        /// <summary>R_i 가 자물쇠 i 이후 뒤 구역을 제외하는지 검증한다(4-3절).</summary>
        [Test]
        public void ComputeReachableRooms_자물쇠_뒤_방을_제외한다()
        {
            MapBlueprint blueprint = BlueprintFixtures.CreateMiniBlueprint();

            // R_1: 자물쇠 1·2 전부 잠김 — 방 3은 e2(잠금2)·e5(잠금1) 뒤라 제외
            HashSet<int> r1 = ReachabilityAnalyzer.ComputeReachableRooms(blueprint, 1);

            Assert.That(r1, Is.EquivalentTo(new[] { 0, 1, 2, 4, 5 }));
        }

        /// <summary>R_i 가 자물쇠 1~i−1 을 열린 것으로 취급하는지 검증한다(4-3절).</summary>
        [Test]
        public void ComputeReachableRooms_선행_자물쇠는_열린_것으로_취급한다()
        {
            MapBlueprint blueprint = BlueprintFixtures.CreateMiniBlueprint();

            // R_2: 자물쇠 1(e5 루프)은 열림 취급 — 방 3이 4→3 경로로 편입, 자물쇠 2(e2)는 여전히 잠김
            HashSet<int> r2 = ReachabilityAnalyzer.ComputeReachableRooms(blueprint, 2);

            Assert.That(r2, Does.Contain(3), "선행 자물쇠 1이 열리면 루프 간선 e5로 방 3에 도달해야 한다");
            Assert.That(r2, Is.EquivalentTo(new[] { 0, 1, 2, 3, 4, 5 }));
        }

        /// <summary>관문 간선 차단 BFS 가 관문 뒤 구역을 제외하는지 검증한다(6절 파훼 쌍 판정).</summary>
        [Test]
        public void ComputeReachableWithEdgeBlocked_관문_뒤_구역을_제외한다()
        {
            MapBlueprint blueprint = BlueprintFixtures.CreateMiniBlueprint();

            // e4(4-5) 를 관문 취급 — 방 5는 다른 경로가 없어 제외. 잠긴 문(e2·e5)은 간선으로 통행
            HashSet<int> reachable = ReachabilityAnalyzer.ComputeReachableWithEdgeBlocked(blueprint, 4);

            Assert.That(reachable, Is.EquivalentTo(new[] { 0, 1, 2, 3, 4 }));
        }

        /// <summary>위험 깊이 계산이 잠긴 문도 간선으로 취급하는지 검증한다(3절·AC-06 기준).</summary>
        [Test]
        public void ComputeDepths_잠긴_문도_간선으로_취급한다()
        {
            MapBlueprint blueprint = BlueprintFixtures.CreateMiniBlueprint();

            int[] depths = DangerGradeCalculator.ComputeDepths(blueprint);

            // 방 3 깊이 3 = 잠긴 e2(2→3) 를 간선으로 센 값(잠금 무시 시에도 e5 경유 4가 아니라 3이 최단)
            Assert.That(depths, Is.EqualTo(new[] { 0, 1, 2, 3, 3, 4 }));
        }

        /// <summary>봉인 간선(RoomB = -1, 막힌 벽)이 통행으로 취급되지 않는지 검증한다.</summary>
        [Test]
        public void ComputeDepths_봉인_간선은_통행이_아니다()
        {
            MapBlueprint blueprint = BlueprintFixtures.CreateMiniBlueprint();

            // e6(방 5, RoomB=-1) 봉인 — 이웃으로 순회하면 예외/유령 방이 생긴다
            int[] depths = null;
            Assert.DoesNotThrow(() => depths = DangerGradeCalculator.ComputeDepths(blueprint));
            Assert.That(depths.Length, Is.EqualTo(6), "봉인 간선이 방 수를 늘리면 안 된다");

            HashSet<int> reachable = ReachabilityAnalyzer.ComputeReachableRooms(blueprint, 1);
            Assert.That(reachable.Contains(-1), Is.False, "봉인 간선의 RoomB(-1) 가 도달 집합에 들어가면 안 된다");
        }
    }
}
