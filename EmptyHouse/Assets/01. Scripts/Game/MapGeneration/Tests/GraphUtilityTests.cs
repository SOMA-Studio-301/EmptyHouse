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
        [Ignore("TODO(impl): M1")]
        public void ComputeReachableRooms_자물쇠_뒤_방을_제외한다()
        {
            // TODO(impl):
        }

        /// <summary>R_i 가 자물쇠 1~i−1 을 열린 것으로 취급하는지 검증한다(4-3절).</summary>
        [Test]
        [Ignore("TODO(impl): M1")]
        public void ComputeReachableRooms_선행_자물쇠는_열린_것으로_취급한다()
        {
            // TODO(impl):
        }

        /// <summary>관문 간선 차단 BFS 가 관문 뒤 구역을 제외하는지 검증한다(6절 파훼 쌍 판정).</summary>
        [Test]
        [Ignore("TODO(impl): M1")]
        public void ComputeReachableWithEdgeBlocked_관문_뒤_구역을_제외한다()
        {
            // TODO(impl):
        }

        /// <summary>위험 깊이 계산이 잠긴 문도 간선으로 취급하는지 검증한다(3절·AC-06 기준).</summary>
        [Test]
        [Ignore("TODO(impl): M1")]
        public void ComputeDepths_잠긴_문도_간선으로_취급한다()
        {
            // TODO(impl):
        }

        /// <summary>봉인 간선(RoomB = -1, 막힌 벽)이 통행으로 취급되지 않는지 검증한다.</summary>
        [Test]
        [Ignore("TODO(impl): M1")]
        public void ComputeDepths_봉인_간선은_통행이_아니다()
        {
            // TODO(impl):
        }
    }
}
