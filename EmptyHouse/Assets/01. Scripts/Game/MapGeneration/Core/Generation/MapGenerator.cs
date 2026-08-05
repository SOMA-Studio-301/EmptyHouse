using System.Collections.Generic;
using Border.Core;

namespace EmptyHouse.MapGen.Core
{
    /// <summary>
    /// 생성 파이프라인 파사드(1절): 레이아웃(3절) → 열쇠·자물쇠(4절) → 스폰(5절) → 검증(7절) → 실패 시 리롤.
    /// 난수는 시드 하나로 리시드한 단일 스트림만 소비한다(8절 결정론) — 리롤도 같은 스트림을 이어 쓴다.
    /// 서버 전용 호출·시드 복제·상태 오브젝트 스폰은 어댑터 소관(8절) — 코어는 엔진과 네트워크를 모른다.
    /// </summary>
    public sealed class MapGenerator
    {
        public const string GeneratorVersion = "0.2.0"; // 생성기 버전 — MapBlueprintMeta에 스냅샷(1절). 0.2.0: 복도 연결자 원자 배치·방 전용 예산(같은 시드 ≠ 0.1.0 결과)

        private readonly DeterministicRng rng = new DeterministicRng(); // 단일 난수 스트림(8절)
        private readonly LayoutGenerator layoutGenerator = new LayoutGenerator(); // 3절
        private readonly LockKeyPlacer lockKeyPlacer = new LockKeyPlacer(); // 4절
        private readonly SpawnDistributor spawnDistributor = new SpawnDistributor(); // 5절
        private readonly BlueprintValidator validator = new BlueprintValidator(); // 7절

        /// <summary>
        /// 시드·파라미터·템플릿 집합으로 MapBlueprint 를 생성한다.
        /// 검증 실패 시 RerollMax 까지 리롤(X1·X3), 초과 시 실패 결과를 반환한다(X2 — 폴백 맵 없음, 예외 없음).
        /// </summary>
        /// <param name="genParams">생성 파라미터(9절). Seed 는 0이 아닌 확정 값이어야 한다(X8).</param>
        /// <param name="templates">사용 가능한 방/복도 템플릿 서술자 집합.</param>
        /// <returns>성공 여부·블루프린트·리롤 횟수·실패 사유를 담은 결과.</returns>
        public MapGenResult Generate(MapGenParams genParams, IReadOnlyList<RoomTemplateDef> templates)
        {
            Log.D($"[MapGenerator] Generate 시드={genParams.Seed}");
            var result = new MapGenResult();
            var inputErrors = new List<string>();
            if (!ValidateInputs(genParams, templates, inputErrors))
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

                if (!layoutGenerator.TryGenerate(rng, genParams, templates, blueprint))
                {
                    result.FailReasons.Add($"시도 {attempt + 1}: 레이아웃 생성 실패");
                    continue;
                }

                int[] depths = DangerGradeCalculator.ComputeDepths(blueprint);
                if (!lockKeyPlacer.TryPlace(rng, genParams, blueprint, depths, templates))
                {
                    result.FailReasons.Add($"시도 {attempt + 1}: 열쇠·자물쇠 배치 실패");
                    continue;
                }

                if (!spawnDistributor.TryDistribute(rng, genParams, blueprint, depths, templates))
                {
                    result.FailReasons.Add($"시도 {attempt + 1}: 스폰 분배 실패");
                    continue;
                }

                ValidationReport report = validator.Validate(blueprint, genParams);
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
            Log.D($"[MapGenerator] 리롤 상한({genParams.RerollMax}) 초과 — 생성 실패(X2)");
            return result;
        }

        /// <summary>
        /// 생성 시도 전에 파라미터·템플릿 구성의 모순을 검사한다(X4) — 리롤로 낭비하지 않기 위한 사전 밸리데이션.
        /// 입구 앵커 부재, MinCount 합이 총 방 수 초과, 필수 요소 수용 불가 등을 걸러낸다.
        /// </summary>
        /// <param name="genParams">검사할 파라미터.</param>
        /// <param name="templates">검사할 템플릿 집합.</param>
        /// <param name="errors">발견한 모순 사유 수집 목록.</param>
        /// <returns>모순이 없으면 true.</returns>
        public bool ValidateInputs(MapGenParams genParams, IReadOnlyList<RoomTemplateDef> templates, List<string> errors)
        {
            Log.D("[MapGenerator] ValidateInputs");
            if (genParams.Seed == 0)
            {
                errors.Add("X4: 시드 0 — 코어 진입 전에 서버가 실제 값으로 확정해야 한다(X8)");
            }

            if (genParams.RoomsTotalMin < 2 || genParams.RoomsTotalMin > genParams.RoomsTotalMax)
            {
                errors.Add($"X4: 총 방 수 범위 모순(Min {genParams.RoomsTotalMin} · Max {genParams.RoomsTotalMax})");
            }

            bool hasAnchor = false;
            bool hasVaccineMarker = false;
            bool hasKeyMarker = false;
            bool hasOilMarker = false;
            int minCountSum = 0;
            int maxCountSum = 0;
            for (int t = 0; t < templates.Count; t++)
            {
                RoomTemplateDef template = templates[t];
                hasAnchor |= template.IsEntranceAnchor;
                if (!template.IsCorridor && !template.IsEntranceAnchor)
                {
                    // 총 방 수 예산은 방 전용 집계 — 복도·입구 앵커는 예산 밖(각자 MaxCount 로만 제한)
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
                    if (template.MinCount > 0 && genParams.CorridorLinkPercent <= 0)
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
                for (int m = 0; m < template.Markers.Length; m++)
                {
                    if (template.Markers[m].Kind != MarkerKind.ItemSpawn)
                    {
                        continue;
                    }

                    ItemCategoryMask mask = template.Markers[m].ItemMask;
                    hasVaccineMarker |= (mask & ItemCategoryMask.Vaccine) != 0;
                    hasKeyMarker |= (mask & ItemCategoryMask.Key) != 0;
                    hasOilMarker |= (mask & ItemCategoryMask.Oil) != 0;
                }
            }

            if (!hasAnchor)
            {
                errors.Add("X4: 입구 앵커 템플릿 부재(3절 — 레이아웃 시작 불가)");
            }

            if (minCountSum > genParams.RoomsTotalMax)
            {
                errors.Add($"X4: 방 템플릿 MinCount 합({minCountSum})이 총 방 수 상한({genParams.RoomsTotalMax}) 초과");
            }

            if (maxCountSum < genParams.RoomsTotalMin)
            {
                errors.Add($"X4: 방 템플릿 MaxCount 합({maxCountSum})이 총 방 수 하한({genParams.RoomsTotalMin}) 미만 — 레이아웃이 성립 불가(복도·입구는 집계 제외)");
            }

            if (!hasVaccineMarker)
            {
                errors.Add("X4: Vaccine 허용 ItemSpawn 마커 부재 — 백신 배치 불가");
            }

            if (!hasKeyMarker)
            {
                errors.Add("X4: Key 허용 ItemSpawn 마커 부재 — 열쇠 배치 불가");
            }

            if (!hasOilMarker)
            {
                errors.Add("X4: Oil 허용 ItemSpawn 마커 부재 — 기름 배치 불가");
            }

            if (genParams.OilCount < 1)
            {
                errors.Add($"X4: OilCount({genParams.OilCount}) < 1 — 기름은 필수(7절 패스1)라 배치 0이면 검증이 항상 실패");
            }

            return errors.Count == 0;
        }
    }
}
