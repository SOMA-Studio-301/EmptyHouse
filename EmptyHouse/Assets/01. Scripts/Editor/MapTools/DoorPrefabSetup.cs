using Border.Core;
using EmptyHouse.MapGen.Runtime;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.AI;

namespace EmptyHouse.EditorTools
{
    /// <summary>
    /// Rooms/Door.prefab 을 절차 맵 네트워크 문으로 셋업한다(멱등) — NetworkObject·DoorInteractable·
    /// Animator(IsOpen)·NavMeshObstacle(Carve)·상호작용 면 2종(자물쇠·손잡이) 부착과 참조 배선,
    /// 문짝 개방 애니메이션 클립/컨트롤러 생성, 레지스트리 DoorPrefab·NetworkPrefabs 등재까지 처리한다.
    /// 자물쇠 비주얼 변종(lockVisualsByPair)은 아트 소관이라 비워 둔다 — 프리팹에 넣고 수동 연결.
    /// </summary>
    public static class DoorPrefabSetup
    {
        private const string doorPrefabPath = "Assets/02. Prefab/Map/DecoratedRooms/Door/Door.prefab"; // 대상 문 조립체(2026-08-13 폴더 이동 반영)
        private const string animFolder = "Assets/02. Prefab/Map/Rooms/DoorAnim"; // 애니메이션 에셋 폴더
        private const string controllerPath = animFolder + "/DoorAnimator.controller"; // IsOpen 토글 컨트롤러
        private const string closedClipPath = animFolder + "/Door_Closed.anim"; // 닫힘 포즈(기본 상태)
        private const string openClipPath = animFolder + "/Door_Open.anim"; // 개방 스윙(영구 개방 — 역방향 없음)
        private const string sfxChannelPath = "Assets/03. ScriptableObjects/Events/Audio/SO_Event_Sfx.asset"; // 오브젝트 사운드 채널
        private const string noiseChannelPath = "Assets/03. ScriptableObjects/Events/SO_Event_NoiseEmitted.asset"; // 좀비 지각 소음 채널
        private const string registryPath = "Assets/03. ScriptableObjects/MapGen/SO_MapPrefabRegistry.asset"; // 프리팹 레지스트리
        private const string networkPrefabsPath = "Assets/DefaultNetworkPrefabs.asset"; // NGO 기본 NetworkPrefabsList
        private const float openSwingDegrees = 100f; // 개방 스윙 각(적당값 — 경첩 방향이 반대면 부호만 뒤집어 재실행)
        private const float openSeconds = 1.0f; // 개방 소요 시간

        /// <summary>문 프리팹 컴포넌트·애니메이션·등재를 일괄 셋업한다(재실행 안전).</summary>
        public static void Setup()
        {
            Log.D("[DoorPrefabSetup] Setup");
            GameObject root = PrefabUtility.LoadPrefabContents(doorPrefabPath);

            Transform leafL = FindLeaf(root.transform, "Hall_Door_L");
            Transform leafR = FindLeaf(root.transform, "Hall_Door_R");
            if (leafL == null || leafR == null)
            {
                Log.E($"[DoorPrefabSetup] 문짝을 찾지 못했다(L={leafL != null}, R={leafR != null}) — Hall_Door_L*/Hall_Door_R* 이름 확인");
                PrefabUtility.UnloadPrefabContents(root);
                return;
            }

            AnimatorController controller = EnsureAnimator(root, leafL, leafR);

            EnsureComponent<NetworkObject>(root);
            Animator animator = EnsureComponent<Animator>(root);
            animator.runtimeAnimatorController = controller;

            NavMeshObstacle obstacle = EnsureComponent<NavMeshObstacle>(root);
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.center = new Vector3(0f, 1.4f, 0f);
            obstacle.size = new Vector3(3.4f, 2.8f, 0.6f); // 3M 개구 전폭 차단 — 개방 시 DoorInteractable 이 비활성화
            obstacle.carving = true;
            obstacle.carveOnlyStationary = true;

            var sfxChannel = AssetDatabase.LoadAssetAtPath<SFXEventChannelSO>(sfxChannelPath);
            var noiseChannel = AssetDatabase.LoadAssetAtPath<NoiseEventChannelSO>(noiseChannelPath);

            DoorInteractable door = EnsureComponent<DoorInteractable>(root);
            var doorSo = new SerializedObject(door);
            doorSo.FindProperty("doorAnimator").objectReferenceValue = animator;
            doorSo.FindProperty("navObstacle").objectReferenceValue = obstacle;
            doorSo.FindProperty("sfxEventChannel").objectReferenceValue = sfxChannel;
            doorSo.ApplyModifiedPropertiesWithoutUndo();

            // 손잡이 면은 양쪽 문짝 자식(개방 스윙과 함께 비켜나 열린 문간 레이를 가로채지 않게, 어느 쪽을 조작해도 동일).
            // 자물쇠는 별도 스폰 오브젝트 — 문은 양면 LockPos 앵커만 제공하고 스포너가 접근측 면을 고른다
            RemoveLegacyFaces(root, leafL, leafR);
            SetupHandleFace(leafL, door, noiseChannel, "HandleFace_L");
            SetupHandleFace(leafR, door, noiseChannel, "HandleFace_R");
            (Transform lockFront, Transform lockBack) = EnsureLockAnchors(root, leafR);
            var lockPosSo = new SerializedObject(door);
            lockPosSo.FindProperty("lockPosFront").objectReferenceValue = lockFront;
            lockPosSo.FindProperty("lockPosBack").objectReferenceValue = lockBack;
            lockPosSo.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, doorPrefabPath);
            PrefabUtility.UnloadPrefabContents(root);

