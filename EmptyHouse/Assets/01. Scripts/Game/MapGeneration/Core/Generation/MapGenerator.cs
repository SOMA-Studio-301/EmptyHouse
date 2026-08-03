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
        public const string GeneratorVersion = "0.1.0"; // 생성기 버전 — MapBlueprintMeta에 스냅샷(1절)

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
            // TODO(impl): ValidateInputs(X4) → rng.Reseed(Seed) → [레이아웃 → 위험 깊이 → 자물쇠·열쇠 → 스폰 → 검증] 리롤 루프 → 결과 조립
            Log.D($"[MapGenerator] Generate 시드={genParams.Seed}");
            return default;
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
            // TODO(impl):
            Log.D("[MapGenerator] ValidateInputs");
            return default;
        }
    }
}
