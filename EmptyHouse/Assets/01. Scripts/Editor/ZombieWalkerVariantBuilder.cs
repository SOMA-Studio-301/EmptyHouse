using Border.Core;
using EmptyHouse.MapGen.Core;
using EmptyHouse.MapGen.Runtime;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Walker 좀비 외형 변종 4종을 만드는 1회용 에디터 툴.
/// mixamozombie FBX 4종을 Humanoid 로 재임포트한 뒤, Zombie.prefab 의 프리팹 변형(Variant)으로
/// 모델(ZombieModel)만 교체한 Zombie_Walker_1~4 를 생성하고 SO_MapPrefabRegistry 의
/// ZombieWalker 변종 풀(Variants)에 등재한다.
/// 스폰 시 변종 선택은 MapStateObjectSpawner.PickVariant(시드 결정론)가 이미 처리하므로 런타임 코드는 바뀌지 않는다.
/// 변형(Variant)으로 만드는 이유: Zombie.prefab 의 로직 컴포넌트 튜닝이 4종에 자동 전파된다.
/// </summary>
public static class ZombieWalkerVariantBuilder
{
    private const string zombiePrefabPath = "Assets/02. Prefab/Zombie/Zombie.prefab";
    private const string controllerPath = "Assets/02. Prefab/Zombie/ZombieLocomotion.controller";
    private const string registryPath = "Assets/03. ScriptableObjects/MapGen/SO_MapPrefabRegistry.asset";
    private const string modelFolder = "Assets/Characters/mixamozombie";
    private const string materialFolder = "Assets/Characters/Materials";
    private const string variantFolder = "Assets/02. Prefab/Zombie";
    private const string modelNodeName = "ZombieModel"; // Zombie.prefab 안의 모델 노드 이름 — 교체 대상 식별자

    // 변종 인덱스(1~4) 순서 = 파일 이름 순서. textures/zombie{N}_basecolor.png 와 번호가 짝이다
    private static readonly string[] modelFiles = { "Zombie1 Idle", "Zombie2 Crawl", "Zombie3 Running", "Zombie4 Walk" };

    /// <summary>재임포트 → 변형 4종 생성 → 레지스트리 등재를 한 번에 수행한다.</summary>
    [MenuItem("Tools/Zombie/Walker 모델 변종 4종 생성")]
    public static void Build()
    {
        Log.D("[ZombieWalkerVariant] 생성 시작");
        ReimportAsHumanoid();

        var variants = new NetworkObject[modelFiles.Length];
        for (int i = 0; i < modelFiles.Length; i++)
        {
            variants[i] = BuildVariant(i + 1, $"{modelFolder}/{modelFiles[i]}.fbx");
        }

        RegisterVariants(variants);
        AssetDatabase.SaveAssets();
        Log.D("[ZombieWalkerVariant] 생성 완료 — Zombie_Walker_1~4 + 레지스트리 등재");
    }

    /// <summary>
    /// FBX 4종을 Humanoid + Create Avatar 로 재임포트한다 — ZombieLocomotion(휴머노이드 클립) 리타게팅 전제.
    /// 이미 설정돼 있으면 건너뛴다(재실행 안전).
    /// </summary>
    private static void ReimportAsHumanoid()
    {
        foreach (string name in modelFiles)
        {
            string path = $"{modelFolder}/{name}.fbx";
            ModelImporter importer = (ModelImporter)AssetImporter.GetAtPath(path);
            if (importer == null)
            {
                Debug.LogError($"[ZombieWalkerVariant] FBX 없음: {path}");
                continue;
            }

            if (importer.animationType == ModelImporterAnimationType.Human
                && importer.avatarSetup == ModelImporterAvatarSetup.CreateFromThisModel)
            {
                continue;
            }

            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.SaveAndReimport();
            Log.D($"[ZombieWalkerVariant] Humanoid 재임포트: {name}");
        }
    }