            RegisterDoor();
            AssetDatabase.SaveAssets();
            Log.D("[DoorPrefabSetup] 완료 — 수동 확인: 경첩 방향(스윙 부호)·자물쇠 비주얼 변종 lockVisualsByPair 연결");
        }

        /// <summary>문짝 개방 클립 2종과 IsOpen 토글 컨트롤러를 확보한다 — 기존 에셋이 있으면 재사용.</summary>
        /// <param name="root">프리팹 루트(경로 계산 기준).</param>
        /// <param name="leafL">왼쪽 문짝.</param>
        /// <param name="leafR">오른쪽 문짝.</param>
        /// <returns>IsOpen 토글 애니메이터 컨트롤러.</returns>
        private static AnimatorController EnsureAnimator(GameObject root, Transform leafL, Transform leafR)
        {
            if (!AssetDatabase.IsValidFolder(animFolder))
            {
                AssetDatabase.CreateFolder("Assets/02. Prefab/Map/Rooms", "DoorAnim");
            }

            AnimationClip closedClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(closedClipPath);
            if (closedClip == null)
            {
                closedClip = new AnimationClip();
                WriteLeafCurves(closedClip, root, leafL, 0f, 0.033f);
                WriteLeafCurves(closedClip, root, leafR, 0f, 0.033f);
                AssetDatabase.CreateAsset(closedClip, closedClipPath);
            }

            AnimationClip openClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(openClipPath);
            if (openClip == null)
            {
                openClip = new AnimationClip();
                // 좌우 반대 방향 스윙 — 경첩 반대면 부호를 뒤집고 클립 삭제 후 재실행
                WriteLeafCurves(openClip, root, leafL, openSwingDegrees, openSeconds);
                WriteLeafCurves(openClip, root, leafR, -openSwingDegrees, openSeconds);
                AssetDatabase.CreateAsset(openClip, openClipPath);
            }

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
                controller.AddParameter("IsOpen", AnimatorControllerParameterType.Bool);

                AnimatorStateMachine machine = controller.layers[0].stateMachine;
                AnimatorState closed = machine.AddState("Closed");
                closed.motion = closedClip;
                machine.defaultState = closed;
                AnimatorState open = machine.AddState("Open");
                open.motion = openClip;

                AnimatorStateTransition toOpen = closed.AddTransition(open);
                toOpen.hasExitTime = false;
                toOpen.duration = 0f;
                toOpen.AddCondition(AnimatorConditionMode.If, 0f, "IsOpen");
            }

