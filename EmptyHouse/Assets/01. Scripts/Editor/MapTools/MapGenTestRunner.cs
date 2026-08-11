using System.IO;
using System.Text;
using Border.Core;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace EmptyHouse.EditorTools
{
    /// <summary>
    /// EditMode 테스트를 코드로 실행하고 결과를 파일로 남긴다 — unity-cli exec 는 동기 반환이라
    /// 비동기 테스트 런의 결과를 받을 수 없어, 완료 콜백이 요약을 파일에 쓰고 밖에서 폴링한다.
    /// </summary>
    public static class MapGenTestRunner
    {
        private const string sessionKey = "MapGenTestRunner.OutPath"; // 도메인 리로드 생존용 결과 경로 보관 키

        private static ResultWriter activeCallbacks; // 콜백 GC 방지 보관(런 완료까지)

        /// <summary>
        /// 도메인 리로드 후 재부착 — 테스트 런 시작이 리로드를 유발해 정적 콜백이 유실되므로,
        /// 진행 중 런이 있으면(SessionState 에 경로 잔존) 새 콜백을 다시 등록해 RunFinished 를 받아낸다.
        /// </summary>
        [InitializeOnLoadMethod]
        private static void ReattachAfterReload()
        {
            string pending = SessionState.GetString(sessionKey, string.Empty);
            if (!string.IsNullOrEmpty(pending))
            {
                Register(pending);
            }
        }

        /// <summary>
        /// MapGen 테스트 어셈블리의 EditMode 테스트 전체를 실행한다. 완료 시 outPath 에 요약을 쓴다.
        /// </summary>
        /// <param name="outPath">결과 파일 절대 경로(기존 파일은 삭제 후 시작).</param>
        /// <returns>시작 확인 문자열.</returns>
        public static string RunAll(string outPath)
        {
            if (File.Exists(outPath))
            {
                File.Delete(outPath);
            }

            SessionState.SetString(sessionKey, outPath);
            TestRunnerApi api = Register(outPath);
            api.Execute(new ExecutionSettings(new Filter
            {
                testMode = TestMode.EditMode,
                assemblyNames = new[] { "EmptyHouse.MapGen.Core.Tests" },
            }));
            return $"[MapGenTestRunner] 시작 — 결과: {outPath}";
        }

        /// <summary>콜백을 만들어 TestRunnerApi 에 등록한다.</summary>
        /// <param name="outPath">결과 파일 절대 경로.</param>
        /// <returns>등록된 API 인스턴스.</returns>
        private static TestRunnerApi Register(string outPath)
        {
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            activeCallbacks = new ResultWriter(outPath);
            api.RegisterCallbacks(activeCallbacks);
            return api;
        }

        /// <summary>테스트 런 콜백 — 실패 목록과 집계를 파일로 쓴다.</summary>
        private sealed class ResultWriter : ICallbacks
        {
            private readonly string outPath; // 결과 파일 경로
            private readonly StringBuilder failures = new StringBuilder(); // 실패 테스트 축적

            /// <summary>결과 파일 경로를 받는다.</summary>
            /// <param name="path">결과 파일 절대 경로.</param>
            public ResultWriter(string path)
            {
                outPath = path;
            }

            /// <summary>런 시작 — 처리 없음.</summary>
            /// <param name="testsToRun">실행 예정 테스트 트리.</param>
            public void RunStarted(ITestAdaptor testsToRun)
            {
            }

            /// <summary>런 종료 — 집계·실패 목록을 파일로 쓰고 재부착 키를 지운다.</summary>
            /// <param name="result">런 결과 루트.</param>
            public void RunFinished(ITestResultAdaptor result)
            {
                var sb = new StringBuilder();
                sb.Append("PASS=").Append(result.PassCount)
                  .Append(" FAIL=").Append(result.FailCount)
                  .Append(" SKIP=").Append(result.SkipCount)
                  .Append('\n');
                sb.Append(failures);
                File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
                Log.D($"[MapGenTestRunner] 완료 — PASS {result.PassCount} FAIL {result.FailCount} SKIP {result.SkipCount}");
                SessionState.EraseString(sessionKey);
                activeCallbacks = null;
            }

            /// <summary>개별 테스트 시작 — 처리 없음.</summary>
            /// <param name="test">시작 테스트.</param>
            public void TestStarted(ITestAdaptor test)
            {
            }

            /// <summary>개별 테스트 종료 — 실패만 축적한다.</summary>
            /// <param name="result">테스트 결과.</param>
            public void TestFinished(ITestResultAdaptor result)
            {
                if (result.TestStatus == TestStatus.Failed)
                {
                    failures.Append("FAILED: ").Append(result.FullName).Append('\n')
                            .Append(result.Message).Append('\n');
                }
            }
        }
    }
}
