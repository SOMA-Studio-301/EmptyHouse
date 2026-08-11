using System.Collections.Generic;
using Border.Core;

namespace EmptyHouse.MapGen.Core
{
    /// <summary>
    /// 생성 파이프라인 파사드(1절): 레이아웃(3절) → 열쇠·자물쇠(4절) → 스폰(5절) → 검증(7절) → 실패 시 리롤.
    /// 난수는 시드 하나로 리시드한 단일 스트림만 소비한다(8절 결정론) — 리롤도 같은 스트림을 이어 쓴다.
    /// 서버 전용 호출·시드 복제·상태 오브젝트 스폰은 어댑터 소관(8절) — 코어는 엔진과 네트워크를 모른다.
    /// 입력은 <see cref="MapGenPlan"/> 하나(M9-4) — 구 (파라미터, 템플릿) 시그니처는 층 1개 Plan 합성 위임으로 존치.
    /// </summary>
    public sealed class MapGenerator
    {
        public const string GeneratorVersion = "0.13.0"; // 생성기 버전 — MapBlueprintMeta에 스냅샷(1절). 0.13.0: 계단 샤프트(SSA — 시드 층 강제 삽입·전 층 좌표 복사·수직 간선, M9-5). 층 1개 블루프린트 결과는 0.11.0 과 동일(골든 증명)

        private readonly DeterministicRng rng = new DeterministicRng(); // 단일 난수 스트림(8절)
        private readonly LayoutGenerator layoutGenerator = new LayoutGenerator(); // 3절
        private readonly LockKeyPlacer lockKeyPlacer = new LockKeyPlacer(); // 4절
        private readonly SpawnDistributor spawnDistributor = new SpawnDistributor(); // 5절
        private readonly BlueprintValidator validator = new BlueprintValidator(); // 7절

        /// <summary>
        /// v1 호환 진입점 — 층 1개 Plan 을 합성해 위임한다. 결과는 v1 과 완전 동일해야 한다(골든 게이트).
        /// </summary>
        /// <param name="genParams">생성 파라미터(9절). Seed 는 0이 아닌 확정 값이어야 한다(X8).</param>
        /// <param name="templates">사용 가능한 방/복도 템플릿 서술자 집합.</param>
        /// <returns>성공 여부·블루프린트·리롤 횟수·실패 사유를 담은 결과.</returns>
        public MapGenResult Generate(MapGenParams genParams, IReadOnlyList<RoomTemplateDef> templates)
        {
            return Generate(MapGenPlan.FromLegacy(genParams, templates));
        }