            return controller;
        }

        /// <summary>문짝 하나의 로컬 오일러 회전 커브(x·y·z)를 클립에 쓴다 — y 만 닫힘값 → 닫힘값+스윙으로 보간.</summary>
        /// <param name="clip">대상 클립.</param>
        /// <param name="root">경로 계산 기준 루트.</param>
        /// <param name="leaf">문짝 트랜스폼.</param>
        /// <param name="swing">y 스윙 각(0 = 고정 포즈).</param>
        /// <param name="seconds">클립 길이.</param>
        private static void WriteLeafCurves(AnimationClip clip, GameObject root, Transform leaf, float swing, float seconds)
        {
            string path = AnimationUtility.CalculateTransformPath(leaf, root.transform);
            Vector3 closed = leaf.localEulerAngles;
            clip.SetCurve(path, typeof(Transform), "localEulerAnglesRaw.x", AnimationCurve.Constant(0f, seconds, closed.x));
            clip.SetCurve(path, typeof(Transform), "localEulerAnglesRaw.y", AnimationCurve.EaseInOut(0f, closed.y, seconds, closed.y + swing));
            clip.SetCurve(path, typeof(Transform), "localEulerAnglesRaw.z", AnimationCurve.Constant(0f, seconds, closed.z));
        }

        /// <summary>손잡이 판정면을 문짝 자식으로 확보한다 — 문짝 메시 실측 바운드에 맞춘 트리거 박스.</summary>
        /// <param name="leaf">부모 문짝(메시 보유 오브젝트).</param>
        /// <param name="door">상태 단일 소스 문 루트.</param>
        /// <param name="noiseChannel">소음 채널(InteractableBase 요구).</param>
        /// <param name="name">면 이름.</param>
        private static void SetupHandleFace(Transform leaf, DoorInteractable door, NoiseEventChannelSO noiseChannel, string name)
        {
            Bounds bounds = LeafBounds(leaf);
            Vector3 size = bounds.size;
            size.z = Mathf.Max(size.z, 0.25f); // 얇은 문판이라 조준 두께 확보
            SetupFace<DoorHandleInteractable>(leaf, door, noiseChannel, name, bounds.center, size);
        }

        /// <summary>
        /// 자물쇠 스폰 앵커 양면(LockPos 앞면 −Z·LockPos_Back 뒷면 +Z)을 루트 자식으로 확보한다 —
        /// 닫힌 오른쪽 문짝의 이음(seam) 가장자리, 손잡이 높이. 뒷면은 문짝 두께 너머 미러 + 야우 180°.
        /// 스포너가 접근측(입구에서 얕은 방) 면을 골라 스폰한다. 자물쇠는 별도 NetworkObject 라 해정 시 소멸된다.
        /// </summary>
        /// <param name="root">프리팹 루트.</param>
        /// <param name="leafR">오른쪽 문짝(이음 기준).</param>
        /// <returns>(앞면, 뒷면) 앵커 트랜스폼.</returns>
        private static (Transform front, Transform back) EnsureLockAnchors(GameObject root, Transform leafR)
        {
            Transform front = EnsureChild(root, "LockPos");
            Transform back = EnsureChild(root, "LockPos_Back");

            Bounds bounds = LeafBounds(leafR);
            float seamSign = Mathf.Sign(bounds.center.x == 0f ? 1f : bounds.center.x);
            var seamLocal = new Vector3(bounds.center.x + seamSign * bounds.extents.x * 0.85f, Mathf.Max(1.0f, bounds.center.y * 0.85f), bounds.center.z);
            front.position = leafR.TransformPoint(seamLocal); // 닫힘 포즈 기준 월드 위치 — 루트 자식이라 개방 스윙과 무관
            front.rotation = root.transform.rotation;
            back.position = front.position + root.transform.forward * (bounds.extents.z * 2f + 0.04f); // 문짝 두께 + 여유 너머 반대면
            back.rotation = root.transform.rotation * Quaternion.Euler(0f, 180f, 0f);
            return (front, back);
        }

        /// <summary>루트 바로 아래 이름 일치 자식을 확보한다(없으면 생성).</summary>
        /// <param name="root">프리팹 루트.</param>
        /// <param name="name">자식 이름.</param>
        /// <returns>자식 트랜스폼.</returns>
        private static Transform EnsureChild(GameObject root, string name)
        {
            Transform child = root.transform.Find(name);
            if (child == null)
            {
                child = new GameObject(name).transform;
                child.SetParent(root.transform, false);
            }

            return child;
        }

        /// <summary>상호작용 면 자식을 확보한다 — Interactable 레이어·트리거 박스 콜라이더·면 스크립트·참조 배선.</summary>
        /// <param name="parent">면을 붙일 부모(문짝).</param>
        /// <param name="door">상태 단일 소스 문 루트.</param>
        /// <param name="noiseChannel">소음 채널(InteractableBase 요구).</param>
        /// <param name="name">면 이름.</param>
        /// <param name="center">콜라이더 중심(부모 로컬).</param>
        /// <param name="size">콜라이더 크기.</param>
        private static void SetupFace<T>(Transform parent, DoorInteractable door, NoiseEventChannelSO noiseChannel, string name, Vector3 center, Vector3 size) where T : Component
        {
            Transform face = parent.Find(name);
            if (face == null)
            {
                face = new GameObject(name).transform;
                face.SetParent(parent, false);
            }

            face.gameObject.layer = LayerMask.NameToLayer("Interactable");

            BoxCollider collider = EnsureComponent<BoxCollider>(face.gameObject);
            collider.isTrigger = true; // 이동을 막지 않되 조준 레이에는 걸린다(m_QueriesHitTriggers=1)
            collider.center = center;
            collider.size = size;

            T component = EnsureComponent<T>(face.gameObject);
            var so = new SerializedObject(component);
            so.FindProperty("door").objectReferenceValue = door;
            so.FindProperty("noiseEmittedChannel").objectReferenceValue = noiseChannel;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>문짝 메시의 로컬 바운드 — 면 콜라이더 실측 기준(문짝 오브젝트가 메시 보유자라는 전제, NavMesh 베이커와 동일).</summary>
        /// <param name="leaf">문짝 트랜스폼.</param>
        /// <returns>메시 로컬 바운드.</returns>
        private static Bounds LeafBounds(Transform leaf)
        {
            MeshFilter meshFilter = leaf.GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                return meshFilter.sharedMesh.bounds;
            }

            return new Bounds(new Vector3(0.7f, 1.3f, 0f), new Vector3(1.4f, 2.6f, 0.25f)); // 메시 미보유 폴백(3M 개구 반쪽 근사)
        }

        /// <summary>구버전 셋업 잔재(루트 면·문짝 LockFace)를 제거한다 — 자물쇠는 별도 스폰 체계로 전환됐다.</summary>
        /// <param name="root">프리팹 루트.</param>
        /// <param name="leafL">왼쪽 문짝.</param>
        /// <param name="leafR">오른쪽 문짝.</param>
        private static void RemoveLegacyFaces(GameObject root, Transform leafL, Transform leafR)
        {
            foreach (Transform parent in new[] { root.transform, leafL, leafR })
            {
                foreach (string name in new[] { "LockFace", "HandleFace" })
                {
                    Transform legacy = parent.Find(name);
                    if (legacy != null)
                    {
                        Object.DestroyImmediate(legacy.gameObject);
                    }
                }
            }
        }

        /// <summary>hall 테마 층 정의들의 DoorPrefab 과 NetworkPrefabs 목록에 문 프리팹을 등재한다(중복 등재 방지, M10-1).</summary>
        private static void RegisterDoor()
        {
            var doorAsset = AssetDatabase.LoadAssetAtPath<GameObject>(doorPrefabPath);
            int wired = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:FloorDefinitionSO", new[] { "Assets/03. ScriptableObjects/MapGen" }))
            {
                var floor = AssetDatabase.LoadAssetAtPath<FloorDefinitionSO>(AssetDatabase.GUIDToAssetPath(guid));
                if (floor.ThemeId != "hall")
                {
                    continue; // 문 외형은 테마 소유 — hall 층에만 배선
                }

                floor.DoorPrefab = doorAsset.GetComponent<NetworkObject>();
                EditorUtility.SetDirty(floor);
                wired++;
            }

            if (wired == 0)
            {
                Log.W("[DoorPrefabSetup] hall 층 정의 없음 — 런타임 어댑터 셋업을 먼저 실행");
            }

            var prefabsList = AssetDatabase.LoadAssetAtPath<ScriptableObject>(networkPrefabsPath);
            var listSo = new SerializedObject(prefabsList);
            SerializedProperty list = listSo.FindProperty("List");
            for (int i = 0; i < list.arraySize; i++)
            {
                if (list.GetArrayElementAtIndex(i).FindPropertyRelative("Prefab").objectReferenceValue == doorAsset)
                {
                    return; // 이미 등재
                }
            }

            list.InsertArrayElementAtIndex(list.arraySize);
            SerializedProperty entry = list.GetArrayElementAtIndex(list.arraySize - 1);
            entry.FindPropertyRelative("Override").enumValueIndex = 0;
            entry.FindPropertyRelative("Prefab").objectReferenceValue = doorAsset;
            entry.FindPropertyRelative("SourcePrefabToOverride").objectReferenceValue = null;
            entry.FindPropertyRelative("SourceHashToOverride").uintValue = 0;
            entry.FindPropertyRelative("OverridingTargetPrefab").objectReferenceValue = null;
            listSo.ApplyModifiedPropertiesWithoutUndo();
            Log.D("[DoorPrefabSetup] NetworkPrefabs 등재 완료");
        }

        /// <summary>이름 접두사로 문짝 트랜스폼을 찾는다(중첩 프리팹 내부 포함, 최상위 우선).</summary>
        /// <param name="root">탐색 루트.</param>
        /// <param name="prefix">이름 접두사(Hall_Door_L / Hall_Door_R).</param>
        /// <returns>일치 트랜스폼 — 없으면 null.</returns>
        private static Transform FindLeaf(Transform root, string prefix)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name.StartsWith(prefix))
                {
                    return child;
                }
            }

            return null;
        }

        /// <summary>컴포넌트를 확보한다(없으면 추가).</summary>
        /// <param name="target">대상 게임오브젝트.</param>
        /// <returns>확보한 컴포넌트.</returns>
        private static T EnsureComponent<T>(GameObject target) where T : Component
        {
            T component = target.GetComponent<T>();
            return component != null ? component : target.AddComponent<T>();
        }
    }
}