    /// <summary>
    /// Zombie.prefab 의 프리팹 변형 하나를 만든다 — 기존 ZombieModel 제거 후 대상 FBX 모델을 같은 이름으로
    /// 삽입하고(레이어 승계, ZombieLocomotion, 루트모션 OFF, ZombieRootMotion 부착) 애니메이터 참조를 재배선한다.
    /// </summary>
    /// <param name="index">변종 번호(1부터) — 프리팹·머터리얼·텍스처 이름에 쓰인다.</param>
    /// <param name="fbxPath">모델 FBX 경로.</param>
    /// <returns>저장된 변형 프리팹의 NetworkObject.</returns>
    private static NetworkObject BuildVariant(int index, string fbxPath)
    {
        GameObject baseAsset = AssetDatabase.LoadAssetAtPath<GameObject>(zombiePrefabPath);
        var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(controllerPath);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(baseAsset);
        try
        {
            Object.DestroyImmediate(instance.transform.Find(modelNodeName).gameObject); // 변형에 '제거된 오브젝트' 오버라이드로 기록된다

            GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, instance.transform);
            model.name = modelNodeName;
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            SetLayerRecursively(model.transform, instance.layer);

            Animator animator = model.GetComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false; // 이동 권한은 서버 ZombieRootMotion — 클라는 NetworkTransform 이 받는다
            model.AddComponent<ZombieRootMotion>();
            EnsureMaterial(model, index);

            RewireAnimator(instance.GetComponent<ZombieController>(), animator);
            RewireAnimator(instance.GetComponent<ZombieAnimator>(), animator);

            string path = $"{variantFolder}/Zombie_Walker_{index}.prefab";
            PrefabUtility.SaveAsPrefabAsset(instance, path);
            Log.D($"[ZombieWalkerVariant] 변형 저장: {path}");
            return AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<NetworkObject>();
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    /// <summary>
    /// 임포트된 머터리얼이 깨져 있으면(셰이더가 URP 가 아니거나 베이스 텍스처 부재) textures 폴더의
    /// basecolor 로 URP Lit 머터리얼(M_Zombie{N})을 만들어 전 렌더러에 배정한다. 정상이면 그대로 둔다.
    /// metallic_roughness 는 채널 규약(glTF)과 URP 기대 채널이 달라 쓰지 않는다 — 대신 Smoothness 를 낮춘다.
    /// </summary>
    /// <param name="model">배치된 모델 인스턴스.</param>
    /// <param name="index">변종 번호(1부터).</param>
    private static void EnsureMaterial(GameObject model, int index)
    {
        var renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        bool broken = false;
        foreach (SkinnedMeshRenderer renderer in renderers)
        {
            foreach (Material material in renderer.sharedMaterials)
            {
                if (material == null || !material.shader.name.StartsWith("Universal Render Pipeline") || material.mainTexture == null)
                {
                    broken = true;
                }
            }
        }

        if (!broken)
        {
            Log.D($"[ZombieWalkerVariant] 변종 {index}: 임포트 머터리얼 정상 — 교체 생략(렌더러 {renderers.Length}개)");
            return;
        }

        string matPath = $"{materialFolder}/M_Zombie{index}.mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, matPath);
        }

        var baseColor = AssetDatabase.LoadAssetAtPath<Texture2D>($"{modelFolder}/textures/zombie{index}_basecolor.png");
        if (baseColor == null)
        {
            Debug.LogError($"[ZombieWalkerVariant] 변종 {index}: basecolor 텍스처 없음 — 머터리얼 텍스처 미배정");
        }

        mat.SetTexture("_BaseMap", baseColor);
        mat.SetFloat("_Smoothness", 0.2f);
        EditorUtility.SetDirty(mat);

        foreach (SkinnedMeshRenderer renderer in renderers)
        {
            var materials = new Material[renderer.sharedMaterials.Length];
            for (int m = 0; m < materials.Length; m++)
            {
                materials[m] = mat;
            }

            renderer.sharedMaterials = materials;
        }

        Log.D($"[ZombieWalkerVariant] 변종 {index}: 머터리얼 교체 {matPath}(렌더러 {renderers.Length}개)");
    }

    /// <summary>컴포넌트의 직렬화 필드 animator 를 새 모델의 Animator 로 재배선한다(private 필드라 SerializedObject 경유).</summary>
    /// <param name="component">ZombieController 또는 ZombieAnimator.</param>
    /// <param name="animator">새 모델의 Animator.</param>
    private static void RewireAnimator(Component component, Animator animator)
    {
        var so = new SerializedObject(component);
        so.FindProperty("animator").objectReferenceValue = animator;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>레지스트리의 ZombieWalker 항목 Variants 를 4종으로 갱신한다. 기본 Prefab(폴백)은 그대로 둔다.</summary>
    /// <param name="variants">등재할 변형 프리팹 목록.</param>
    private static void RegisterVariants(NetworkObject[] variants)
    {
        MapPrefabRegistrySO registry = AssetDatabase.LoadAssetAtPath<MapPrefabRegistrySO>(registryPath);
        foreach (SpawnPrefabEntry entry in registry.SpawnPrefabs)
        {
            if (entry.Kind != SpawnKind.ZombieWalker)
            {
                continue;
            }

            entry.Variants = variants;
            EditorUtility.SetDirty(registry);
            Log.D($"[ZombieWalkerVariant] 레지스트리 등재: ZombieWalker 변종 {variants.Length}종");
            return;
        }

        Debug.LogError("[ZombieWalkerVariant] 레지스트리에 ZombieWalker 항목 없음 — 등재 실패");
    }

    /// <summary>트리 전체의 레이어를 지정 값으로 맞춘다 — 좀비 레이어(시야·소음 판정)에 모델도 포함시킨다.</summary>
    /// <param name="root">루트 트랜스폼.</param>
    /// <param name="layer">적용 레이어.</param>
    private static void SetLayerRecursively(Transform root, int layer)
    {
        root.gameObject.layer = layer;
        foreach (Transform child in root)
        {
            SetLayerRecursively(child, layer);
        }
    }
}