        /// <summary>
        /// 계획(전역 파라미터 + 층별 파라미터·템플릿)으로 MapBlueprint 를 생성한다(M9-4).
        /// 검증 실패 시 RerollMax 까지 리롤(X1·X3), 초과 시 실패 결과를 반환한다(X2 — 폴백 맵 없음, 예외 없음).
        /// </summary>
        /// <param name="plan">생성 계획.</param>
        /// <returns>성공 여부·블루프린트·리롤 횟수·실패 사유를 담은 결과.</returns>
        public MapGenResult Generate(MapGenPlan plan)
        {
            MapGenParams genParams = plan.Params;
            var result = new MapGenResult();
            var inputErrors = new List<string>();
            if (!ValidateInputs(plan, inputErrors))
            {
                result.FailReasons.AddRange(inputErrors);
                return result;
            }

            // 리롤도 리시드 없이 같은 스트림을 이어 쓴다(8절·X3) — 시도 = 레이아웃+열쇠+스폰+검증 한 묶음
            rng.Reseed(genParams.Seed);
            for (int attempt = 0; attempt <= genParams.RerollMax; attempt++)
            {
                result.RerollCount = attempt;
                var blueprint = new MapBlueprint();
                blueprint.Meta.Seed = genParams.Seed;
                blueprint.Meta.GeneratorVersion = GeneratorVersion;
                blueprint.Meta.ParamsSnapshot = genParams;

                if (!layoutGenerator.TryGenerate(rng, plan, blueprint))
                {
                    result.FailReasons.Add($"시도 {attempt + 1}: 레이아웃 생성 실패");
                    continue;
                }

                int[] hops = DangerGradeCalculator.ComputeHopDistances(blueprint);
                int[] grades = DangerGradeCalculator.ComputeDangerGrades(blueprint, hops, plan); // DangerBias 0 이면 hops 와 동일(v1 하위호환)

                // 탈출문 전환은 자물쇠·스폰보다 먼저 — 봉인 간선을 바꾸는 작업이라 이후 단계가 최종 그래프를 본다
                ReturnExitPlacer.Place(genParams, blueprint, plan.FlatTemplates, grades);

                if (!lockKeyPlacer.TryPlace(rng, genParams, blueprint, grades, plan.FlatTemplates))
                {
                    result.FailReasons.Add($"시도 {attempt + 1}: 열쇠·자물쇠 배치 실패");
                    continue;
                }

                if (!spawnDistributor.TryDistribute(rng, genParams, blueprint, grades, plan.FlatTemplates))
                {
                    result.FailReasons.Add($"시도 {attempt + 1}: 스폰 분배 실패");
                    continue;
                }

                ValidationReport report = validator.Validate(blueprint, genParams, plan.FlatTemplates);
                result.LastReport = report;
                if (report.AllPassed)
                {
                    result.Success = true;
                    result.Blueprint = blueprint;
                    return result;
                }

                for (int f = 0; f < report.FailReasons.Count; f++)
                {
                    result.FailReasons.Add($"시도 {attempt + 1}: {report.FailReasons[f]}");
                }
            }

            // X2 — 폴백 맵 없음, 예외 없음. 실패 사유·소비 리롤 횟수를 그대로 보고한다(AC-18)
            return result;
        }

        /// <summary>v1 호환 사전 검증 — 층 1개 Plan 을 합성해 위임한다.</summary>
        /// <param name="genParams">검사할 파라미터.</param>
        /// <param name="templates">검사할 템플릿 집합.</param>
        /// <param name="errors">발견한 모순 사유 수집 목록.</param>
        /// <returns>모순이 없으면 true.</returns>
        public bool ValidateInputs(MapGenParams genParams, IReadOnlyList<RoomTemplateDef> templates, List<string> errors)
        {
            return ValidateInputs(MapGenPlan.FromLegacy(genParams, templates), errors);
        }

