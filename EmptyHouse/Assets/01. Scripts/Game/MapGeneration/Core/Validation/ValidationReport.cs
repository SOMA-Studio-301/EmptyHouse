using System.Collections.Generic;

namespace EmptyHouse.MapGen.Core
{
    /// <summary>
    /// 검증 패스 결과(7절 4종 + 경고). 기획자 툴의 검증 결과 패널이 그대로 표시한다(10절·AC-23).
    /// </summary>
    public sealed class ValidationReport
    {
        public bool EssentialsReachable; // 패스 1 — 필수 아이템(백신·기름·모든 열쇠) 전부 도달 가능
        public bool KeyInvariantHolds; // 패스 2 — 열쇠_i ∈ R_i 전수 성립(4-3절)
        public bool HardlockPairsHold; // 패스 3 — 파훼 쌍 A등급 성립(6절)
        public bool ShortcutValuesHold; // 패스 4 — 채택 지름길 가치 ≥ 임계(4-2절)
        public bool FloorReachabilityHolds = true; // 패스 5(M9-5) — 전 방 도달 + 비시드 층마다 수직 간선 ≥ 1. 층 1개면 전 방 도달만 검사
        public bool AllPassed; // 전 패스 통과(검증기가 기록)
        public List<string> FailReasons = new List<string>(); // 실패 사유(규칙 단위 — X1)
        public List<string> Warnings = new List<string>(); // B등급 파훼 쌍 미충족 등 경고(X6)
    }
}
