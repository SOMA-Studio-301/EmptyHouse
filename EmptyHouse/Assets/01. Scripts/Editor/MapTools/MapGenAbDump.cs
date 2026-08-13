using System.Globalization;
using System.IO;
using System.Text;
using Border.Core;
using EmptyHouse.MapGen.Core;
using EmptyHouse.MapGen.Runtime;
using UnityEditor;
using UnityEngine;

namespace EmptyHouse.EditorTools
{
    /// <summary>
    /// A/B 결과 불변 증명 도구 — 시드 구간의 블루프린트를 정규화 문자열로 덤프해 파일로 남긴다.
    /// 리팩터 전(A)·후(B)에 각각 실행해 바이트 비교하면 "결과 불변"이 기계 증명된다(M9-1 수용 기준).
    /// 파라미터·템플릿은 런타임과 같은 빈 집 정의(SO_Map_Hall) 단일 출처 — 실제 생성 경로 그대로(M10-1).
    /// 포맷은 Tests/BlueprintDump 와 동일 규칙(고정 순서·InvariantCulture) — Meta 는 제외(버전 문자열이 끼면 A/B 가 항상 다르다).
    /// </summary>
    public static class MapGenAbDump
    {
        private const string mapDefinitionPath = "Assets/03. ScriptableObjects/MapGen/SO_Map_Hall.asset"; // 빈 집 정의 단일 출처(M10-1)

        /// <summary>
        /// 시드 1~seedCount 의 덤프를 outDir 에 seed_{n:0000}.txt 로 기록한다.
        /// </summary>
        /// <param name="outDir">출력 폴더(절대 경로) — 없으면 만든다.</param>
        /// <param name="seedCount">덤프할 시드 수(1부터 연속).</param>
        /// <returns>요약 문자열(성공/실패 수) — unity-cli exec 반환용.</returns>
        public static string BuildDumps(string outDir, int seedCount)
        {
            Directory.CreateDirectory(outDir);
            var generator = new MapGenerator();
            var definition = AssetDatabase.LoadAssetAtPath<MapDefinitionSO>(mapDefinitionPath);
            int ok = 0;
            int fail = 0;
            for (int seed = 1; seed <= seedCount; seed++)
            {
                MapGenParams genParams = JsonUtility.FromJson<MapGenParams>(JsonUtility.ToJson(definition.GenParams));
                genParams.Seed = seed;
                MapGenPlan plan = MapPlanBuilder.Build(definition, genParams, out _);
                MapGenResult result = generator.Generate(plan);
                string path = Path.Combine(outDir, $"seed_{seed:0000}.txt");
                if (!result.Success)
                {
                    File.WriteAllText(path, $"FAIL {string.Join(" / ", result.FailReasons)}\n", new UTF8Encoding(false));
                    fail++;
                    continue;
                }

                File.WriteAllText(path, Dump(result.Blueprint), new UTF8Encoding(false));
                ok++;
            }

            string summary = $"[MapGenAbDump] {outDir} — 성공 {ok} · 실패 {fail}";
            Log.D(summary);
            return summary;
        }

        /// <summary>블루프린트 정규화 덤프 — Tests/BlueprintDump.Dump 와 같은 포맷(rooms/edges/spawns, Meta 제외).</summary>
        /// <param name="blueprint">덤프할 블루프린트.</param>
        /// <returns>정규화 문자열.</returns>
        public static string Dump(MapBlueprint blueprint)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < blueprint.Rooms.Count; i++)
            {
                BlueprintRoom room = blueprint.Rooms[i];
                sb.Append("room ").Append(i)
                  .Append('|').Append(room.TemplateId)
                  .Append('|').Append(room.Cell.X).Append(',').Append(room.Cell.Y)
                  .Append('|').Append(room.Rotation)
                  .Append("|f").Append(room.FloorIndex) // 층 서수 — Tests/BlueprintDump 와 포맷 동기(M9-2)
                  .Append('\n');
            }

            for (int i = 0; i < blueprint.Edges.Count; i++)
            {
                BlueprintEdge edge = blueprint.Edges[i];
                sb.Append("edge ").Append(i)
                  .Append('|').Append(edge.RoomA).Append('.').Append(edge.SocketA)
                  .Append('-').Append(edge.RoomB).Append('.').Append(edge.SocketB)
                  .Append('|').Append(edge.State)
                  .Append("|lock=").Append(edge.LockNumber).Append('.').Append(edge.LockKind)
                  .Append('\n');
            }

            for (int i = 0; i < blueprint.Spawns.Count; i++)
            {
                BlueprintSpawn spawn = blueprint.Spawns[i];
                sb.Append("spawn ").Append(i)
                  .Append("|room=").Append(spawn.RoomIndex)
                  .Append("|marker=").Append(spawn.MarkerId)
                  .Append('|').Append(spawn.Kind)
                  .Append("|wander=").Append(spawn.WanderRadiusCells.ToString("0.###", CultureInfo.InvariantCulture))
                  .Append("|key=").Append(spawn.KeyNumber)
                  .Append('\n');
            }

            return sb.ToString();
        }
    }
}
