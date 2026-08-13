using System.Collections.Generic;
using Border.Core;
using EmptyHouse.MapGen.Core;
using UnityEngine;

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
            Log.D("[MapPlanBuilder] Lint");
            if (definition.Floors == null || definition.Floors.Length == 0)
            {
                errors.Add("층이 없다");
                return false;
            }

            if (string.IsNullOrEmpty(definition.MapId))
            {
                errors.Add("MapId 가 비어 있다 — 네트워크 복제·해시 재현 키라 필수다");
            }

            if (definition.BasementCount < 0 || definition.BasementCount >= definition.Floors.Length)
            {
                errors.Add($"BasementCount({definition.BasementCount}) 범위 밖 — 서수 0(시드 층)이 존재하려면 0 ≤ BasementCount ≤ 층 수-1");
                LogAll(errors);
                return false; // 서수 유도가 성립하지 않아 이하 층별 검사가 무의미하다
            }

            if (definition.CommonRegistry == null)
            {
                errors.Add("CommonRegistry 미배선 — 테마 무관 상호작용·아이템 프리팹 원천이 필수다");
            }

            // 같은 층 정의 에셋이 두 슬롯에 배선되면 에러 — 층 재사용은 "다른 빈 집 간"이 전제이지 한 빈 집 안 중복이 아니다
            for (int slot = 0; slot < definition.Floors.Length; slot++)
            {
                for (int other = slot + 1; other < definition.Floors.Length; other++)
                {
                    if (definition.Floors[slot] != null && definition.Floors[slot] == definition.Floors[other])
                    {
                        errors.Add($"층 정의 중복 — 슬롯 {slot}(서수 {definition.FloorIndexOf(slot)})·슬롯 {other}(서수 {definition.FloorIndexOf(other)})가 같은 에셋({definition.Floors[slot].name}) — 층마다 별도 층 정의를 배선해야 한다");
                    }
                }
            }

            RoomTemplateSO referenceStair = null;
            for (int slot = 0; slot < definition.Floors.Length; slot++)
            {
                FloorDefinitionSO floor = definition.Floors[slot];
                int floorIndex = definition.FloorIndexOf(slot);
                if (floor == null)
                {
                    errors.Add($"층 {floorIndex}: 층 정의 미배선");
                    continue;
                }

                if (!Mathf.Approximately(floor.CellMeters, definition.Floors[0].CellMeters))
                {
                    errors.Add($"층 {floorIndex}: CellMeters({floor.CellMeters}) ≠ 기준({definition.Floors[0].CellMeters}) — 계단 연결 층은 셀 실측이 같아야 샤프트 XZ 가 정렬된다");
                }

                if (floor.FloorHeight <= 0f)
                {
                    errors.Add($"층 {floorIndex}: FloorHeight({floor.FloorHeight}) ≤ 0");
                }

                if (definition.Floors.Length > 1)
                {
                    if (floor.StairTemplate == null)
                    {
                        errors.Add($"층 {floorIndex}: StairTemplate 미배선 — 다층은 층마다 계단실이 필요하다(X4 ③)");
                    }
                    else if (referenceStair == null)
                    {
                        referenceStair = floor.StairTemplate;
                    }
                    else if (!SameStairShape(referenceStair, floor.StairTemplate))
                    {
                        errors.Add($"층 {floorIndex}: 계단실 {floor.StairTemplate.TemplateId} 풋프린트·소켓 배열이 기준({referenceStair.TemplateId})과 다르다 — 샤프트 좌표 복사(SSA)가 성립하지 않는다(X4 ④)");
                    }
                }

                // 위층이 있는 층은 완성 계단 프리팹이 필수 — 없으면 그 층에서 위층으로 걸어 올라갈 수 없다
                if (slot + 1 < definition.Floors.Length && floor.StairPrefab == null)
                {
                    errors.Add($"층 {floorIndex}: StairPrefab 미배선 — 위층(서수 {floorIndex + 1})이 있어 계단 삽입이 필요하다");
                }

                // 환경 프리팹 결손 — 정의가 환경 소유가 된 대가로 층마다 검사한다(구 레지스트리 시절엔 전역 1회)
                if (floor.SealWallPrefab == null)
                {
                    errors.Add($"층 {floorIndex}: SealWallPrefab 미배선");
                }

                if (floor.DoorPrefab == null)
                {
                    errors.Add($"층 {floorIndex}: DoorPrefab 미배선");
                }

                if (floor.ReturnExitPrefab == null)
                {
                    errors.Add($"층 {floorIndex}: ReturnExitPrefab 미배선");
                }

                if (floor.GenParams != null && floor.GenParams.ThemeId != null && floor.GenParams.ThemeId != floor.ThemeId)
                {
                    errors.Add($"층 {floorIndex}: ThemeId 드리프트 — 층 정의({floor.ThemeId}) vs 코어 파라미터({floor.GenParams.ThemeId})");
                }
            }

            // FloorPlaneY 부호 검증 — 오프바이원이 나오는 유일한 식(설계 D 단위 검증을 린트로 수행)
            if (definition.FloorOf(0) != null)
            {
                float y0 = FloorGeometry.FloorPlaneY(definition, 0);
                if (!Mathf.Approximately(y0, 0f))
                {
                    errors.Add($"FloorPlaneY(0) = {y0} — 0 이어야 한다");
                }

                if (definition.FloorOf(1) != null && !Mathf.Approximately(FloorGeometry.FloorPlaneY(definition, 1), definition.FloorOf(0).FloorHeight))
                {
                    errors.Add("FloorPlaneY(+1) ≠ FloorHeight(0) — 위층 오프셋 식 결함");
                }

                if (definition.FloorOf(-1) != null && !Mathf.Approximately(FloorGeometry.FloorPlaneY(definition, -1), -definition.FloorOf(-1).FloorHeight))
                {
                    errors.Add("FloorPlaneY(-1) ≠ -FloorHeight(-1) — 아래층 오프셋 식 결함(층고는 아래 층 보유)");
                }
            }

            LogAll(errors);
            return errors.Count == 0;
        }

        /// <summary>두 계단실 템플릿의 풋프린트·소켓 배열이 같은지 비교한다(X4 ④ — 샤프트 좌표 복사 전제).</summary>
        /// <param name="a">기준 계단실.</param>
        /// <param name="b">비교 계단실.</param>
        /// <returns>동형이면 true.</returns>
        private static bool SameStairShape(RoomTemplateSO a, RoomTemplateSO b)
        {
            RoomTemplateDef defA = a.ToDef();
            RoomTemplateDef defB = b.ToDef();
            if (defA.WidthCells != defB.WidthCells || defA.HeightCells != defB.HeightCells || defA.Sockets.Length != defB.Sockets.Length)
            {
                return false;
            }

            for (int s = 0; s < defA.Sockets.Length; s++)
            {
                if (defA.Sockets[s].LocalCell.X != defB.Sockets[s].LocalCell.X ||
                    defA.Sockets[s].LocalCell.Y != defB.Sockets[s].LocalCell.Y ||
                    defA.Sockets[s].Direction != defB.Sockets[s].Direction)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>수집된 린트 실패 사유를 에러 로그로 내보낸다.</summary>
        /// <param name="errors">실패 사유 목록.</param>
        private static void LogAll(List<string> errors)
        {
            for (int i = 0; i < errors.Count; i++)
            {
                Log.E($"[MapPlanBuilder] 린트 실패 — {errors[i]}");
            }
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
            Log.D("[MapPlanBuilder] Build");
            var lintErrors = new List<string>();
            if (!Lint(definition, lintErrors))
            {
                flatTemplateAssets = null;
                return null; // 조립 거부(R4) — 사유는 Lint 가 이미 에러 로그로 내보냈다
            }

            var floorParams = new FloorGenParams[definition.Floors.Length];
            var floorSets = new FloorTemplateSet[definition.Floors.Length];
            var flatAssets = new List<RoomTemplateSO>();

            for (int slot = 0; slot < definition.Floors.Length; slot++)
            {
                FloorDefinitionSO floor = definition.Floors[slot];
                int floorIndex = definition.FloorIndexOf(slot);
                string suffix = floorIndex == 0 ? string.Empty : $"@f{floorIndex}";

                var defs = new List<RoomTemplateDef>();
                for (int t = 0; t < floor.Templates.Length; t++)
                {
                    RoomTemplateSO asset = floor.Templates[t];
                    if (asset.IsEntranceAnchor && floorIndex != 0)
                    {
                        continue; // 입구는 시드 층 전용(X4 ②)
                    }

                    RoomTemplateDef def = asset.ToDef();
                    def.TemplateId += suffix;
                    defs.Add(def);
                    flatAssets.Add(asset);
                }

                if (floor.StairTemplate != null && definition.Floors.Length > 1)
                {
                    RoomTemplateDef stair = floor.StairTemplate.ToDef();
                    stair.TemplateId += $"@f{floorIndex}"; // 계단은 전 층 같은 SO 재사용 가능 — 층 접미사로 유일화(시드 층 포함)
                    defs.Add(stair);
                    flatAssets.Add(floor.StairTemplate);
                }

                // 층 파라미터 — 층 정의의 코어 파라미터를 쓰되 서수·테마는 여기서 스탬프(층 SO 는 서수를 모른다 — 재사용 전제)
                FloorGenParams cloned = floor.GenParams ?? new FloorGenParams();
                floorParams[slot] = new FloorGenParams
                {
                    FloorIndex = floorIndex,
                    ThemeId = floor.ThemeId,
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
                    ReturnExitCount = cloned.ReturnExitCount,
                    WardrobeCount = cloned.WardrobeCount,
                };
                floorSets[slot] = new FloorTemplateSet { FloorIndex = floorIndex, ThemeId = floor.ThemeId, Templates = defs.ToArray() };
            }

            flatTemplateAssets = flatAssets.ToArray();
            return MapGenPlan.Compose(snapshot, floorParams, floorSets);
        }
    }
}
