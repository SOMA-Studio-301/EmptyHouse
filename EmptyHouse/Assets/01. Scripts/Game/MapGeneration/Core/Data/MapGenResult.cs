using System.Collections.Generic;

namespace EmptyHouse.MapGen.Core
{
    /// <summary>Generate 호출 결과 — 실패 시에도 사유·리롤 횟수를 보고한다(X2·AC-17·AC-18).</summary>
    public sealed class MapGenResult
    {
        public bool Success; // 생성 성공 여부
        public MapBlueprint Blueprint; // 성공 시 확정 블루프린트(실패 시 null)
        public int RerollCount; // 소비한 리롤 횟수(로그 대상 — 7절)
        public List<string> FailReasons = new List<string>(); // 실패 사유(규칙 단위 — X1·X2)
        public ValidationReport LastReport; // 마지막 검증 리포트(실패 시 마지막 실패 사유 로그용)
    }
}
