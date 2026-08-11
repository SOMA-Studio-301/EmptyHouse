using System.Collections.Generic;
using System.Text;
using Border.Core;
using EmptyHouse.MapGen.Core;
using EmptyHouse.MapGen.Runtime;
using UnityEditor;
using UnityEngine;

namespace EmptyHouse.EditorTools
{
    /// <summary>
    /// 3층(B1·1F·2F) 스윕 플랜 팩토리 + 시드 스윕(M9-7) — 층 예산·샤프트 파라미터 확정용 실측 도구.
    /// 프리뷰(3층 보기)와 스윕이 같은 플랜을 쓴다. 어댑터 층 스택(MapFloorStackSO — M9-8)이 생기면
    /// 그쪽이 실제 플랜 원천이 되고 이 팩토리는 스윕·프리뷰 전용으로 남는다.
    /// </summary>
    public static class MapGenFloorSweep
    {
        private const string genParamsPath = "Assets/03. ScriptableObjects/MapGen/SO_MapGenParams.asset"; // 전역 파라미터 단일 출처
        private const string registryPath = "Assets/03. ScriptableObjects/MapGen/SO_MapPrefabRegistry.asset"; // 템플릿 단일 출처

        /// <summary>
        /// 3층 스윕 플랜을 만든다 — 시드 층(1F) = 레지스트리 템플릿 + 계단실, B1·2F = ID 접미사 사본 + 각 층 계단실.
        /// 층 예산은 FloorGenParams 코드 기본값(스윕이 확정한 값)을 쓴다. 백신 층 배정 = 2F 1 + B1 2(레벨디자인).
        /// </summary>
        /// <param name="seed">확정 시드.</param>
        /// <returns>3층 Plan.</returns>
        public static MapGenPlan ThreeFloorPlan(int seed)
        {
            var registry = AssetDatabase.LoadAssetAtPath<MapPrefabRegistrySO>(registryPath);
            var paramsAsset = AssetDatabase.LoadAssetAtPath<MapGenParamsSO>(genParamsPath);
            MapGenParams genParams = JsonUtility.FromJson<MapGenParams>(JsonUtility.ToJson(paramsAsset.Params));
            genParams.Seed = seed;
            genParams.VaccineFloorPlan = new[] { 1, -1, -1 }; // 레벨디자인 — 2F 1 + B1 2

            List<RoomTemplateDef> catalog = registry.CreateTemplates();
            var seedTemplates = new List<RoomTemplateDef>(catalog) { StairTemplate("stair_f0") };
            var floors = new[]
            {
                new FloorTemplateSet { FloorIndex = 0, ThemeId = "hall", Templates = seedTemplates.ToArray() },
                new FloorTemplateSet { FloorIndex = 1, ThemeId = "hall", Templates = CloneForFloor(catalog, "_f1", "stair_f1") },
                new FloorTemplateSet { FloorIndex = -1, ThemeId = "hall", Templates = CloneForFloor(catalog, "_b1", "stair_b1") },
            };

            // 층 예산·난이도 — FloorGenParams 코드 기본값이 스윕 확정값의 단일 원천(M9-7)
            var floorParams = new[]
            {
                new FloorGenParams { FloorIndex = 0, ThemeId = "hall", DangerBias = 0 },
                new FloorGenParams { FloorIndex = 1, ThemeId = "hall", DangerBias = 1 },
                new FloorGenParams { FloorIndex = -1, ThemeId = "hall", DangerBias = 2 },
            };

            return MapGenPlan.Compose(genParams, floorParams, floors);
        }

