using Border.Core;

namespace EmptyHouse.MapGen.Core.Tests
{
    /// <summary>
    /// 블루프린트 → 정규화 문자열 덤프 — 결정론 비교(AC-01·AC-02 근거) 전용 테스트 헬퍼.
    /// 같은 블루프린트는 항상 같은 문자열이 되도록 고정 포맷·고정 순서(리스트 인덱스 순)로 직렬화한다.
    /// </summary>
    public static class BlueprintDump
    {
        /// <summary>
        /// 블루프린트 전체(meta 제외한 rooms/edges/spawns)를 정규화 문자열로 덤프한다.
        /// 두 생성 결과의 완전 동일성은 이 문자열 비교로 판정한다.
        /// </summary>
        /// <param name="blueprint">덤프할 블루프린트.</param>
        /// <returns>정규화 덤프 문자열.</returns>
        public static string Dump(MapBlueprint blueprint)
        {
            // TODO(impl):
            Log.D("[BlueprintDump] Dump");
            return default;
        }
    }
}
