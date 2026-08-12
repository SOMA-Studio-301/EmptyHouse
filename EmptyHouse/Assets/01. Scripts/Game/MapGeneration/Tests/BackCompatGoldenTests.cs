using System.IO;
using System.Text;
using EmptyHouse.MapGen.Runtime;
using NUnit.Framework;

namespace EmptyHouse.MapGen.Core.Tests
{
    /// <summary>
    /// v1 하위호환 골든 게이트(M9-2) — 층 1개(현행) 구성의 시드 1~100 블루프린트 덤프를
    /// Tests/Golden/ 고정본과 대조한다. 다층 작업 전 기간, rng 스트림 오염(신규 난수 소비)이
    /// 단 1회만 끼어도 여기서 잡힌다. 골든이 없는 시드는 캡처 후 **실패**로 알린다(암묵 캡처 금지) —
    /// 캡처본을 커밋하고 재실행하면 통과한다.
    /// </summary>
    public sealed class BackCompatGoldenTests
    {
        private const int seedCount = 100; // 골든 시드 수(1부터 연속)

        /// <summary>골든 폴더 절대 경로 — 이 소스 파일 위치 기준(작업 디렉터리 무관).</summary>
        private static string GoldenDir => Path.Combine(ProjectRoot(), "Assets", "01. Scripts", "Game", "MapGeneration", "Tests", "Golden");

        /// <summary>층 1개(현행 v1) 구성 100시드가 골든 덤프와 전부 일치한다 — 다층 도입의 결과 불변 증명.</summary>
        [Test]
        public void Generate_단일층_구성은_v1_골든과_일치한다()
        {
            Directory.CreateDirectory(GoldenDir);
            var generator = new MapGenerator();
            int captured = 0;
            var mismatches = new StringBuilder();

            for (int seed = 1; seed <= seedCount; seed++)
            {
                var genParams = new MapGenParams { Seed = seed };
                MapGenResult result = generator.Generate(genParams, MapTemplateCatalog.Create());
                Assert.That(result.Success, Is.True, $"시드 {seed}: 생성 실패 — {string.Join(" / ", result.FailReasons)}");

                string dump = BlueprintDump.Dump(result.Blueprint);
                string path = Path.Combine(GoldenDir, $"seed_{seed:0000}.txt");
                if (!File.Exists(path))
                {
                    File.WriteAllText(path, dump, new UTF8Encoding(false));
                    captured++;
                    continue;
                }

                // 개행 정규화 — git autocrlf 가 체크아웃에서 \n 을 \r\n 으로 바꿔도 골든이 깨지지 않게
                if (File.ReadAllText(path, Encoding.UTF8).Replace("\r\n", "\n") != dump)
                {
                    mismatches.Append("시드 ").Append(seed).Append(' ');
                }
            }

            Assert.That(captured, Is.EqualTo(0),
                $"골든 {captured}건 신규 캡처 — Tests/Golden/ 을 커밋하고 재실행하라(암묵 통과 금지)");
            Assert.That(mismatches.Length, Is.EqualTo(0),
                $"골든 불일치: {mismatches}— 층 1개 결과가 v1 과 달라졌다(rng 스트림 오염 또는 알고리즘 변경). " +
                "의도된 변경이면 골든을 재캡처(폴더 삭제 후 실행)하고 그 사유를 커밋 메시지에 남긴다");
        }

        /// <summary>프로젝트 루트(Assets 상위) 절대 경로 — CurrentDirectory 가 프로젝트 루트가 아닐 때 대비.</summary>
        /// <returns>프로젝트 루트 경로.</returns>
        private static string ProjectRoot()
        {
            // 에디터 테스트는 CurrentDirectory = 프로젝트 루트가 보장된다(Unity 규약). Assets 존재로 검증만 한다
            string cwd = Directory.GetCurrentDirectory();
            Assert.That(Directory.Exists(Path.Combine(cwd, "Assets")), Is.True, $"프로젝트 루트가 아니다: {cwd}");
            return cwd;
        }
    }
}