        /// <summary>계단실 템플릿 — 3×3(D1), room_3x3 소켓 위상 재사용.</summary>
        /// <param name="id">템플릿 ID.</param>
        /// <returns>계단실 서술자.</returns>
        private static RoomTemplateDef StairTemplate(string id)
        {
            return new RoomTemplateDef
            {
                TemplateId = id,
                WidthCells = 3,
                HeightCells = 3,
                AllowedFloors = FloorMask.F1,
                MinCount = 0,
                MaxCount = 3,
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

        /// <summary>비시드 층 템플릿 세트 — 입구 제외 사본(ID 접미사·MinCount 0) + 그 층 계단실.</summary>
        /// <param name="catalog">원천 템플릿 목록.</param>
        /// <param name="suffix">TemplateId 접미사.</param>
        /// <param name="stairId">계단실 ID.</param>
        /// <returns>층 템플릿 배열.</returns>
        private static RoomTemplateDef[] CloneForFloor(List<RoomTemplateDef> catalog, string suffix, string stairId)
        {
            var result = new List<RoomTemplateDef>();
            for (int t = 0; t < catalog.Count; t++)
            {
                if (catalog[t].IsEntranceAnchor)
                {
                    continue;
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

        /// <summary>시드 스윕(M9-7) — 실패율·리롤·층별 방 수·샤프트·사이클·백신 층 배정을 실측한다.</summary>
        /// <param name="seedCount">스윕 시드 수(1부터 연속).</param>
        /// <returns>요약 리포트.</returns>
        public static string BuildReport(int seedCount)
        {
            var generator = new MapGenerator();
            int ok = 0;
            int fail = 0;
            int rerollTotal = 0;
            int rerollMaxSeen = 0;
            var roomsPerFloor = new Dictionary<int, List<int>>();
            var shaftCounts = new List<int>();
            var cyclePerFloor = new Dictionary<int, List<float>>();
            var failReasonHead = new StringBuilder();

            for (int seed = 1; seed <= seedCount; seed++)
            {
                MapGenResult result = generator.Generate(ThreeFloorPlan(seed));
                if (!result.Success)
                {
                    fail++;
                    if (fail <= 3)
                    {
                        failReasonHead.AppendLine($"    시드 {seed}: {(result.FailReasons.Count > 0 ? result.FailReasons[result.FailReasons.Count - 1] : "?")}");
                    }

                    continue;
                }

                ok++;
                rerollTotal += result.RerollCount;
                rerollMaxSeen = Mathf.Max(rerollMaxSeen, result.RerollCount);
                shaftCounts.Add(result.Blueprint.Shafts.Count);
                foreach (BlueprintFloor floor in result.Blueprint.Floors)
                {
                    if (!roomsPerFloor.TryGetValue(floor.FloorIndex, out List<int> list))
                    {
                        roomsPerFloor[floor.FloorIndex] = list = new List<int>();
                        cyclePerFloor[floor.FloorIndex] = new List<float>();
                    }

                    list.Add(floor.RoomCount);
                    cyclePerFloor[floor.FloorIndex].Add(floor.CycleRoomPercentAchieved);
                }
            }

            var report = new StringBuilder();
            report.AppendLine($"[MapGenFloorSweep] 3층 시드 {seedCount} — 성공 {ok} 실패 {fail}({100f * fail / seedCount:F1}%) · 리롤 평균 {(ok > 0 ? (float)rerollTotal / ok : 0):F2} 최대 {rerollMaxSeen}");
            report.AppendLine($"  샤프트 {Avg(shaftCounts):F2} (범위 {Min(shaftCounts)}~{Max(shaftCounts)})");
            var floorKeys = new List<int>(roomsPerFloor.Keys);
            floorKeys.Sort();
            foreach (int floor in floorKeys)
            {
                report.AppendLine($"  층 {floor,2}: 방(복도·계단 포함) 평균 {Avg(roomsPerFloor[floor]):F1} (범위 {Min(roomsPerFloor[floor])}~{Max(roomsPerFloor[floor])}) · 층 사이클 {AvgF(cyclePerFloor[floor]):F1}%");
            }

            if (fail > 0)
            {
                report.Append("  실패 예시:\n").Append(failReasonHead);
            }

            Log.D(report.ToString());
            return report.ToString();
        }

        /// <summary>정수 목록 평균(빈 목록 0).</summary>
        private static float Avg(List<int> list)
        {
            if (list.Count == 0)
            {
                return 0f;
            }

            long sum = 0;
            foreach (int v in list)
            {
                sum += v;
            }

            return (float)sum / list.Count;
        }

        /// <summary>실수 목록 평균(빈 목록 0).</summary>
        private static float AvgF(List<float> list)
        {
            if (list.Count == 0)
            {
                return 0f;
            }

            float sum = 0f;
            foreach (float v in list)
            {
                sum += v;
            }

            return sum / list.Count;
        }

        /// <summary>정수 목록 최소(빈 목록 0).</summary>
        private static int Min(List<int> list)
        {
            int min = int.MaxValue;
            foreach (int v in list)
            {
                min = Mathf.Min(min, v);
            }

            return list.Count == 0 ? 0 : min;
        }

        /// <summary>정수 목록 최대(빈 목록 0).</summary>
        private static int Max(List<int> list)
        {
            int max = int.MinValue;
            foreach (int v in list)
            {
                max = Mathf.Max(max, v);
            }

            return list.Count == 0 ? 0 : max;
        }
    }
}
