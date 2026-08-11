using System.Collections.Generic;
using Border.Core;
using EmptyHouse.MapGen.Core;
using EmptyHouse.MapGen.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace EmptyHouse.MapGen.Editor
{
    /// <summary>
    /// 예시 맵 씬 빌더 — 블루프린트 지오메트리 조립은 **런타임 조립기(MapRuntimeAssembler.Assemble)를 그대로 호출**하고,
    /// 그 위에 그레이박스 확인용 부가물(문·탈출문 실물, 스폰 표식, 라이트 오프)만 얹는다(AC-21 — 동일 조립 라이브러리).
    /// 과거의 기하 라인 복제(PlaceRoom/PlaceOpening 등 20여 함수)는 M9-1 에서 제거 — 조립 규칙 수정은 조립기 한 곳이다.
    /// 재실행 시 기존 GeneratedMaps 루트를 통째로 지우고 다시 만든다(멱등 — 원복 = 루트 삭제).
    /// </summary>
    public static class MapGenSceneBuilder
    {
        private const int mapCount = 5; // 예시 맵 수
        private const int baseSeed = 101; // 시드 탐색 시작값(재현 고정)
        private const float mapSpacing = 400f; // 맵 루트 간 X 간격(m) — 기본값(방 58~60 + 복도) 바운드와 여유
        private const float mapZOffset = -400f; // 수작업 맵(Z 0~160)과 겹치지 않는 남쪽 오프셋
        private const string rootName = "GeneratedMaps"; // 생성물 루트 이름(통째 삭제 = 원복)
        private const string markerMaterialFolder = "Assets/02. Prefab/Map/MapGenMarkers"; // 표식 머티리얼 폴더
        private const string sharedParamsPath = "Assets/03. ScriptableObjects/MapGen/SO_MapGenParams.asset"; // 런타임 드라이버와 공유하는 파라미터 에셋(단일 출처)
        private const string registryPath = "Assets/03. ScriptableObjects/MapGen/SO_MapPrefabRegistry.asset"; // 프리팹 레지스트리(단일 출처)

        /// <summary>
        /// 활성 씬에 공유 파라미터(SO_MapGenParams) 기준 예시 맵 5개를 생성한다 —
        /// baseSeed 부터 성공 시드를 5개 채택해 X 방향으로 나란히 배치하고 씬을 저장한다.
        /// </summary>
        [MenuItem("Tools/Map/절차 예시 맵 5개 생성")]
        public static void BuildFiveExampleMaps()
        {
            Log.D("[MapGenSceneBuilder] BuildFiveExampleMaps");
            if (EditorApplication.isPlaying)
            {
                Log.W("[MapGenSceneBuilder] 플레이 모드에서는 실행할 수 없다 — 정지 후 재실행.");
                return;
            }

            UnityEngine.SceneManagement.Scene activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(activeScene.path))
            {
                Log.W("[MapGenSceneBuilder] Untitled 씬은 저장 경로가 없어 조립 결과를 보존할 수 없다 — 씬을 먼저 저장한 뒤 재실행.");
                return;
            }

            // 비활성 루트·과거 중복 루트까지 전부 제거(멱등) — GameObject.Find 는 비활성을 못 찾고 타 씬까지 검색한다
            foreach (GameObject sceneRoot in activeScene.GetRootGameObjects())
            {
                if (sceneRoot.name == rootName)
                {
                    Object.DestroyImmediate(sceneRoot);
                }
            }

            var root = new GameObject(rootName);
            var generator = new MapGenerator();
            List<RoomTemplateDef> templates = MapTemplateCatalog.Create();
            var registry = AssetDatabase.LoadAssetAtPath<MapPrefabRegistrySO>(registryPath);

            int built = 0;
            int seed = baseSeed;
            var summary = new System.Text.StringBuilder();
            int totalIssues = 0;
            while (built < mapCount && seed < baseSeed + 40)
            {
                MapGenResult result = generator.Generate(CreateParams(seed), templates);
                if (!result.Success)
                {
                    Log.D($"[MapGenSceneBuilder] 시드 {seed} 실패 — 다음 시드 시도: {string.Join(" / ", result.FailReasons)}");
                    seed++;
                    continue;
                }

                Vector3 offset = new Vector3(built * mapSpacing, 0f, mapZOffset);
                GameObject mapRoot = BuildMap(result.Blueprint, templates, registry, root.transform, offset, built);
                int issues = MapGenSceneAuditor.AuditMap(mapRoot, result.Blueprint, templates, built);
                totalIssues += issues;

                // 방 수는 방 전용 집계(복도·입구 제외) — Rooms.Count 그대로 찍으면 예산 위반으로 오독된다
                int roomTotal = 0;
                int corridorTotal = 0;
                for (int r = 0; r < result.Blueprint.Rooms.Count; r++)
                {
                    RoomTemplateDef template = MapRuntimeAssembler.FindTemplate(templates, result.Blueprint.Rooms[r].TemplateId);
                    if (template.IsCorridor)
                    {
                        corridorTotal++;
                    }
                    else if (!template.IsEntranceAnchor)
                    {
                        roomTotal++;
                    }
                }

                summary.AppendLine($"맵 {built}: 시드 {seed} · 방 {roomTotal} · 복도 {corridorTotal} · 리롤 {result.RerollCount} · 경고 {result.LastReport.Warnings.Count} · 감사 {issues}건");
                built++;
                seed++;
            }

            EditorSceneManager.MarkSceneDirty(root.scene);
            if (!EditorSceneManager.SaveScene(root.scene))
            {
                Log.E($"[MapGenSceneBuilder] 씬 저장 실패 — 수동 저장(Ctrl+S) 필요. path='{root.scene.path}'");
            }

            if (totalIssues > 0)
            {
                Log.W($"[MapGenSceneBuilder] 완료 — {built}/{mapCount}개 생성 · 감사 문제 {totalIssues}건(콘솔 [Audit] 라인·씬 AuditMarkers 참조)\n{summary}");
            }
            else
            {
                Log.D($"[MapGenSceneBuilder] 완료 — {built}/{mapCount}개 생성 · 감사 문제 0건\n{summary}");
            }
        }

        /// <summary>
        /// 씬 빌더 생성 파라미터 — 런타임 드라이버와 **같은 에셋**(SO_MapGenParams)을 직접 읽어 시드만 덮는다.
        /// 프리뷰에서 본 맵 = 실제 플레이 맵을 보장하는 지점이다(과거 코드 초기값을 쓰다 복도 경유 100 vs 50 으로 갈렸다).
        /// 에셋 오염 방지를 위해 JSON 복제본을 반환한다. 감사 도구가 같은 파라미터로 블루프린트를 재현한다(드리프트 방지).
        /// </summary>
        /// <param name="seed">확정 시드.</param>
        /// <returns>생성 파라미터 복제본.</returns>
        internal static MapGenParams CreateParams(int seed)
        {
            var asset = AssetDatabase.LoadAssetAtPath<MapGenParamsSO>(sharedParamsPath);
            MapGenParams genParams;
            if (asset == null)
            {
                Log.W($"[MapGenSceneBuilder] 파라미터 에셋 없음 — 코드 기본값으로 진행(런타임과 어긋날 수 있다): {sharedParamsPath}");
                genParams = new MapGenParams();
            }
            else
            {
                genParams = JsonUtility.FromJson<MapGenParams>(JsonUtility.ToJson(asset.Params));
            }

            genParams.Seed = seed;
            return genParams;
        }

        /// <summary>
        /// 블루프린트 하나를 조립한다 — 지오메트리는 런타임 조립기 호출(동일 코드 경로), 그 위에
        /// 그레이박스 부가물: 문·탈출문 실물(앵커 위치), 스폰 표식, 내장 라이트 오프(Forward+ 한도)·컬러 제거.
        /// </summary>
        /// <param name="blueprint">조립할 블루프린트.</param>
        /// <param name="templates">생성에 사용한 템플릿 목록.</param>
        /// <param name="registry">프리팹 레지스트리.</param>
        /// <param name="parent">루트 부모.</param>
        /// <param name="offset">맵 앵커 월드 오프셋(입구 앵커 방이 이 위치에 온다 — 런타임 입구 고정과 동일 규약).</param>
        /// <param name="index">맵 번호(이름용).</param>
        /// <returns>맵 루트 게임오브젝트(감사용).</returns>
        private static GameObject BuildMap(MapBlueprint blueprint, IReadOnlyList<RoomTemplateDef> templates, MapPrefabRegistrySO registry, Transform parent, Vector3 offset, int index)
        {
            Log.D($"[MapGenSceneBuilder] BuildMap {index} 시드={blueprint.Meta.Seed}");

            // 런타임과 같은 조립 — 임시 앵커를 오프셋에 두고 Assemble 이 입구를 그 위치에 고정하게 한다
            var anchor = new GameObject($"MapAnchor_{index}");
            anchor.transform.SetParent(parent, false);
            anchor.transform.position = offset;
            GameObject mapRoot = MapRuntimeAssembler.Assemble(blueprint, templates, registry, anchor.transform, null,
                (prefab, parentTransform) => (GameObject)PrefabUtility.InstantiatePrefab(prefab, parentTransform)); // 프리팹 링크 보존 — 씬 직렬화 비대 방지
            mapRoot.name = $"GeneratedMap_{index}_Seed{blueprint.Meta.Seed}";
            mapRoot.transform.SetParent(parent, true);
            Object.DestroyImmediate(anchor);

            // 그레이박스는 정적 씬 — 런타임 컬러 컴포넌트를 제거하고 라이트를 전부 끈다
            // (맵 5개면 라이트 300개+ 로 URP Forward+ 한도를 넘어 씬 뷰 렌더 에러(ZBinningJob)가 난다)
            var culler = mapRoot.GetComponent<EmptyHouse.Environment.MapLightCuller>();
            if (culler != null)
            {
                Object.DestroyImmediate(culler);
            }

            foreach (Light light in mapRoot.GetComponentsInChildren<Light>(true))
            {
                light.enabled = false;
            }

            // 문·탈출문 실물 — 서버 스포너(MapStateObjectSpawner)와 같은 규칙: 조립기가 남긴 앵커 위치에
            // 프리팹을 세우고 문틀 실측 바운드 중심을 앵커 XZ 에 맞춘다
            Transform doorsRoot = mapRoot.transform.Find("Doors");
            for (int e = 0; e < blueprint.Edges.Count; e++)
            {
                BlueprintEdge edge = blueprint.Edges[e];
                if (edge.State == EdgeState.DoorOpen || edge.State == EdgeState.DoorLocked)
                {
                    Transform doorAnchor = doorsRoot.Find($"DoorAnchor_e{e}");
                    if (doorAnchor == null)
                    {
                        continue; // 개구 기하 불일치로 조립기가 생략한 간선 — 감사가 표면화한다
                    }

                    GameObject door = PlaceAlignedDoor(registry.DoorPrefab.gameObject, doorAnchor, doorsRoot);
                    door.name = edge.State == EdgeState.DoorLocked
                        ? $"Door_e{e}_Locked_{edge.LockNumber}_{edge.LockKind}"
                        : $"Door_e{e}_Open";
                }
                else if (edge.State == EdgeState.ReturnExit)
                {
                    Transform returnAnchor = doorsRoot.Find($"ReturnAnchor_e{e}");
                    if (returnAnchor == null)
                    {
                        continue;
                    }

                    GameObject exitDoor = PlaceAlignedDoor(registry.ReturnExitPrefab.gameObject, returnAnchor, doorsRoot);
                    exitDoor.name = $"ReturnExit_e{e}_방{edge.RoomA}";
                }
            }

            // 스폰 표식
            var spawnsRoot = new GameObject("Spawns");
            spawnsRoot.transform.SetParent(mapRoot.transform, false);
            (int minX, int minY) = MapRuntimeAssembler.MinCellBounds(blueprint);
            for (int s = 0; s < blueprint.Spawns.Count; s++)
            {
                PlaceSpawnMarker(blueprint, templates, blueprint.Spawns[s], spawnsRoot.transform, mapRoot.transform.position, minX, minY);
            }

            return mapRoot;
        }

        /// <summary>문 계열 프리팹을 앵커 위치·회전에 세우고 문틀 실측 바운드 중심을 앵커 XZ 에 맞춘다(스포너 동일 규칙).</summary>
        /// <param name="prefab">문 프리팹 루트.</param>
        /// <param name="anchor">조립기가 남긴 앵커.</param>
        /// <param name="doorsRoot">부모 컨테이너.</param>
        /// <returns>배치된 인스턴스.</returns>
        private static GameObject PlaceAlignedDoor(GameObject prefab, Transform anchor, Transform doorsRoot)
        {
            var door = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            door.transform.SetParent(doorsRoot, false);
            door.transform.position = anchor.position;
            door.transform.rotation = anchor.rotation;
            Bounds frame = MapRuntimeAssembler.FrameBounds(door);
            door.transform.position += new Vector3(anchor.position.x - frame.center.x, 0f, anchor.position.z - frame.center.z);
            return door;
        }

        /// <summary>스폰 표식(색 구체)을 마커 전역 셀 중심에 놓는다 — 열쇠는 KeyNumber 를 이름에 표기.</summary>
        /// <param name="blueprint">대상 블루프린트.</param>
        /// <param name="templates">템플릿 목록.</param>
        /// <param name="spawn">표식할 스폰.</param>
        /// <param name="spawnsRoot">표식 부모.</param>
        /// <param name="mapOrigin">맵 원점 월드 좌표.</param>
        /// <param name="minX">맵 최소 셀 X.</param>
        /// <param name="minY">맵 최소 셀 Y.</param>
        private static void PlaceSpawnMarker(MapBlueprint blueprint, IReadOnlyList<RoomTemplateDef> templates, BlueprintSpawn spawn, Transform spawnsRoot, Vector3 mapOrigin, int minX, int minY)
        {
            RoomTemplateDef template = MapRuntimeAssembler.FindTemplate(templates, blueprint.Rooms[spawn.RoomIndex].TemplateId);
            MarkerDef marker = FindMarker(template, spawn.MarkerId);
            if (marker == null)
            {
                return; // 앵커 전용 종류(벽장 — MarkerId -1)는 코어 마커가 없다. 좌표는 방 프리팹 MapItemAnchor 소관이라 표식도 생략
            }

            CellCoord worldCell = CellMath.WorldCell(blueprint.Rooms[spawn.RoomIndex], template, marker.LocalCell);

            float cell = MapTemplateCatalog.CellMeters;
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(sphere.GetComponent<Collider>());
            sphere.transform.SetParent(spawnsRoot, false);
            sphere.transform.position = mapOrigin + new Vector3((worldCell.X - minX + 0.5f) * cell, 1.2f, (worldCell.Y - minY + 0.5f) * cell);
            sphere.transform.localScale = Vector3.one * (spawn.Kind == SpawnKind.HerdArea ? 2f : 1.1f);
            sphere.name = spawn.Kind == SpawnKind.Key ? $"Spawn_Key_{spawn.KeyNumber}" : $"Spawn_{spawn.Kind}";
            sphere.GetComponent<Renderer>().sharedMaterial = MarkerMaterial(spawn.Kind);
        }

        /// <summary>스폰 종류별 표식 머티리얼을 가져온다 — 없으면 폴더와 함께 생성해 에셋으로 저장한다.</summary>
        /// <param name="kind">스폰 종류.</param>
        /// <returns>공유 머티리얼.</returns>
        private static Material MarkerMaterial(SpawnKind kind)
        {
            (string name, Color color) = kind switch
            {
                SpawnKind.ZombieWalker => ("MG_Zombie", new Color(0.85f, 0.15f, 0.15f)),
                SpawnKind.ZombieListener => ("MG_Listener", new Color(1f, 0.45f, 0.1f)),
                SpawnKind.ZombieWatcher => ("MG_Watcher", new Color(0.7f, 0.1f, 0.55f)),
                SpawnKind.VaccineAntigen => ("MG_Vaccine", new Color(0.1f, 0.8f, 0.3f)),
                SpawnKind.VaccineSerum => ("MG_Vaccine", new Color(0.1f, 0.8f, 0.3f)),
                SpawnKind.VaccineStabilizer => ("MG_Vaccine", new Color(0.1f, 0.8f, 0.3f)),
                SpawnKind.Key => ("MG_Key", new Color(1f, 0.85f, 0.1f)),
                SpawnKind.Fuel => ("MG_Fuel", new Color(0.55f, 0.35f, 0.1f)),
                SpawnKind.Scrap => ("MG_Scrap", new Color(0.55f, 0.55f, 0.55f)),
                SpawnKind.Throwable => ("MG_Throwable", new Color(0.1f, 0.7f, 0.85f)),
                SpawnKind.CorpseStation => ("MG_Station", new Color(0.5f, 0.2f, 0.8f)),
                SpawnKind.Generator => ("MG_Generator", new Color(0.15f, 0.4f, 0.95f)),
                _ => ("MG_Herd", new Color(0.6f, 0.05f, 0.05f)),
            };

            return GetOrCreateMaterial(name, color);
        }

        /// <summary>이름·색으로 머티리얼 에셋을 가져오거나 없으면 폴더와 함께 생성한다(감사 도구 공용).</summary>
        /// <param name="name">에셋 이름(확장자 제외).</param>
        /// <param name="color">기본 색.</param>
        /// <returns>공유 머티리얼.</returns>
        internal static Material GetOrCreateMaterial(string name, Color color)
        {
            string path = $"{markerMaterialFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
            {
                return material;
            }

            if (!AssetDatabase.IsValidFolder(markerMaterialFolder))
            {
                AssetDatabase.CreateFolder("Assets/02. Prefab/Map", "MapGenMarkers");
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            material = new Material(shader);
            material.SetColor("_BaseColor", color);
            material.SetColor("_Color", color);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        /// <summary>템플릿에서 마커 Id 로 마커를 찾는다.</summary>
        /// <param name="template">대상 템플릿.</param>
        /// <param name="markerId">마커 Id.</param>
        /// <returns>마커 정의 — 없으면 null.</returns>
        private static MarkerDef FindMarker(RoomTemplateDef template, int markerId)
        {
            for (int m = 0; m < template.Markers.Length; m++)
            {
                if (template.Markers[m].Id == markerId)
                {
                    return template.Markers[m];
                }
            }

            return null;
        }
    }
}
