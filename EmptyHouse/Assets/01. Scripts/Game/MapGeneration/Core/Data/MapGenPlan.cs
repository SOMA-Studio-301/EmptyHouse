using System.Collections.Generic;
using Border.Core;

namespace EmptyHouse.MapGen.Core
{
    /// <summary>
    /// 생성 입력 묶음(M8) — <see cref="MapGenerator.Generate"/> 의 단일 입력.
    /// 전역 파라미터 + 층별 파라미터 + 층별 템플릿 세트를 한 덩어리로 들고 다닌다.
    ///
    /// **코어는 맵도 테마도 모른다** — 어느 맵(스테이지)에서 이 Plan 이 나왔는지는 어댑터 소관이고,
    /// 코어는 Plan 만 본다. 이 경계가 다중 맵(M9)이 코어에 침투하는 것을 막는다.
    ///
    /// 템플릿은 층별 배열(<see cref="FloorTemplateSet"/>)로 격리하되, 조회 성능과
    /// <see cref="BlueprintRoom.TemplateIndex"/> 안정성을 위해 층 순서대로 이어붙인
    /// 평탄화 테이블(<see cref="FlatTemplates"/>)을 함께 만든다.
    /// </summary>
    public sealed class MapGenPlan
    {
        public MapGenParams Params; // 전역 파라미터(시드·리롤·자물쇠 수·필수 아이템 수·샤프트 수)
        public FloorGenParams[] FloorParams; // 층별 파라미터 — 배열 순서가 층 순서 결정의 입력(rng 미소비)
        public FloorTemplateSet[] Floors; // 층별 템플릿 세트 — FloorParams 와 같은 순서·같은 길이
        public IReadOnlyList<RoomTemplateDef> FlatTemplates; // 층 순서로 이어붙인 평탄화 템플릿 테이블
        public int SeedFloorSlot; // 입구 앵커를 가진 층의 슬롯 인덱스(= 레이아웃 시작 층)

        /// <summary>
        /// 층 슬롯의 평탄화 테이블 구간 시작 인덱스를 반환한다.
        /// 층 성장 루프가 후보를 자기 층 구간으로 한정할 때 쓴다.
        /// </summary>
        /// <param name="floorSlot">층 슬롯 인덱스(<see cref="Floors"/> 배열 인덱스).</param>
        /// <returns>구간 시작 인덱스.</returns>
        public int TemplateStart(int floorSlot)
        {
            // TODO(impl):
            Log.D($"[MapGenPlan] TemplateStart slot={floorSlot}");
            return default;
        }

        /// <summary>층 슬롯의 평탄화 테이블 구간 길이를 반환한다.</summary>
        /// <param name="floorSlot">층 슬롯 인덱스.</param>
        /// <returns>구간 길이.</returns>
        public int TemplateCount(int floorSlot)
        {
            // TODO(impl):
            Log.D($"[MapGenPlan] TemplateCount slot={floorSlot}");
            return default;
        }

        /// <summary>층 서수(부호)로 층 슬롯 인덱스를 찾는다. 없으면 -1.</summary>
        /// <param name="floorIndex">층 서수(B1 = -1 · 1F = 0 · 2F = +1).</param>
        /// <returns>층 슬롯 인덱스, 없으면 -1.</returns>
        public int SlotOfFloor(int floorIndex)
        {
            // TODO(impl):
            Log.D($"[MapGenPlan] SlotOfFloor floor={floorIndex}");
            return default;
        }

        /// <summary>
        /// v1 호환 경로 — 층 개념이 없는 (파라미터, 템플릿) 쌍에서 **층 1개짜리** Plan 을 합성한다.
        /// 층 1개 구성은 현행 v1 과 완전히 같은 블루프린트를 내야 한다(골든 회귀 게이트).
        /// ⚠️ 호출부의 합성 분기 조건은 반드시 `Floors == null || Floors.Length == 0` 두 가지 모두다 —
        /// `MapGenNetworkDriver.SnapshotParams` 가 `JsonUtility` 왕복이라 **null 배열이 빈 배열로 되살아난다.**
        /// </summary>
        /// <param name="genParams">전역 파라미터(층별 항목은 스칼라 폴백 값을 쓴다).</param>
        /// <param name="templates">단일 템플릿 세트.</param>
        /// <returns>층 1개 Plan.</returns>
        public static MapGenPlan FromLegacy(MapGenParams genParams, IReadOnlyList<RoomTemplateDef> templates)
        {
            // TODO(impl):
            Log.D("[MapGenPlan] FromLegacy");
            return default;
        }
    }
}
