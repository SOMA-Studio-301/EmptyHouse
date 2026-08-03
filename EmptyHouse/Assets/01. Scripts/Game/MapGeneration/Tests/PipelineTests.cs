using NUnit.Framework;

namespace EmptyHouse.MapGen.Core.Tests
{
    /// <summary>
    /// M5 — BlueprintValidator · MapGenerator(리롤 루프) 테스트.
    /// 손상 블루프린트는 픽스처를 복제 후 규칙 하나만 고의로 깨뜨려 만든다.
    /// </summary>
    public sealed class PipelineTests
    {
        /// <summary>패스 1 — 필수 아이템(백신·기름·열쇠) 미도달 블루프린트를 검출한다(7절 1).</summary>
        [Test]
        [Ignore("TODO(impl): M5")]
        public void Validate_필수_아이템_미도달을_검출한다()
        {
            // TODO(impl):
        }

        /// <summary>패스 2 — 열쇠_i ∉ R_i 위반 블루프린트를 검출한다(7절 2).</summary>
        [Test]
        [Ignore("TODO(impl): M5")]
        public void Validate_열쇠_불변식_위반을_검출한다()
        {
            // TODO(impl):
        }

        /// <summary>패스 3 — 파훼 쌍 A등급(투척물·사체 충전소) 위반을 검출한다(7절 3·6절).</summary>
        [Test]
        [Ignore("TODO(impl): M5")]
        public void Validate_파훼_쌍_A등급_위반을_검출한다()
        {
            // TODO(impl):
        }

        /// <summary>패스 4 — 가치 미달 지름길을 검출한다(7절 4).</summary>
        [Test]
        [Ignore("TODO(impl): M5")]
        public void Validate_지름길_가치_미달을_검출한다()
        {
            // TODO(impl):
        }

        /// <summary>B등급 파훼 쌍(발전기) 미충족은 실패가 아니라 경고만 남긴다(AC-16 · X6).</summary>
        [Test]
        [Ignore("TODO(impl): M5")]
        public void Validate_B등급_미충족은_경고만_남긴다()
        {
            // TODO(impl):
        }

        /// <summary>RerollMax 초과 시 무한 루프 없이 명시적 실패를 반환한다(AC-18 · X2 — 폴백 맵 없음).</summary>
        [Test]
        [Ignore("TODO(impl): M5")]
        public void Generate_리롤_상한_초과_시_명시적으로_실패한다()
        {
            // TODO(impl):
        }

        /// <summary>결과에 채택 시드·리롤 횟수·실패 사유가 보존된다(AC-17 — 버그 재현 키).</summary>
        [Test]
        [Ignore("TODO(impl): M5")]
        public void Generate_결과에_시드와_리롤_횟수가_보존된다()
        {
            // TODO(impl):
        }

        /// <summary>같은 파라미터·같은 시드로 2회 실행한 전체 파이프라인 결과가 완전 동일하다(AC-01).</summary>
        [Test]
        [Ignore("TODO(impl): M5")]
        public void Generate_같은_입력은_같은_블루프린트를_만든다()
        {
            // TODO(impl):
        }
    }
}
