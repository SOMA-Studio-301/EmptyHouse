using System.Collections.Generic;
using Border.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace EmptyHouse.EditorTools
{
    /// <summary>
    /// 달빛(디렉셔널) 누광 수정 툴.
    /// ① URP 에셋의 Shadow Distance를 150으로 올려 원거리 그림자 소실을 막고,
    /// ② 단면 메시인 구조물 프리팹(벽/바닥/천장)의 그림자 캐스팅을 Two Sided로 일괄 변경해
    /// 뒷면 컬링으로 인한 지붕/벽 관통을 차단한다.
    /// 실행 후 씬을 감사해 아직 단면(On)으로 남은 벽·슬래브의 소스 폴더를 보고한다.
    /// </summary>
    public static class MapShadowFixer
    {
        private const float targetShadowDistance = 150f;

        /// <summary>TwoSided를 적용할 구조물 프리팹 폴더들.</summary>
        private static readonly string[] archFolders =
        {
            "Assets/Horror_Pack_1/!Prefabs/Architectural",
            "Assets/04. Arts/Environment/HorrorPack/!Prefabs/Architectural",
        };

        /// <summary>URP 렌더 파이프라인 에셋 경로들.</summary>
        private static readonly string[] rpAssets =
        {
            "Assets/Settings/URP3D/PC_RPAsset.asset",
            "Assets/Settings/URP3D/Mobile_RPAsset.asset",
        };

        /// <summary>
        /// 그림자 거리 상향 + 구조물 프리팹 TwoSided 일괄 적용 후, 씬에 남은 단면 구조물을 감사한다.
        /// </summary>
        [MenuItem("Tools/Map/그림자 누광 수정 (TwoSided+거리)")]
        public static void Fix()
        {
            // ① Shadow Distance
            foreach (string path in rpAssets)
            {
                UniversalRenderPipelineAsset rp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
                if (rp == null) { Log.D($"[Shadow] URP 에셋 없음: {path}"); continue; }
                rp.shadowDistance = targetShadowDistance;
                EditorUtility.SetDirty(rp);
                Log.D($"[Shadow] {System.IO.Path.GetFileNameWithoutExtension(path)}: ShadowDistance → {targetShadowDistance}");
            }

            // ② 구조물 프리팹 TwoSided
            int prefabChanged = 0, rendererChanged = 0;
            foreach (string folder in archFolders)
            {
                if (!AssetDatabase.IsValidFolder(folder)) { Log.D($"[Shadow] 폴더 없음: {folder}"); continue; }
                foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    GameObject root = PrefabUtility.LoadPrefabContents(path);
                    if (root == null) continue;
                    bool changed = false;
                    foreach (MeshRenderer mr in root.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        if (mr.shadowCastingMode != ShadowCastingMode.On) continue;
                        mr.shadowCastingMode = ShadowCastingMode.TwoSided;
                        rendererChanged++;
                        changed = true;
                    }
                    if (changed) { PrefabUtility.SaveAsPrefabAsset(root, path); prefabChanged++; }
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
            AssetDatabase.SaveAssets();
            Log.D($"[Shadow] TwoSided 적용: 프리팹 {prefabChanged}개 / 렌더러 {rendererChanged}개");

            AuditScene();
        }

        /// <summary>
        /// 씬의 벽·슬래브형 렌더러 중 아직 단면(On)인 것들의 프리팹 소스 폴더를 집계해 보고한다.
        /// (다른 에셋으로 지은 셸이 커버됐는지 확인용)
        /// </summary>
        private static void AuditScene()
        {
            Dictionary<string, int> byFolder = new Dictionary<string, int>();
            int remain = 0;
            foreach (MeshRenderer mr in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (mr.shadowCastingMode != ShadowCastingMode.On) continue;
                Bounds b = mr.bounds;
                bool wallLike = b.size.y > 2.5f;
                bool slabLike = b.size.y < 0.3f && (b.size.x > 2f || b.size.z > 2f);
                if (!wallLike && !slabLike) continue;
                remain++;
                string src = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(mr.gameObject) ?? "(비프리팹)";
                int cut = src.LastIndexOf('/');
                string folder = cut > 0 ? src.Substring(0, cut) : src;
                if (!byFolder.ContainsKey(folder)) byFolder[folder] = 0;
                byFolder[folder]++;
            }
            if (remain == 0) { Log.D("[Shadow] 감사: 단면으로 남은 벽/슬래브 없음 — 완료."); return; }
            Log.D($"[Shadow] 감사: 단면(On) 벽/슬래브 {remain}개 잔존 — 소스 폴더:");
            foreach (KeyValuePair<string, int> kv in byFolder)
                Log.D($"[Shadow]   {kv.Value}개 ← {kv.Key}");
        }
    }
}
