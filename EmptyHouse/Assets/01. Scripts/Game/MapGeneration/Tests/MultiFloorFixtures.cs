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
            var floors = new[]
            {
                new FloorTemplateSet { FloorIndex = 0, ThemeId = "hall", Templates = seedTemplates.ToArray() },
                new FloorTemplateSet { FloorIndex = 1, ThemeId = "hall", Templates = CloneForFloor(catalog, "_f1", "stair_f1") },
                new FloorTemplateSet { FloorIndex = -1, ThemeId = "hall", Templates = CloneForFloor(catalog, "_b1", "stair_b1") },
            };

            var floorParams = new[]
            {
                new FloorGenParams { FloorIndex = 0, ThemeId = "hall", RoomsTotalMin = 9, RoomsTotalMax = 11, CycleRoomPercent = 60, CorridorLinkPercent = 100, CorridorChainMax = 3, DangerBias = 0 },
                new FloorGenParams { FloorIndex = 1, ThemeId = "hall", RoomsTotalMin = 6, RoomsTotalMax = 8, CycleRoomPercent = 60, CorridorLinkPercent = 100, CorridorChainMax = 3, DangerBias = 1 },
                new FloorGenParams { FloorIndex = -1, ThemeId = "hall", RoomsTotalMin = 6, RoomsTotalMax = 8, CycleRoomPercent = 60, CorridorLinkPercent = 100, CorridorChainMax = 3, DangerBias = 2 },
            };

            return MapGenPlan.Compose(genParams, floorParams, floors);
        }

        /// <summary>계단실 템플릿 — 3×3, room_3x3 소켓 위상 재사용(D1 — 소켓 정렬 불변식 자동 충족).</summary>
        /// <param name="id">템플릿 ID.</param>
        /// <returns>계단실 서술자.</returns>
        public static RoomTemplateDef StairTemplate(string id)
        {
            return new RoomTemplateDef
            {
                TemplateId = id,
                WidthCells = 3,
                HeightCells = 3,
                AllowedFloors = FloorMask.F1,
                MinCount = 0,
                MaxCount = 3, // ShaftCountMax 상한 — 층당 최대 3개
                IsStairAnchor = true,
                Sockets = new[]
                {
                    new SocketDef { Id = 0, LocalCell = new CellCoord(1, 0), Direction = SocketDirection.South },
                    new SocketDef { Id = 1, LocalCell = new CellCoord(1, 2), Direction = SocketDirection.North },
                    new SocketDef { Id = 2, LocalCell = new CellCoord(0, 1), Direction = SocketDirection.West },
                    new SocketDef { Id = 3, LocalCell = new CellCoord(2, 1), Direction = SocketDirection.East },
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
