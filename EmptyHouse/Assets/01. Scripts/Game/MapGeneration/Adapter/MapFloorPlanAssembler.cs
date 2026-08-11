using System.Collections.Generic;
using Border.Core;
using EmptyHouse.MapGen.Core;
using UnityEngine;

namespace EmptyHouse.MapGen.Runtime
{
    /// <summary>
    /// 층 스택 SO → 코어 생성 계획 조립(M9-8) — 어댑터가 맵·테마·프리팹을 알고 코어는 Plan 만 본다(AC-03 경계).
    /// 전 층이 같은 테마 레지스트리를 공유해도 TemplateId 는 층 접미사(@f{서수})로 유일화한다(X4 ⑤).
    /// 프리팹 역참조는 문자열이 아니라 평탄화 인덱스 정렬 배열(FlatTemplateAssets)로 한다 —
    /// 접미사 붙은 ID 로 레지스트리를 다시 뒤지는 경로를 없앤다.
    /// </summary>
    public static class MapFloorPlanAssembler
    {
        /// <summary>
        /// 층 스택에서 Plan 과 평탄화 템플릿 SO 배열(코어 FlatTemplates 와 같은 순서)을 조립한다.
        /// 입구 앵커 템플릿은 층 서수 0(시드 층)에만 포함한다(X4 ②). 층 서수 0 의 ID 는 접미사 없이 유지된다.
        /// </summary>
        /// <param name="stack">층 스택.</param>
        /// <param name="genParams">전역 파라미터(시드 미확정 허용 — 호출자가 스냅샷에 시드를 박는다).</param>
        /// <param name="flatTemplateAssets">평탄화 인덱스 → 템플릿 SO(프리팹·변형 원천) 출력.</param>
        /// <returns>생성 계획.</returns>
        public static MapGenPlan Build(MapFloorStackSO stack, MapGenParams genParams, out RoomTemplateSO[] flatTemplateAssets)
        {
            var floorParams = new FloorGenParams[stack.Floors.Length];
            var floorSets = new FloorTemplateSet[stack.Floors.Length];
            var flatAssets = new List<RoomTemplateSO>();

            for (int i = 0; i < stack.Floors.Length; i++)
            {
                FloorPrefabSet entry = stack.Floors[i];
                string suffix = entry.FloorIndex == 0 ? string.Empty : $"@f{entry.FloorIndex}";

                var defs = new List<RoomTemplateDef>();
                for (int t = 0; t < entry.Registry.Templates.Length; t++)
                {
                    RoomTemplateSO asset = entry.Registry.Templates[t];
                    if (asset.IsEntranceAnchor && entry.FloorIndex != 0)
                    {
                        continue; // 입구는 시드 층 전용(X4 ②)
                    }

                    RoomTemplateDef def = asset.ToDef();
                    def.TemplateId += suffix;
                    defs.Add(def);
                    flatAssets.Add(asset);
                }

                if (entry.StairTemplate != null && stack.Floors.Length > 1)
                {
                    RoomTemplateDef stair = entry.StairTemplate.ToDef();
                    stair.TemplateId += $"@f{entry.FloorIndex}"; // 계단은 전 층 같은 SO 재사용 가능 — 층 접미사로 유일화(시드 층 포함)
                    defs.Add(stair);
                    flatAssets.Add(entry.StairTemplate);
                }

                // 층 파라미터 — 스택 항목의 코어 파라미터를 쓰되 서수·테마는 스택 값으로 강제(드리프트 차단)
                FloorGenParams cloned = entry.GenParams ?? new FloorGenParams();
                floorParams[i] = new FloorGenParams
                {
                    FloorIndex = entry.FloorIndex,
                    ThemeId = entry.ThemeId,
                    RoomsTotalMin = cloned.RoomsTotalMin,
                    RoomsTotalMax = cloned.RoomsTotalMax,
                    CycleRoomPercent = cloned.CycleRoomPercent,
                    CorridorLinkPercent = cloned.CorridorLinkPercent,
                    CorridorChainMax = cloned.CorridorChainMax,
                    DangerBias = cloned.DangerBias,
                    ZombieDensitySafeMin = cloned.ZombieDensitySafeMin,
                    ZombieDensitySafeMax = cloned.ZombieDensitySafeMax,
                    ZombieDensityMidMin = cloned.ZombieDensityMidMin,
                    ZombieDensityMidMax = cloned.ZombieDensityMidMax,
                    ZombieDensityDangerMin = cloned.ZombieDensityDangerMin,
                    ZombieDensityDangerMax = cloned.ZombieDensityDangerMax,
                    ListenerRatioPercent = cloned.ListenerRatioPercent,
                    HerdZombieCountMin = cloned.HerdZombieCountMin,
                    HerdZombieCountMax = cloned.HerdZombieCountMax,
                    EnabledZombieTypes = cloned.EnabledZombieTypes,
                };
                floorSets[i] = new FloorTemplateSet { FloorIndex = entry.FloorIndex, ThemeId = entry.ThemeId, Templates = defs.ToArray() };
            }

            flatTemplateAssets = flatAssets.ToArray();
            return MapGenPlan.Compose(genParams, floorParams, floorSets);
        }

