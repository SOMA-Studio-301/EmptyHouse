using Border.Core;

namespace EmptyHouse.MapGen.Core
{
    /// <summary>
    /// MapBlueprint 데이터 단계 검증(7절) — 하나라도 실패하면 호출자가 시드를 리롤한다(X1).
    /// 프리팹 인스턴스화 없이 실행된다(AC-03).
    /// </summary>
    public sealed class BlueprintValidator
    {
        /// <summary>
        /// 4종 패스를 모두 실행해 리포트를 만든다.
        /// B등급 파훼 쌍(발전기) 미충족은 실패가 아니라 경고로 기록한다(6절·X6·AC-16).
        /// </summary>
        /// <param name="blueprint">검증할 블루프린트.</param>
        /// <param name="genParams">생성 파라미터(지름길 임계·Listener 보장 거리).</param>
        /// <returns>패스별 통과/실패와 사유를 담은 리포트.</returns>
        public ValidationReport Validate(MapBlueprint blueprint, MapGenParams genParams)
        {
            // TODO(impl): 4종 패스 실행 → AllPassed·FailReasons·Warnings 기록
            Log.D("[BlueprintValidator] Validate");
            return default;
        }

        /// <summary>패스 1 — 필수 아이템(백신·기름·모든 열쇠)이 전부 도달 가능한지 검사한다(7절 1).</summary>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="report">결과를 기록할 리포트.</param>
        /// <returns>통과 여부.</returns>
        private bool CheckEssentialsReachable(MapBlueprint blueprint, ValidationReport report)
        {
            // TODO(impl):
            Log.D("[BlueprintValidator] CheckEssentialsReachable");
            return default;
        }

        /// <summary>패스 2 — 열쇠_i ∈ R_i 전수 검사(7절 2·4-3절·AC-08).</summary>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="report">결과를 기록할 리포트.</param>
        /// <returns>통과 여부.</returns>
        private bool CheckKeyInvariant(MapBlueprint blueprint, ValidationReport report)
        {
            // TODO(impl): ReachabilityAnalyzer.ComputeReachableRooms 재사용
            Log.D("[BlueprintValidator] CheckKeyInvariant");
            return default;
        }

        /// <summary>
        /// 패스 3 — 파훼 쌍 검사(7절 3·6절). A등급(Listener 길목↔투척물, HerdArea↔사체 충전소)은 실패,
        /// B등급(Watcher 어둠 구간↔발전기)은 경고만 기록한다(X6).
        /// </summary>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="genParams">생성 파라미터(Listener 보장 거리).</param>
        /// <param name="report">결과를 기록할 리포트.</param>
        /// <returns>A등급 통과 여부.</returns>
        private bool CheckHardlockPairs(MapBlueprint blueprint, MapGenParams genParams, ValidationReport report)
        {
            // TODO(impl): ReachabilityAnalyzer.ComputeReachableWithEdgeBlocked 재사용
            Log.D("[BlueprintValidator] CheckHardlockPairs");
            return default;
        }

        /// <summary>패스 4 — 채택된 지름길 전부 귀환 단축 이득 ≥ ShortcutValueMin 검사(7절 4·AC-10).</summary>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="genParams">생성 파라미터(지름길 임계).</param>
        /// <param name="report">결과를 기록할 리포트.</param>
        /// <returns>통과 여부.</returns>
        private bool CheckShortcutValues(MapBlueprint blueprint, MapGenParams genParams, ValidationReport report)
        {
            // TODO(impl):
            Log.D("[BlueprintValidator] CheckShortcutValues");
            return default;
        }
    }
}
