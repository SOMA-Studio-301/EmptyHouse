using System.Collections.Generic;
using Border.Core;

namespace EmptyHouse.MapGen.Core
{
    /// <summary>
    /// 좀비·아이템 스폰 분배(5절) — 위험 등급 예산 분배 + 마커 채우기.
    /// 배치 가능 위치는 마커가 보장하므로(2절) 이 클래스는 마커 후보 중 선택만 한다.
    /// </summary>
    public sealed class SpawnDistributor
    {
        /// <summary>
        /// 좀비·아이템·설비 스폰을 분배해 blueprint 의 Spawns 를 채운다(열쇠는 LockKeyPlacer 소관).
        /// </summary>
        /// <param name="rng">단일 난수 스트림.</param>
        /// <param name="genParams">생성 파라미터.</param>
        /// <param name="blueprint">레이아웃·열쇠 배치가 끝난 블루프린트.</param>
        /// <param name="dangerDepths">방별 위험 깊이.</param>
        /// <param name="templates">템플릿 집합(마커 조회용).</param>
        /// <returns>분배 성공 여부 — 실패 시 호출자가 리롤.</returns>
        public bool TryDistribute(DeterministicRng rng, MapGenParams genParams, MapBlueprint blueprint, int[] dangerDepths, IReadOnlyList<RoomTemplateDef> templates)
        {
            // TODO(impl): DistributeZombies → DistributeItems → DistributeFacilities
            Log.D("[SpawnDistributor] TryDistribute");
            return default;
        }

        /// <summary>
        /// 좀비 예산을 위험 등급별 밀도로 분배한다(5절). 타입 규칙 — Watcher: 어두움 태그 방 +
        /// 길목 GeneratorSlot 세트 / Listener: 관문 앞 투척물 보장(6절 불변식과 연동) / 나머지 Walker.
        /// HerdArea 에는 Walker 단독 무리(Listener 미포함).
        /// </summary>
        /// <param name="rng">단일 난수 스트림.</param>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="dangerDepths">방별 위험 깊이.</param>
        /// <param name="templates">템플릿 집합.</param>
        private void DistributeZombies(DeterministicRng rng, MapBlueprint blueprint, int[] dangerDepths, IReadOnlyList<RoomTemplateDef> templates)
        {
            // TODO(impl):
            Log.D("[SpawnDistributor] DistributeZombies");
        }

        /// <summary>
        /// 아이템을 분배한다(5절) — 백신 3종은 서로 다른 고위험 가지(서브트리)에 분산(AC-12),
        /// 투척물은 Listener 보장분 + 회피 예산분(D4 — 외출마다 재배치), 기름은 깊은 구역 집중, 스크랩은 깊이 비례.
        /// </summary>
        /// <param name="rng">단일 난수 스트림.</param>
        /// <param name="genParams">생성 파라미터(Listener 보장 거리).</param>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="dangerDepths">방별 위험 깊이.</param>
        /// <param name="templates">템플릿 집합.</param>
        private void DistributeItems(DeterministicRng rng, MapGenParams genParams, MapBlueprint blueprint, int[] dangerDepths, IReadOnlyList<RoomTemplateDef> templates)
        {
            // TODO(impl):
            Log.D("[SpawnDistributor] DistributeItems");
        }

        /// <summary>
        /// 설비를 분배한다(5절) — 사체 충전소는 CorpseStationSlot 에서 선정(최소 개수는 HerdArea 파훼 쌍이 강제),
        /// 발전기는 Watcher 어둠 구간 길목의 GeneratorSlot(6절 B등급).
        /// </summary>
        /// <param name="rng">단일 난수 스트림.</param>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="dangerDepths">방별 위험 깊이.</param>
        /// <param name="templates">템플릿 집합.</param>
        private void DistributeFacilities(DeterministicRng rng, MapBlueprint blueprint, int[] dangerDepths, IReadOnlyList<RoomTemplateDef> templates)
        {
            // TODO(impl):
            Log.D("[SpawnDistributor] DistributeFacilities");
        }
    }
}
