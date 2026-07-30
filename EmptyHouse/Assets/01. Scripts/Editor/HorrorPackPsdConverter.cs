using System.Collections.Generic;
using System.IO;
using System.Text;
using Border.Core;
using UnityEditor;
using UnityEngine;

/// <summary>
/// HorrorPack 의 PSD 텍스처를 Unity 임포트 결과 그대로 PNG 로 바꿔 용량을 줄이는 일회성 에디터 툴.
/// 게임에 쓰이는 픽셀은 그대로 두고, Unity 가 애초에 쓰지 않는 레이어·16bit 데이터만 버린다.
/// .psd.meta 를 .png.meta 로 그대로 옮겨 GUID 를 보존하므로 머티리얼 참조가 끊기지 않는다.
/// </summary>
public static class HorrorPackPsdConverter
{
    private const string PackRoot = "Assets/04. Arts/Environment/HorrorPack"; // 변환 대상 폴더

    /// <summary>
    /// 변환 대상 PSD 의 소스 해상도와 임포트 설정을 조사해 콘솔에 출력한다. 파일은 건드리지 않는다.
    /// </summary>
    [MenuItem("Tools/Horror Pack/PSD 1. 사전 조사 (읽기 전용)")]
    public static void InspectPsd()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("===== PSD 사전 조사 =====");

        foreach (string path in FindPsdPaths())
        {
            TextureImporter imp = AssetImporter.GetAtPath(path) as TextureImporter;
            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            long len = new FileInfo(path).Length;

            imp.GetSourceTextureWidthAndHeight(out int srcW, out int srcH);

            sb.AppendLine($"{Path.GetFileName(path)}");
            sb.AppendLine($"   파일 {len / 1048576.0:N1} MB / 소스 {srcW}x{srcH} / 임포트결과 {tex.width}x{tex.height} {tex.format}");
            sb.AppendLine($"   type={imp.textureType} sRGB={imp.sRGBTexture} maxSize={imp.maxTextureSize} alpha={imp.alphaSource}");
        }

        Log.D(sb.ToString());
    }

    /// <summary>
    /// PSD 를 PNG 로 변환하고 원본 PSD 를 제거한다. meta 를 그대로 옮겨 GUID 를 유지한다.
    /// </summary>
    [MenuItem("Tools/Horror Pack/PSD 2. PNG 로 변환 (파일 변경)")]
    public static void ConvertPsdToPng()
    {
        List<string> psdPaths = FindPsdPaths();
        StringBuilder sb = new StringBuilder();
        List<string> converted = new List<string>();

        // 텍스처마다 원래 압축 설정이 다르므로(알베도=무압축, MET=DXT5) 각각 기억해 두었다가 그대로 되돌린다.
        Dictionary<string, bool> origReadable = new Dictionary<string, bool>();
        Dictionary<string, TextureImporterCompression> origCompression = new Dictionary<string, TextureImporterCompression>();
        Dictionary<string, int> origCompressionQuality = new Dictionary<string, int>();
        long before = 0;
        long after = 0;

        foreach (string psdPath in psdPaths)
        {
            TextureImporter imp = AssetImporter.GetAtPath(psdPath) as TextureImporter;

            string pngKey = Path.ChangeExtension(psdPath, ".png");
            origReadable[pngKey] = imp.isReadable;
            origCompression[pngKey] = imp.textureCompression;
            origCompressionQuality[pngKey] = imp.compressionQuality;

            before += new FileInfo(psdPath).Length;

            imp.isReadable = true;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            AssetDatabase.ImportAsset(psdPath, ImportAssetOptions.ForceUpdate);

            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(psdPath);
            byte[] png = tex.EncodeToPNG();

            string pngPath = Path.ChangeExtension(psdPath, ".png");
            File.WriteAllBytes(pngPath, png);

            // meta 를 그대로 옮겨야 GUID 가 유지되어 머티리얼 참조가 살아남는다.
            File.Move($"{psdPath}.meta", $"{pngPath}.meta");
            File.Delete(psdPath);

            long a = new FileInfo(pngPath).Length;
            after += a;
            converted.Add(pngPath);

            sb.AppendLine($"  {Path.GetFileName(psdPath)} -> {Path.GetFileName(pngPath)}  {tex.width}x{tex.height}  {a / 1048576.0:N1} MB");
        }

        AssetDatabase.Refresh();

        // 변환된 PNG 의 임포트 설정을 원래대로 되돌린다(읽기 불가 + 원래 압축).
        foreach (string pngPath in converted)
        {
            TextureImporter imp = AssetImporter.GetAtPath(pngPath) as TextureImporter;
            if (imp == null)
            {
                continue;
            }

            imp.isReadable = origReadable[pngPath];
            imp.textureCompression = origCompression[pngPath];
            imp.compressionQuality = origCompressionQuality[pngPath];
            AssetDatabase.ImportAsset(pngPath, ImportAssetOptions.ForceUpdate);
        }

        Log.D($"PSD -> PNG 변환 완료 {converted.Count}건 / {before / 1048576.0:N1} MB -> {after / 1048576.0:N1} MB\n{sb}");
    }

    /// <summary>
    /// 팩의 모든 텍스처가 현재 어떤 해상도·포맷으로 임포트되는지 출력한다. 변환 전후 비교용이다.
    /// </summary>
    [MenuItem("Tools/Horror Pack/PSD 3. 변환 결과 확인 (읽기 전용)")]
    public static void VerifyImportedFormats()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("===== 임포트 결과 포맷 =====");

        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { PackRoot }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            TextureImporter imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (tex == null || imp == null)
            {
                continue;
            }

            sb.AppendLine($"  {Path.GetFileName(path),-32} {tex.width}x{tex.height,-6} {tex.format,-10} sRGB={imp.sRGBTexture} readable={imp.isReadable}");
        }

        Log.D(sb.ToString());
    }

    /// <summary>
    /// 변환 대상 PSD 경로 목록을 수집한다.
    /// </summary>
    /// <returns>팩 폴더 아래 PSD 에셋 경로 목록.</returns>
    private static List<string> FindPsdPaths()
    {
        List<string> result = new List<string>();
        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { PackRoot }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.EndsWith(".psd", System.StringComparison.OrdinalIgnoreCase))
            {
                result.Add(path);
            }
        }

        result.Sort();
        return result;
    }
}