        /// <summary>
        /// 층 스택 어댑터 린트(M9-8 — 경고가 아니라 조립 거부 재료, R4) — 미터 정합 결함은 4패스를 전부 통과하므로
        /// 코어가 못 잡는다. ① 계단 연결 층 쌍 CellMeters 동일 ② 층고·계단 기하 성립(FloorPlaneY 부호 검증)
        /// ③ ThemeId 코어·어댑터 대조 ④ 다층이면 전 층 StairTemplate 존재.
        /// </summary>
        /// <param name="stack">층 스택.</param>
        /// <param name="errors">결함 사유 수집 목록.</param>
        /// <returns>결함이 없으면 true.</returns>
        public static bool Lint(MapFloorStackSO stack, List<string> errors)
        {
            if (stack.Floors == null || stack.Floors.Length == 0)
            {
                errors.Add("층 스택이 비어 있다");
                return false;
            }

            for (int i = 0; i < stack.Floors.Length; i++)
            {
                FloorPrefabSet entry = stack.Floors[i];
                if (entry.Registry == null)
                {
                    errors.Add($"층 {entry.FloorIndex}: 레지스트리 미배선");
                    continue;
                }

                if (!Mathf.Approximately(entry.CellMeters, stack.Floors[0].CellMeters))
                {
                    errors.Add($"층 {entry.FloorIndex}: CellMeters({entry.CellMeters}) ≠ 기준({stack.Floors[0].CellMeters}) — 계단 연결 층은 셀 실측이 같아야 샤프트 XZ 가 정렬된다");
                }

                if (entry.FloorHeight <= 0f)
                {
                    errors.Add($"층 {entry.FloorIndex}: FloorHeight({entry.FloorHeight}) ≤ 0");
                }

                if (stack.Floors.Length > 1 && entry.StairTemplate == null)
                {
                    errors.Add($"층 {entry.FloorIndex}: StairTemplate 미배선 — 다층은 층마다 계단실이 필요하다(X4 ③)");
                }

                if (entry.GenParams != null && entry.GenParams.ThemeId != null && entry.GenParams.ThemeId != entry.ThemeId)
                {
                    errors.Add($"층 {entry.FloorIndex}: ThemeId 드리프트 — 스택({entry.ThemeId}) vs 코어 파라미터({entry.GenParams.ThemeId})");
                }
            }

            // FloorPlaneY 부호 검증 — 오프바이원이 나오는 유일한 식(설계 D 단위 검증을 린트로 수행)
            if (stack.Find(0) != null)
            {
                float y0 = FloorGeometry.FloorPlaneY(stack, 0);
                if (!Mathf.Approximately(y0, 0f))
                {
                    errors.Add($"FloorPlaneY(0) = {y0} — 0 이어야 한다");
                }

                FloorPrefabSet up = stack.Find(1);
                if (up != null && !Mathf.Approximately(FloorGeometry.FloorPlaneY(stack, 1), stack.Find(0).FloorHeight))
                {
                    errors.Add("FloorPlaneY(+1) ≠ FloorHeight(0) — 위층 오프셋 식 결함");
                }

                FloorPrefabSet down = stack.Find(-1);
                if (down != null && !Mathf.Approximately(FloorGeometry.FloorPlaneY(stack, -1), -down.FloorHeight))
                {
                    errors.Add("FloorPlaneY(-1) ≠ -FloorHeight(-1) — 아래층 오프셋 식 결함(층고는 아래 층 보유)");
                }
            }

            for (int i = 0; i < errors.Count; i++)
            {
                Log.E($"[MapFloorPlanAssembler] 린트 실패 — {errors[i]}");
            }

            return errors.Count == 0;
        }
    }
}
