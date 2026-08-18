using System.Collections.Generic;

namespace EmptyHouse.MapGen.Core
{
    /// <summary>
    /// 생성 입력 묶음(M9) — <see cref="MapGenerator.Generate(MapGenPlan)"/> 의 단일 입력.
    /// 전역 파라미터 + 층별 파라미터 + 층별 템플릿 세트를 한 덩어리로 들고 다닌다.
    ///
    /// **코어는 맵도 테마도 모른다** — 어느 맵(스테이지)에서 이 Plan 이 나왔는지는 어댑터 소관이고,
    /// 코어는 Plan 만 본다. 이 경계가 다중 맵(M10)이 코어에 침투하는 것을 막는다.
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
        /// 방·층 루프 안에서 반복 호출되는 순수 계산이라 로그 트레이스를 두지 않는다(CellMath 규약).
        /// </summary>
        /// <param name="floorSlot">층 슬롯 인덱스(<see cref="Floors"/> 배열 인덱스).</param>
        /// <returns>구간 시작 인덱스.</returns>
        public int TemplateStart(int floorSlot)
        {
            int start = 0;
            for (int i = 0; i < floorSlot; i++)
            {
                start += Floors[i].Templates.Length;
            }

            return start;
        }

        /// <summary>층 슬롯의 평탄화 테이블 구간 길이를 반환한다.</summary>
        /// <param name="floorSlot">층 슬롯 인덱스.</param>
        /// <returns>구간 길이.</returns>
        public int TemplateCount(int floorSlot)
        {
            return Floors[floorSlot].Templates.Length;
        }

        /// <summary>층 서수(부호)로 층 슬롯 인덱스를 찾는다. 없으면 -1.</summary>
        /// <param name="floorIndex">층 서수(B1 = -1 · 1F = 0 · 2F = +1).</param>
        /// <returns>층 슬롯 인덱스, 없으면 -1.</returns>
        public int SlotOfFloor(int floorIndex)
        {
            for (int i = 0; i < Floors.Length; i++)
            {
                if (Floors[i].FloorIndex == floorIndex)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>층 서수의 위험 층 가중(DangerBias)을 찾는다 — 미등재 층은 0(가중 없음).</summary>
        /// <param name="floorIndex">층 서수.</param>
        /// <returns>층 가중값.</returns>
        public int DangerBiasOf(int floorIndex)
        {
            for (int i = 0; i < FloorParams.Length; i++)
            {
                if (FloorParams[i].FloorIndex == floorIndex)
                {
                    return FloorParams[i].DangerBias;
                }
            }

            return 0;
        }

        /// <summary>
        /// v1 호환 경로 — 층 개념이 없는 (파라미터, 템플릿) 쌍에서 **층 1개짜리** Plan 을 합성한다(테스트·레거시 툴 전용,
        /// 런타임·프리뷰는 MapPlanBuilder 단일 경로 — M10-1). 층 1개 구성은 현행 v1 과 완전히 같은 블루프린트를 내야 한다(골든 회귀 게이트).
        /// </summary>
        /// <param name="genParams">전역 파라미터(층별 항목은 스칼라 폴백 값을 쓴다).</param>
        /// <param name="templates">단일 템플릿 세트.</param>
        /// <returns>층 1개 Plan.</returns>
        public static MapGenPlan FromLegacy(MapGenParams genParams, IReadOnlyList<RoomTemplateDef> templates)
        {
            var floorParams = new FloorGenParams
            {
                FloorIndex = 0,
                ThemeId = null,
                RoomsTotalMin = genParams.RoomsTotalMin,
                RoomsTotalMax = genParams.RoomsTotalMax,
                CycleRoomPercent = genParams.CycleRoomPercent,
                CorridorLinkPercent = genParams.CorridorLinkPercent,
                CorridorChainMax = genParams.CorridorChainMax,
                DangerBias = 0,
                ZombieDensitySafeMin = genParams.ZombieDensitySafeMin,
                ZombieDensitySafeMax = genParams.ZombieDensitySafeMax,
                ZombieDensityMidMin = genParams.ZombieDensityMidMin,
                ZombieDensityMidMax = genParams.ZombieDensityMidMax,
                ZombieDensityDangerMin = genParams.ZombieDensityDangerMin,
                ZombieDensityDangerMax = genParams.ZombieDensityDangerMax,
                ListenerRatioPercent = genParams.ListenerRatioPercent,
                HerdSpawnBands = genParams.HerdSpawnBands,
                HerdZombieCountMin = genParams.HerdZombieCountMin,
                HerdZombieCountMax = genParams.HerdZombieCountMax,
                EnabledZombieTypes = genParams.EnabledZombieTypes,
                ReturnExitCount = genParams.ReturnExitCount, // v1 전역값을 시드 층에 매핑(M10-1 이관 — 골든 불변의 축)
                WardrobeCount = genParams.WardrobeCount,
            };

            var templateArray = new RoomTemplateDef[templates.Count];
            for (int i = 0; i < templates.Count; i++)
            {
                templateArray[i] = templates[i];
            }

            return Compose(genParams, new[] { floorParams }, new[]
            {
                new FloorTemplateSet { FloorIndex = 0, ThemeId = null, Templates = templateArray },
            });
        }

        /// <summary>
        /// 층별 재료에서 Plan 을 조립한다 — 평탄화 테이블과 시드 층 슬롯(입구 앵커 보유 층)을 계산한다.
        /// 입구 앵커가 없으면 SeedFloorSlot = -1 로 남는다(X4 사전 검증이 잡는다 — 여기서 throw 하지 않는다).
        /// </summary>
        /// <param name="genParams">전역 파라미터.</param>
        /// <param name="floorParams">층별 파라미터(슬롯 순서).</param>
        /// <param name="floors">층별 템플릿 세트(같은 슬롯 순서).</param>
        /// <returns>조립된 Plan.</returns>
        public static MapGenPlan Compose(MapGenParams genParams, FloorGenParams[] floorParams, FloorTemplateSet[] floors)
        {
            var flat = new List<RoomTemplateDef>();
            int seedSlot = -1;
            for (int i = 0; i < floors.Length; i++)
            {
                for (int t = 0; t < floors[i].Templates.Length; t++)
                {
                    flat.Add(floors[i].Templates[t]);
                    if (seedSlot < 0 && floors[i].Templates[t].IsEntranceAnchor)
                    {
                        seedSlot = i;
                    }
                }
            }

            return new MapGenPlan
            {
                Params = genParams,
                FloorParams = floorParams,
                Floors = floors,
                FlatTemplates = flat,
                SeedFloorSlot = seedSlot,
            };
        }
    }
}
