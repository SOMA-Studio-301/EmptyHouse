using System.Collections.Generic;
using Border.Core;

namespace EmptyHouse.MapGen.Core.Tests
{
    /// <summary>
    /// 테스트 공용 픽스처 — 손으로 짠 미니 그래프와 가짜 템플릿 세트.
    /// M1(그래프 유틸)의 기대값 검증 재료이자 M2~M5 생성기 구동 재료. 구현은 M1 세션 담당.
    /// </summary>
    public static class BlueprintFixtures
    {
        /// <summary>
        /// 손으로 짠 미니 블루프린트 — 방 6(0 = 입구), 트리 간선 5 + 루프 간선 1,
        /// 자물쇠 2(1번 = 루프 지름길, 2번 = 깊은 가지 관문), 봉인 간선(RoomB = -1) 1개 포함.
        /// 기대값(R_1, R_2, 깊이)이 수기로 계산 가능한 최소 크기를 유지할 것.
        /// </summary>
        /// <returns>기대값 검증용 블루프린트.</returns>
        public static MapBlueprint CreateMiniBlueprint()
        {
            // TODO(impl):
            Log.D("[BlueprintFixtures] CreateMiniBlueprint");
            return default;
        }

        /// <summary>
        /// 생성기 구동용 가짜 템플릿 세트 — 입구 앵커 1종 + 방 3~4종(크기·소켓 수 다양) + 복도 1종.
        /// 마커는 ZombieSpawn·ItemSpawn·GeneratorSlot·CorpseStationSlot·HerdArea 전 종류를 최소 1개씩 포함할 것.
        /// </summary>
        /// <returns>M2~M5 property 테스트용 템플릿 목록.</returns>
        public static IReadOnlyList<RoomTemplateDef> CreateFakeTemplates()
        {
            // TODO(impl):
            Log.D("[BlueprintFixtures] CreateFakeTemplates");
            return default;
        }

        /// <summary>
        /// 테스트 스케일 파라미터 — 총 방 수 축소(예: 10~12) 운용, 나머지는 9절 기본값.
        /// </summary>
        /// <param name="seed">확정 시드(0 금지 — X8).</param>
        /// <returns>테스트용 생성 파라미터.</returns>
        public static MapGenParams CreateTestParams(int seed)
        {
            // TODO(impl):
            Log.D($"[BlueprintFixtures] CreateTestParams 시드={seed}");
            return default;
        }
    }
}
