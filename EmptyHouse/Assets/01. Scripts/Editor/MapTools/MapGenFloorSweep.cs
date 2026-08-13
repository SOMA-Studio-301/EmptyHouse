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
    /// 3층(B1·1F·2F) 시드 스윕(M9-7) — 층 예산·샤프트 파라미터 확정용 실측 도구.
    /// 플랜 원천은 런타임과 같은 빈 집 정의(SO_Map_Hall3F — M10-1)·같은 조립 경로(MapPlanBuilder)다(AC-21).
    /// </summary>
    public static class MapGenFloorSweep
    {
        private const string mapDefinitionPath = "Assets/03. ScriptableObjects/MapGen/SO_Map_Hall3F.asset"; // 3층 빈 집 정의 단일 출처(M10-1)

        /// <summary>3층 스윕 플랜을 만든다 — 빈 집 정의(SO_Map_Hall3F)에서 MapPlanBuilder 로 조립한다.</summary>
        /// <param name="seed">확정 시드.</param>
        /// <returns>3층 Plan — 린트 실패 시 null.</returns>
        public static MapGenPlan ThreeFloorPlan(int seed)
        {
            var definition = AssetDatabase.LoadAssetAtPath<MapDefinitionSO>(mapDefinitionPath);
            MapGenParams genParams = JsonUtility.FromJson<MapGenParams>(JsonUtility.ToJson(definition.GenParams));
            genParams.Seed = seed;
            return MapPlanBuilder.Build(definition, genParams, out _);
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
