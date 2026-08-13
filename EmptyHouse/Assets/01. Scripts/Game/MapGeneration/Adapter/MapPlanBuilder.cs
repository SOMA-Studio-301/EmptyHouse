using System.Collections.Generic;
using Border.Core;
using EmptyHouse.MapGen.Core;

namespace EmptyHouse.MapGen.Runtime
{
    /// <summary>
    /// 빈 집 정의 → 생성 계획 조립(M10-1, 설계 B′) — 구 <c>MapFloorPlanAssembler</c>(층 스택판)의 승계.
    /// 층 서수(리스트 위치 − BasementCount)·ThemeId 를 여기서 코어 파라미터 복제본에 스탬프하고
    /// (<see cref="FloorDefinitionSO"/> 는 서수를 모른다), TemplateId 에 층 접미사(@f{N})를 붙여 전층 유일(X4)을 만족시킨다.
    /// **코어는 무수정** — 출력은 여전히 <see cref="MapGenPlan"/> 하나(설계 B′ 경계).
    /// </summary>
    public static class MapPlanBuilder
    {
        /// <summary>
        /// 정의가 조립 가능한 상태인지 검사한다(R4 — 경고가 아니라 조립 거부).
        /// 층 0개 · 계단 연결 층 쌍 CellMeters 불일치 · 계단 템플릿 풋프린트/소켓 불일치 · 환경 프리팹 결손 등.
        /// </summary>
        /// <param name="definition">빈 집 정의.</param>
        /// <param name="errors">실패 사유 축적 리스트(규칙 단위 문자열).</param>
        /// <returns>통과 여부.</returns>
        public static bool Lint(MapDefinitionSO definition, List<string> errors)
        {
            // TODO(impl): MapFloorPlanAssembler.Lint 이관 + 정의 기반 항목(층 서수 유도·CommonRegistry 결손) 추가
            Log.D("[MapPlanBuilder] Lint");
            return default;
        }

        /// <summary>
        /// 빈 집 정의와 시드 확정 스냅샷에서 생성 계획을 조립한다 — 층별 FloorGenParams 복제본에
        /// FloorIndex·ThemeId 스탬프 → 층 템플릿 추출·접미사 처리 → <see cref="MapGenPlan.Compose"/>.
        /// </summary>
        /// <param name="definition">빈 집 정의.</param>
        /// <param name="snapshot">시드가 박힌 맵 전역 파라미터 스냅샷(에셋 원본 오염 금지 — 복제본).</param>
        /// <param name="flatTemplateAssets">평탄화 순서와 같은 템플릿 SO 배열(조립기의 프리팹 조회용) — 실패 시 null.</param>
        /// <returns>생성 계획 — 린트 실패 시 null(조립 거부, R4).</returns>
        public static MapGenPlan Build(MapDefinitionSO definition, MapGenParams snapshot, out RoomTemplateSO[] flatTemplateAssets)
        {
            // TODO(impl): MapFloorPlanAssembler.Build 이관 — 층 스택 순회를 정의 순회로, 서수는 FloorIndexOf 로 스탬프
            Log.D("[MapPlanBuilder] Build");
            flatTemplateAssets = default;
            return default;
        }
    }
}