        /// <summary>
        /// 생성 시도 전에 계획의 모순을 검사한다(X4) — 리롤로 낭비하지 않기 위한 사전 밸리데이션.
        /// 전역 항목은 한 번, 예산·템플릿 구성은 층별로 검사한다(M9-4 층 분해).
        /// </summary>
        /// <param name="plan">검사할 계획.</param>
        /// <param name="errors">발견한 모순 사유 수집 목록.</param>
        /// <returns>모순이 없으면 true.</returns>
        public bool ValidateInputs(MapGenPlan plan, List<string> errors)
        {
            MapGenParams genParams = plan.Params;
            if (genParams.Seed == 0)
            {
                errors.Add("X4: 시드 0 — 코어 진입 전에 서버가 실제 값으로 확정해야 한다(X8)");
            }

            if (genParams.ReturnExitCount < 1)
            {
                errors.Add($"X4: 탈출문 수({genParams.ReturnExitCount}) < 1 — 탈출 경로가 없으면 세션이 끝나지 않는다");
            }

            if (genParams.WardrobeCount < 0)
            {
                errors.Add($"X4: 벽장 수({genParams.WardrobeCount}) < 0");
            }

            if (genParams.OilCount < 1)
            {
                errors.Add($"X4: OilCount({genParams.OilCount}) < 1 — 기름은 필수(7절 패스1)라 배치 0이면 검증이 항상 실패");
            }

            if (plan.FloorParams == null || plan.Floors == null || plan.FloorParams.Length != plan.Floors.Length || plan.Floors.Length == 0)
            {
                errors.Add("X4: 층 구성 불일치 — FloorParams·Floors 는 같은 길이(≥1)여야 한다");
                return false; // 이하 층별 검사가 성립하지 않는다
            }

            if (plan.SeedFloorSlot < 0 || plan.SeedFloorSlot >= plan.Floors.Length)
            {
                errors.Add("X4: 입구 앵커 템플릿 부재(3절 — 레이아웃 시작 불가)");
            }

            bool hasVaccineMarker = false;
            bool hasKeyMarker = false;
            bool hasFuelMarker = false;
            var seenFloorIndices = new List<int>();
            var seenTemplateIds = new List<string>();
            for (int f = 0; f < plan.Floors.Length; f++)
            {
                FloorGenParams floorParams = plan.FloorParams[f];
                FloorTemplateSet floorSet = plan.Floors[f];
                string floorTag = plan.Floors.Length == 1 ? string.Empty : $"[층 {floorSet.FloorIndex}] ";

                if (floorParams.FloorIndex != floorSet.FloorIndex)
                {
                    errors.Add($"X4: 슬롯 {f} 층 서수 불일치 — 파라미터 {floorParams.FloorIndex} vs 템플릿 세트 {floorSet.FloorIndex}");
                }

                if (seenFloorIndices.Contains(floorSet.FloorIndex))
                {
                    errors.Add($"X4: 층 서수 {floorSet.FloorIndex} 중복 — 층 서수는 유일해야 한다");
                }

                seenFloorIndices.Add(floorSet.FloorIndex);

                if (floorParams.RoomsTotalMin < 2 || floorParams.RoomsTotalMin > floorParams.RoomsTotalMax)
                {
                    errors.Add($"X4: {floorTag}총 방 수 범위 모순(Min {floorParams.RoomsTotalMin} · Max {floorParams.RoomsTotalMax})");
                }

                if (floorParams.CycleRoomPercent < 0 || floorParams.CycleRoomPercent > 100)
                {
                    errors.Add($"X4: {floorTag}사이클 소속 방 목표 비율({floorParams.CycleRoomPercent}%)이 0~100 밖");
                }

                if (floorParams.CorridorChainMax < 1)
                {
                    errors.Add($"X4: {floorTag}복도 연쇄 최대({floorParams.CorridorChainMax}) < 1 — 최소 1(연쇄 없음)이어야 한다");
                }

                int minCountSum = 0;
                int maxCountSum = 0;
                int anchorTemplates = 0;
                for (int t = 0; t < floorSet.Templates.Length; t++)
                {
                    RoomTemplateDef template = floorSet.Templates[t];
                    if (seenTemplateIds.Contains(template.TemplateId))
                    {
                        errors.Add($"X4: 템플릿 ID {template.TemplateId} 가 전 층 통틀어 중복 — TemplateId 는 유일해야 한다(방↔템플릿 역참조 키)");
                    }

                    seenTemplateIds.Add(template.TemplateId);
                    anchorTemplates += template.IsEntranceAnchor ? 1 : 0;
                    if (!template.IsCorridor && !template.IsEntranceAnchor)
                    {
                        // 층 방 수 예산은 방 전용 집계 — 복도·입구 앵커는 예산 밖(각자 MaxCount 로만 제한)
                        minCountSum += template.MinCount;
                        maxCountSum += template.MaxCount;
                    }

                    if (template.MinCount > template.MaxCount)
                    {
                        errors.Add($"X4: 템플릿 {template.TemplateId} MinCount({template.MinCount}) > MaxCount({template.MaxCount}) — 자기모순(MinCount 충족 불가)");
                    }

                    if (template.IsCorridor)
                    {
                        // 복도 배치는 복도 경유 확률에만 의존(강제 경로 없음) — 확률 0 + MinCount>0 은 리롤만 소진하는 자기모순
                        if (template.MinCount > 0 && floorParams.CorridorLinkPercent <= 0)
                        {
                            errors.Add($"X4: 복도 {template.TemplateId} MinCount({template.MinCount}) > 0 인데 복도 경유 확률 0% — 복도 배치 경로가 없어 충족 불가");
                        }

                        // 원자 배치(근단+원단)는 개구 2변(직선·코너)까지만 막다른 끝 0 을 보장한다 — 3변 이상(T자)은 v1 미지원
                        var openingDirs = new HashSet<SocketDirection>();
                        for (int s = 0; s < template.Sockets.Length; s++)
                        {
                            openingDirs.Add(template.Sockets[s].Direction);
                        }

                        if (openingDirs.Count > 2)
                        {
                            errors.Add($"X4: 복도 {template.TemplateId} 개구 변 {openingDirs.Count}개 — v1 은 2변(직선·코너)까지만 지원(막다른 끝 0 보장 불가)");
                        }
                    }

                    // 의무 문 소켓은 입구 앵커 전용 — 다른 방의 것은 연결 보장 경로가 없어 조용히 무시되므로 사전 차단
                    for (int s = 0; s < template.Sockets.Length; s++)
                    {
                        if (template.Sockets[s].MandatoryDoor && !template.IsEntranceAnchor)
                        {
                            errors.Add($"X4: 템플릿 {template.TemplateId} 소켓 {template.Sockets[s].Id} 이 의무 문 — v1 은 입구 앵커 전용");
                        }
                    }

                    for (int m = 0; m < template.Markers.Length; m++)
                    {
                        if (template.Markers[m].Kind != MarkerKind.ItemSpawn)
                        {
                            continue;
                        }

                        ItemCategoryMask mask = template.Markers[m].ItemMask;
                        hasVaccineMarker |= (mask & ItemCategoryMask.Vaccine) != 0;
                        hasKeyMarker |= (mask & ItemCategoryMask.Key) != 0;
                        hasFuelMarker |= (mask & ItemCategoryMask.Fuel) != 0;
                    }
                }

                // 입구 앵커는 시드 층에만 — 비시드 층의 앵커는 배치 경로가 없어 조용히 죽는 데이터다
                if (f == plan.SeedFloorSlot && anchorTemplates == 0)
                {
                    errors.Add("X4: 입구 앵커 템플릿 부재(3절 — 레이아웃 시작 불가)");
                }
                else if (f != plan.SeedFloorSlot && anchorTemplates > 0)
                {
                    errors.Add($"X4: {floorTag}입구 앵커 템플릿 {anchorTemplates}개 — 입구는 시드 층 전용(M9)");
                }

                if (minCountSum > floorParams.RoomsTotalMax)
                {
                    errors.Add($"X4: {floorTag}방 템플릿 MinCount 합({minCountSum})이 총 방 수 상한({floorParams.RoomsTotalMax}) 초과");
                }

                if (maxCountSum < floorParams.RoomsTotalMin)
                {
                    errors.Add($"X4: {floorTag}방 템플릿 MaxCount 합({maxCountSum})이 총 방 수 하한({floorParams.RoomsTotalMin}) 미만 — 레이아웃이 성립 불가(복도·입구는 집계 제외)");
                }
            }

            // 다층 전용 검사(M9-5) — 층 ≥2 면 전 층에 계단실 정확히 1종 + 풋프린트·소켓 배열 전 층 동일(X4 ③④)
            if (plan.Floors.Length > 1)
            {
                RoomTemplateDef referenceStair = null;
                for (int f = 0; f < plan.Floors.Length; f++)
                {
                    RoomTemplateDef stair = null;
                    int stairCount = 0;
                    for (int t = 0; t < plan.Floors[f].Templates.Length; t++)
                    {
                        if (plan.Floors[f].Templates[t].IsStairAnchor)
                        {
                            stair = plan.Floors[f].Templates[t];
                            stairCount++;
                        }
                    }

                    if (stairCount != 1)
                    {
                        errors.Add($"X4: 층 {plan.Floors[f].FloorIndex} 계단실 템플릿 {stairCount}종 — 다층은 층마다 정확히 1종이어야 한다(Q5: 테마별 1개)");
                        continue;
                    }

                    if (referenceStair == null)
                    {
                        referenceStair = stair;
                        continue;
                    }

                    bool sameShape = stair.WidthCells == referenceStair.WidthCells && stair.HeightCells == referenceStair.HeightCells
                        && stair.Sockets.Length == referenceStair.Sockets.Length;
                    for (int s = 0; sameShape && s < stair.Sockets.Length; s++)
                    {
                        sameShape = stair.Sockets[s].Id == referenceStair.Sockets[s].Id
                            && stair.Sockets[s].LocalCell.X == referenceStair.Sockets[s].LocalCell.X
                            && stair.Sockets[s].LocalCell.Y == referenceStair.Sockets[s].LocalCell.Y
                            && stair.Sockets[s].Direction == referenceStair.Sockets[s].Direction;
                    }

                    if (!sameShape)
                    {
                        errors.Add($"X4: 층 {plan.Floors[f].FloorIndex} 계단실 {stair.TemplateId} 의 풋프린트·소켓 배열이 기준({referenceStair.TemplateId})과 다르다 — 샤프트 좌표 복사가 성립하지 않는다(SSA)");
                    }
                }
            }

            // 층 배정 마커 존재(X4 ⑦) — 배정 층이 실재하고 그 층 세트에 해당 마커가 있어야 한다
            ValidateFloorPlanMarkers(plan, genParams.VaccineFloorPlan, "백신", MarkerKind.ItemSpawn, ItemCategoryMask.Vaccine, errors);
            ValidateFloorPlanMarkers(plan, genParams.CorpseStationFloorPlan, "사체 충전소", MarkerKind.CorpseStationSlot, ItemCategoryMask.None, errors);

            if (!hasVaccineMarker)
            {
                errors.Add("X4: Vaccine 허용 ItemSpawn 마커 부재 — 백신 배치 불가");
            }

            if (!hasKeyMarker)
            {
                errors.Add("X4: Key 허용 ItemSpawn 마커 부재 — 열쇠 배치 불가");
            }

            if (!hasFuelMarker)
            {
                errors.Add("X4: Fuel 허용 ItemSpawn 마커 부재 — 연료 배치 불가");
            }

            return errors.Count == 0;
        }

