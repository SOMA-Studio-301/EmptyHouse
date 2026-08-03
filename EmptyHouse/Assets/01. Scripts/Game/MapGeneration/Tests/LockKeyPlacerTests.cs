using NUnit.Framework;

namespace EmptyHouse.MapGen.Core.Tests
{
    /// <summary>
    /// M3 — LockKeyPlacer 테스트. AC-08 이 이 시스템의 대표 자동화 검증이다.
    /// </summary>
    public sealed class LockKeyPlacerTests
    {
        /// <summary>시드 1,000개 자동 생성에서 열쇠_i ∉ R_i 위반 0건(AC-08 · 4-3절 절대 규칙).</summary>
        [Test]
        [Ignore("TODO(impl): M3")]
        public void TryPlace_시드_1000개에서_R_불변식_위반이_없다()
        {
            // TODO(impl):
        }

        /// <summary>입구에서 열쇠를 번호 순서대로 회수하는 시뮬레이션이 모든 자물쇠를 연다(AC-09 — 솔로 100% 클리어).</summary>
        [Test]
        [Ignore("TODO(impl): M3")]
        public void TryPlace_번호순_회수_시뮬레이션이_모든_자물쇠를_연다()
        {
            // TODO(impl):
        }

        /// <summary>채택된 지름길 전부 귀환 단축 이득 ≥ ShortcutValueMin(AC-10 · 4-2절).</summary>
        [Test]
        [Ignore("TODO(impl): M3")]
        public void TryPlace_채택_지름길_가치가_임계_이상이다()
        {
            // TODO(impl):
        }

        /// <summary>폴백(X5) 미발동 시 자물쇠 인접 방에 열쇠가 배치된 사례 0건(AC-11 · 4-3절 우선순위 최하위).</summary>
        [Test]
        [Ignore("TODO(impl): M3")]
        public void TryPlace_폴백_없이_자물쇠_인접_방에_열쇠가_없다()
        {
            // TODO(impl):
        }
    }
}
