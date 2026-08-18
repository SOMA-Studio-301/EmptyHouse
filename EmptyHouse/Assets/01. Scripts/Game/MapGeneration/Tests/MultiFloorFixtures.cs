using System.Collections.Generic;
using EmptyHouse.MapGen.Runtime;

namespace EmptyHouse.MapGen.Core.Tests
{
    /// <summary>
    /// 다층 테스트 픽스처(M9-5·M9-6 공용) — 실측 카탈로그 기반 3층(B1·1F·2F) Plan.
    /// 시드 층(1F) = 카탈로그 + 계단실, 비시드 층 = ID 접미사 사본(MinCount 0) + 각 층 계단실.
    /// 층 가중은 로드맵 확정(B1 &gt; 2F &gt; 1F)을 반영해 B1 = 2 · 2F = 1 · 1F = 0.
    /// </summary>
    public static class MultiFloorFixtures
    {
        /// <summary>3층 Plan 을 만든다 — 반환 후 Params/FloorParams 를 조정해도 된다(Compose 는 파라미터를 캐시하지 않는다).</summary>
        /// <param name="seed">확정 시드.</param>
        /// <returns>3층 Plan.</returns>
        public static MapGenPlan ThreeFloorPlan(int seed)
        {
            List<RoomTemplateDef> catalog = MapTemplateCatalog.Create();
            var genParams = new MapGenParams { Seed = seed };

            var seedTemplates = new List<RoomTemplateDef>(catalog) { StairTemplate("stair_f0") };

            // 안전지대 상수 강제(Min=Max=4)는 실측 층 예산(28~32방) 전제 — 픽스처의 9~11 예산에서는
            // 단일 소켓 잎 4개 강제가 프런티어 고갈로 리롤 전멸을 만든다(시드 28 실측).
            // 다층 픽스처의 검증 목적은 계단·층 연결이므로 시드 층에서도 선택 배치로 완화한다(비시드 층은 CloneForFloor 가 이미 Min 0).
            foreach (RoomTemplateDef t in seedTemplates)
            {
                if (t.TemplateId == "safezone_3x3")
                {
                    t.MinCount = 0;
                }
            }

            var floors = new[]
            {
                new FloorTemplateSet { FloorIndex = 0, ThemeId = "hall", Templates = seedTemplates.ToArray() },
                new FloorTemplateSet { FloorIndex = 1, ThemeId = "hall", Templates = CloneForFloor(catalog, "_f1", "stair_f1") },
                new FloorTemplateSet { FloorIndex = -1, ThemeId = "hall", Templates = CloneForFloor(catalog, "_b1", "stair_b1") },
            };

            var floorParams = new[]
            {
                // 탈출문은 시드 층 2(비입구 층 기본 0), 벽장은 층당 2(구 전역 6 의 층 배분 — M10-1 이관)
                new FloorGenParams { FloorIndex = 0, ThemeId = "hall", RoomsTotalMin = 9, RoomsTotalMax = 11, CycleRoomPercent = 60, CorridorLinkPercent = 100, CorridorChainMax = 3, DangerBias = 0, ReturnExitCount = 2, WardrobeCount = 2 },
                new FloorGenParams { FloorIndex = 1, ThemeId = "hall", RoomsTotalMin = 6, RoomsTotalMax = 8, CycleRoomPercent = 60, CorridorLinkPercent = 100, CorridorChainMax = 3, DangerBias = 1, WardrobeCount = 2 },
                new FloorGenParams { FloorIndex = -1, ThemeId = "hall", RoomsTotalMin = 6, RoomsTotalMax = 8, CycleRoomPercent = 60, CorridorLinkPercent = 100, CorridorChainMax = 3, DangerBias = 2, WardrobeCount = 2 },
            };

            return MapGenPlan.Compose(genParams, floorParams, floors);
        }

        /// <summary>
        /// 계단실 템플릿 — 6×6(런타임 StairRoom-6x6 규격과 동일 풋프린트·소켓, 2026-08-13).
        /// 서변 소켓 없음 — 계단 스트립이 서쪽 열 (0,1)~(0,3)에 붙고 위층 도착 개구가 (0,3)이라
        /// 서쪽 문은 계단·개구와 충돌한다. 나머지 변은 {1,4} 자기 대칭(c ↔ L−1−c).
        /// </summary>
        /// <param name="id">템플릿 ID.</param>
        /// <returns>계단실 서술자.</returns>
        public static RoomTemplateDef StairTemplate(string id)
        {
            return new RoomTemplateDef
            {
                TemplateId = id,
                WidthCells = 6,
                HeightCells = 6,
                AllowedFloors = FloorMask.F1,
                MinCount = 0,
                MaxCount = 3, // ShaftCountMax 상한 — 층당 최대 3개
                IsStairAnchor = true,
                Sockets = new[]
                {
                    new SocketDef { Id = 0, LocalCell = new CellCoord(1, 0), Direction = SocketDirection.South },
                    new SocketDef { Id = 1, LocalCell = new CellCoord(4, 0), Direction = SocketDirection.South },
                    new SocketDef { Id = 2, LocalCell = new CellCoord(1, 5), Direction = SocketDirection.North },
                    new SocketDef { Id = 3, LocalCell = new CellCoord(4, 5), Direction = SocketDirection.North },
                    new SocketDef { Id = 4, LocalCell = new CellCoord(5, 1), Direction = SocketDirection.East },
                    new SocketDef { Id = 5, LocalCell = new CellCoord(5, 4), Direction = SocketDirection.East },
                },
                Markers = new MarkerDef[0],
            };
        }

        /// <summary>비시드 층 템플릿 세트 — 입구 제외 카탈로그 사본(ID 접미사·MinCount 0) + 그 층 계단실.</summary>
        /// <param name="catalog">원천 카탈로그.</param>
        /// <param name="suffix">TemplateId 접미사(전 층 유일 제약).</param>
        /// <param name="stairId">계단실 ID.</param>
        /// <returns>층 템플릿 배열.</returns>
        public static RoomTemplateDef[] CloneForFloor(List<RoomTemplateDef> catalog, string suffix, string stairId)
        {
            var result = new List<RoomTemplateDef>();
            for (int t = 0; t < catalog.Count; t++)
            {
                if (catalog[t].IsEntranceAnchor)
                {
                    continue; // 입구는 시드 층 전용(X4 ②)
                }

                result.Add(new RoomTemplateDef
                {
                    TemplateId = catalog[t].TemplateId + suffix,
                    WidthCells = catalog[t].WidthCells,
                    HeightCells = catalog[t].HeightCells,
                    AllowedFloors = catalog[t].AllowedFloors,
                    Tags = catalog[t].Tags,
                    MinCount = 0,
                    MaxCount = catalog[t].MaxCount,
                    IsCorridor = catalog[t].IsCorridor,
                    Sockets = catalog[t].Sockets,
                    Markers = catalog[t].Markers,
                });
            }

            result.Add(StairTemplate(stairId));
            return result.ToArray();
        }
    }
}