        /// <summary>
        /// 층 배정 목록(X4 ⑦) 검사 — 지목한 층 서수가 실재하고, 그 층 템플릿 세트에 요구 마커가 있어야 한다.
        /// 배정이 비어 있으면(널·길이 0) 층 무관 분산이라 검사하지 않는다.
        /// </summary>
        /// <param name="plan">검사할 계획.</param>
        /// <param name="floorPlan">배정 층 서수 목록.</param>
        /// <param name="label">오류 문구용 이름.</param>
        /// <param name="markerKind">요구 마커 종류.</param>
        /// <param name="itemMask">ItemSpawn 이면 요구 카테고리(그 외 None).</param>
        /// <param name="errors">모순 사유 수집 목록.</param>
        private static void ValidateFloorPlanMarkers(MapGenPlan plan, int[] floorPlan, string label, MarkerKind markerKind, ItemCategoryMask itemMask, List<string> errors)
        {
            if (floorPlan == null || floorPlan.Length == 0)
            {
                return;
            }

            for (int i = 0; i < floorPlan.Length; i++)
            {
                int slot = plan.SlotOfFloor(floorPlan[i]);
                if (slot < 0)
                {
                    errors.Add($"X4: {label} 배정 층 {floorPlan[i]} 이 계획에 없다");
                    continue;
                }

                bool hasMarker = false;
                RoomTemplateDef[] templates = plan.Floors[slot].Templates;
                for (int t = 0; t < templates.Length && !hasMarker; t++)
                {
                    for (int m = 0; m < templates[t].Markers.Length && !hasMarker; m++)
                    {
                        MarkerDef marker = templates[t].Markers[m];
                        hasMarker = marker.Kind == markerKind && (itemMask == ItemCategoryMask.None || (marker.ItemMask & itemMask) != 0);
                    }
                }

                if (!hasMarker)
                {
                    errors.Add($"X4: {label} 배정 층 {floorPlan[i]} 의 템플릿 세트에 요구 마커({markerKind})가 없다 — 배정 충족 불가");
                }
            }
        }
    }
}
