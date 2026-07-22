using System.Collections.Generic;
using System.IO;
using System.Text;
using Border.Core;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Map 씬이 실제로 쓰는 Horror_Pack_1 에셋만 추려 저장소에 남길 폴더로 옮기는 일회성 에디터 툴.
/// GUID 를 보존하는 <see cref="AssetDatabase.MoveAsset"/> 를 쓰므로 씬·프리팹 참조가 끊기지 않는다.
/// 추출이 끝나면 삭제해도 되는 도구다.
/// </summary>
public static class HorrorPackExtractor
{
    private const string PackRoot = "Assets/Horror_Pack_1"; // 원본 팩 루트 (gitignore 대상)
    private const string PackScenes = "Assets/Horror_Pack_1/Scenes"; // 데모 씬 폴더 (라이팅 데이터 등 제외 대상)
    private const string MapScene = "Assets/00. Scenes/Map.unity"; // 추출 기준이 되는 맵 씬 (이미 이동 완료)
    private const string DestRoot = "Assets/04. Arts/Environment/HorrorPack"; // 추출 대상 폴더 (저장소 추적)

    /// <summary>
    /// Map 씬이 참조하는 팩 에셋과 용량을 집계해 콘솔에 출력한다. 파일은 건드리지 않는다.
    /// </summary>
    [MenuItem("Tools/Horror Pack/1. 의존성 집계 (읽기 전용)")]
    public static void ReportMapDependencies()
    {
        List<string> deps = CollectPackDependencies();

        long total = 0;
        Dictionary<string, long> byExt = new Dictionary<string, long>();
        foreach (string d in deps)
        {
            if (!File.Exists(d))
            {
                continue;
            }

            long len = new FileInfo(d).Length;
            total += len;
            string ext = Path.GetExtension(d).ToLowerInvariant();
            byExt.TryGetValue(ext, out long acc);
            byExt[ext] = acc + len;
        }

        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"이동 대상 {deps.Count}개 / {total / 1048576.0:N1} MB");
        foreach (KeyValuePair<string, long> kv in byExt)
        {
            sb.AppendLine($"  {kv.Value / 1048576.0,9:N1} MB  {kv.Key}");
        }

        Log.D(sb.ToString());
    }

    /// <summary>
    /// Map 씬과 그 씬이 쓰는 팩 에셋만 추적 대상 폴더로 이동한다. 나머지 팩 에셋은 원위치에 남는다.
    /// </summary>
    [MenuItem("Tools/Horror Pack/2. 사용 에셋만 추출 (이동)")]
    public static void ExtractUsedAssets()
    {
        List<string> deps = CollectPackDependencies();
        StringBuilder log = new StringBuilder();
        int moved = 0;
        int failed = 0;

        // 1단계: 대상 폴더를 먼저 전부 만든다.
        // StartAssetEditing() 배치 안에서 만든 폴더는 StopAssetEditing() 전까지
        // 에셋 DB에 등록되지 않아 MoveAsset 이 "Parent directory is not in asset database" 로 실패한다.
        foreach (string src in deps)
        {
            string dstDir = $"{DestRoot}/{Path.GetDirectoryName(src.Substring(PackRoot.Length + 1)).Replace('\\', '/')}";
            EnsureFolder(dstDir.TrimEnd('/'));
        }

        // 디스크에 만든 폴더를 에셋 DB에 등록시킨다. 이 Refresh 전에는 IsValidFolder 가 false 다.
        AssetDatabase.Refresh();

        // 2단계: 실제 이동만 배치로 묶는다.
        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (string src in deps)
            {
                string dst = $"{DestRoot}/{src.Substring(PackRoot.Length + 1)}";
                string err = AssetDatabase.MoveAsset(src, dst);
                if (string.IsNullOrEmpty(err))
                {
                    moved++;
                }
                else
                {
                    failed++;
                    log.AppendLine($"  실패: {src} -> {err}");
                }
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.Refresh();
        }

        Log.D($"추출 완료: 이동 {moved}개 / 실패 {failed}개\n{log}");
    }

    /// <summary>
    /// Map 씬이 참조하는 팩 에셋 경로 목록을 수집한다. 데모 씬 폴더 산출물은 제외한다.
    /// </summary>
    /// <returns>이동 대상이 되는 팩 내부 에셋 경로 목록.</returns>
    private static List<string> CollectPackDependencies()
    {
        List<string> result = new List<string>();
        foreach (string d in AssetDatabase.GetDependencies(MapScene, true))
        {
            if (!d.StartsWith(PackRoot))
            {
                continue;
            }

            // 데모 씬 폴더(LightingData 등)는 맵과 무관한 잡음이라 함께 옮기지 않는다.
            if (d.StartsWith(PackScenes))
            {
                continue;
            }

            result.Add(d);
        }

        result.Sort();
        return result;
    }

    /// <summary>
    /// 지정한 에셋 폴더 경로를 디스크에 만든다. 등록은 호출부의 <see cref="AssetDatabase.Refresh()"/> 가 담당한다.
    /// </summary>
    /// <param name="folder">"Assets/..." 형태의 폴더 경로.</param>
    /// <remarks>
    /// <see cref="AssetDatabase.CreateFolder"/> 를 쓰면 안 된다. 디스크에 폴더가 이미 있어도
    /// 에셋 DB에 등록돼 있지 않으면 <see cref="AssetDatabase.IsValidFolder"/> 가 false 를 돌려주고,
    /// CreateFolder 는 "이름 1", "이름 2" 처럼 다른 이름으로 새 폴더를 만든다.
    /// 요청한 경로는 끝내 유효해지지 않으므로 호출할 때마다 빈 중복 폴더가 쌓인다.
    /// </remarks>
    private static void EnsureFolder(string folder)
    {
        string abs = Path.Combine(Directory.GetCurrentDirectory(), folder);
        Directory.CreateDirectory(abs);
    }
}
