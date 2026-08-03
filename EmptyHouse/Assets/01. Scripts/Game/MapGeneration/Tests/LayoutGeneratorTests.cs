using NUnit.Framework;

namespace EmptyHouse.MapGen.Core.Tests
{
    /// <summary>
    /// M2 — LayoutGenerator property 테스트. 시드 100개 반복 생성 후 규칙 전수 검사.
    /// 재료: BlueprintFixtures.CreateFakeTemplates · CreateTestParams.
    /// </summary>
    public sealed class LayoutGeneratorTests
    {
        /// <summary>모든 방·복도가 격자 정수 좌표에 스냅되고 풋프린트가 겹치지 않는다(AC-04).</summary>
        [Test]
        [Ignore("TODO(impl): M2")]
        public void TryGenerate_풋프린트가_겹치지_않고_정수_좌표에_스냅된다()
        {
            // TODO(impl):
        }

        /// <summary>빈 소켓 0 — 전부 문/통로/막힌 벽 중 하나로 채워진다(AC-05).</summary>
        [Test]
        [Ignore("TODO(impl): M2")]
        public void TryGenerate_빈_소켓이_없다()
        {
            // TODO(impl):
        }

        /// <summary>입구에서 모든 방까지 경로가 존재한다 — 잠긴 문도 간선으로 친다(AC-06).</summary>
        [Test]
        [Ignore("TODO(impl): M2")]
        public void TryGenerate_고립_방이_없다()
        {
            // TODO(impl):
        }

        /// <summary>루프 간선 수가 파라미터 min/max 범위 안이다(AC-07).</summary>
        [Test]
        [Ignore("TODO(impl): M2")]
        public void TryGenerate_루프_간선_수가_파라미터_범위_안이다()
        {
            // TODO(impl):
        }

        /// <summary>같은 시드 2회 생성 결과가 BlueprintDump 기준 완전 동일하다(AC-01).</summary>
        [Test]
        [Ignore("TODO(impl): M2")]
        public void TryGenerate_같은_시드는_같은_레이아웃을_만든다()
        {
            // TODO(impl):
        }
    }
}
