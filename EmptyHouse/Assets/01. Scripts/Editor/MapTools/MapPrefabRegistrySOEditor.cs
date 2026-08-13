using System.Collections.Generic;
using EmptyHouse.MapGen.Core;
using EmptyHouse.MapGen.Runtime;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

namespace EmptyHouse.EditorTools
{
    /// <summary>
    /// MapPrefabRegistrySO(CommonRegistry — M10-1) 커스텀 인스펙터 — SpawnKind 전 종을 고정 행으로 노출해
    /// ID 타이핑 없이 슬롯 할당만으로 설정하게 하고, 누락·중복을 즉시 표시한다.
    /// 환경(테마 종속) 프리팹 섹션은 FloorDefinitionSO 이관으로 제거됐다(설계 B′ 분류표).
    /// 직렬화 형식은 원본 배열 그대로 유지한다(MapGenRuntimeSetup·소비 컴포넌트 호환).
    /// </summary>
    [CustomEditor(typeof(MapPrefabRegistrySO))]
    public sealed class MapPrefabRegistrySOEditor : Editor
    {
        private static readonly Color missingTint = new Color(1f, 0.55f, 0.55f); // 필수 슬롯 미할당 강조색

        /// <summary>SpawnKind 표시 그룹 — 인스펙터 행 순서의 단일 원천. HerdArea 는 구역 표지(스폰 오브젝트 아님)라 제외.</summary>
        private static readonly (string label, SpawnKind[] kinds)[] spawnGroups =
        {
            ("좀비", new[] { SpawnKind.ZombieWalker, SpawnKind.ZombieListener, SpawnKind.ZombieWatcher }),
            ("백신", new[] { SpawnKind.VaccineAntigen, SpawnKind.VaccineSerum, SpawnKind.VaccineStabilizer }),
            ("아이템", new[] { SpawnKind.Fuel, SpawnKind.Scrap, SpawnKind.Throwable }), // Key 는 아래 페어 섹션이 번호별로 관리한다(공용 프리팹 없음)
            ("설비", new[] { SpawnKind.CorpseStation, SpawnKind.Generator }),
        };

        private const int maxLockPairs = 5; // 자물쇠 최대 쌍 수 — ItemDoorLockCount(2) + ShortcutLockCountMax(3)

        private static readonly HashSet<SpawnKind> spawnVariantFoldout = new HashSet<SpawnKind>(); // 변종 풀을 펼친 종류(도메인 리로드까지 유지)

        /// <summary>레지스트리 전용 인스펙터를 그린다 — 스폰(변종·페어) 섹션.</summary>
        public override void OnInspectorGUI()
        {
            var registry = (MapPrefabRegistrySO)target;
            DrawSpawnSection(registry);
        }

        /// <summary>SpawnKind 전 종 상태 오브젝트 섹션 — 그룹별 고정 행 + 중복 정리 + 미등재 안내.</summary>
        /// <param name="registry">대상 레지스트리.</param>
        private static void DrawSpawnSection(MapPrefabRegistrySO registry)
        {
            EditorGUILayout.LabelField("상태 오브젝트 — 서버 스폰(NetworkPrefabs 등록 필수)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("문·탈출문·봉인 벽·기둥·방 템플릿은 층 정의(FloorDefinitionSO) 소관으로 이관됐다(M10-1)", MessageType.None);

            foreach ((string label, SpawnKind[] kinds) group in spawnGroups)
            {
                EditorGUILayout.Space(2f);
                EditorGUILayout.LabelField(group.label, EditorStyles.miniBoldLabel);
                foreach (SpawnKind kind in group.kinds)
                {
                    DrawSpawnRow(registry, kind);
                }
            }

            DrawPairSection(registry);
            DrawDuplicateSpawnEntries(registry);

            int unassigned = CountUnassignedSpawnKinds(registry);
            if (unassigned > 0)
            {
                EditorGUILayout.HelpBox($"미등재 스폰 종류 {unassigned}종 — 해당 종류는 스폰이 생략된다", MessageType.Info);
            }
        }

        /// <summary>
        /// SpawnKind 한 종의 행을 그린다 — 기본 슬롯 + 변종 풀 접이식.
        /// 기본 슬롯 할당 시 항목 추가, 해제 시 항목 제거. 변종이 1개 이상이면 기본 대신 변종에서 뽑히므로
        /// 행 우측에 그 사실을 배지로 알린다(비어 있으면 기본 프리팹 사용).
        /// </summary>
        /// <param name="registry">대상 레지스트리.</param>
        /// <param name="kind">스폰 종류.</param>
        private static void DrawSpawnRow(MapPrefabRegistrySO registry, SpawnKind kind)
        {
            int index = FindSpawnIndex(registry, kind);
            NetworkObject current = index >= 0 ? registry.SpawnPrefabs[index].Prefab : null;
            int variantCount = CountVariants(registry, index);

            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            var next = (NetworkObject)EditorGUILayout.ObjectField(kind.ToString(), current, typeof(NetworkObject), false);
            if (EditorGUI.EndChangeCheck())
            {
                SetSpawnPrefab(registry, kind, next);
                index = FindSpawnIndex(registry, kind);
            }

            bool expanded = spawnVariantFoldout.Contains(kind);
            string badge = variantCount > 0 ? $"변종 {variantCount}종 ▾" : "변종 + ▾";
            Color previous = GUI.color;
            GUI.color = variantCount > 0 ? new Color(0.7f, 0.9f, 1f) : previous;
            if (GUILayout.Button(badge, EditorStyles.miniButton, GUILayout.Width(74f)))
            {
                if (expanded)
                {
                    spawnVariantFoldout.Remove(kind);
                }
                else
                {
                    spawnVariantFoldout.Add(kind);
                }
            }

            GUI.color = previous;
            EditorGUILayout.EndHorizontal();

            if (!expanded)
            {
                return;
            }

            if (index < 0)
            {
                EditorGUILayout.HelpBox("기본 프리팹을 먼저 할당해야 변종을 등록할 수 있다", MessageType.Info);
                return;
            }

            DrawVariantList(registry, registry.SpawnPrefabs[index]);
        }

        /// <summary>
        /// 한 스폰 종류의 변종 풀을 그린다 — 슬롯 목록 + 추가/제거. 비어 있으면 기본 프리팹이 그대로 쓰인다.
        /// </summary>
        /// <param name="registry">대상 레지스트리(Undo·Dirty 대상).</param>
        /// <param name="entry">대상 스폰 항목.</param>
        private static void DrawVariantList(MapPrefabRegistrySO registry, SpawnPrefabEntry entry)
        {
            EditorGUI.indentLevel++;
            var variants = new List<NetworkObject>(entry.Variants ?? new NetworkObject[0]);
            for (int i = 0; i < variants.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUI.BeginChangeCheck();
                var next = (NetworkObject)EditorGUILayout.ObjectField($"변종 {i}", variants[i], typeof(NetworkObject), false);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(registry, "변종 변경");
                    variants[i] = next;
                    entry.Variants = variants.ToArray();
                    EditorUtility.SetDirty(registry);
                }

                if (GUILayout.Button("−", EditorStyles.miniButton, GUILayout.Width(22f)))
                {
                    Undo.RecordObject(registry, "변종 제거");
                    variants.RemoveAt(i);
                    entry.Variants = variants.ToArray();
                    EditorUtility.SetDirty(registry);
                    EditorGUILayout.EndHorizontal();
                    break;
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(EditorGUI.indentLevel * 15f);
            if (GUILayout.Button("+ 변종 추가", EditorStyles.miniButton, GUILayout.Width(96f)))
            {
                Undo.RecordObject(registry, "변종 추가");
                variants.Add(null);
                entry.Variants = variants.ToArray();
                EditorUtility.SetDirty(registry);
            }

            if (variants.Count > 0)
            {
                GUILayout.Label("변종이 1개 이상이면 기본 대신 변종 풀에서 뽑는다", EditorStyles.miniLabel);
            }

            EditorGUILayout.EndHorizontal();
            EditorGUI.indentLevel--;
        }

        /// <summary>스폰 항목의 유효(비 null) 변종 수를 센다.</summary>
        /// <param name="registry">대상 레지스트리.</param>
        /// <param name="index">스폰 항목 인덱스(-1 = 미등재).</param>
        /// <returns>유효 변종 수.</returns>
        private static int CountVariants(MapPrefabRegistrySO registry, int index)
        {
            if (index < 0 || registry.SpawnPrefabs[index].Variants == null)
            {
                return 0;
            }

            int count = 0;
            NetworkObject[] variants = registry.SpawnPrefabs[index].Variants;
            for (int i = 0; i < variants.Length; i++)
            {
                if (variants[i] != null)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// 열쇠·자물쇠 페어 섹션 — 한 줄에 같은 번호의 열쇠와 자물쇠를 나란히 놓는다(인덱스 + 1 = 페어 번호).
        /// 두 배열이 따로면 한쪽 삽입·삭제로 열쇠_3 ↔ 자물쇠_4 가 되는데, 한 항목으로 묶으면 그 사고가 불가능하다.
        /// 최대 쌍 수(5)까지는 항상 행을 채워 보여주고, 한쪽만 비면 그 줄에서 바로 경고한다.
        /// </summary>
        /// <param name="registry">대상 레지스트리.</param>
        private static void DrawPairSection(MapPrefabRegistrySO registry)
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("열쇠·자물쇠 페어 (인덱스 + 1 = 페어 번호)", EditorStyles.miniBoldLabel);

            var pairs = new List<PairPrefabEntry>(registry.PairPrefabs ?? new PairPrefabEntry[0]);
            bool changed = false;
            while (pairs.Count < maxLockPairs)
            {
                pairs.Add(new PairPrefabEntry());
                changed = true;
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("번호", EditorStyles.miniLabel, GUILayout.Width(34f));
            GUILayout.Label("열쇠", EditorStyles.miniLabel);
            GUILayout.Label("자물쇠 (DoorLockFace 루트)", EditorStyles.miniLabel);
            GUILayout.Space(26f);
            EditorGUILayout.EndHorizontal();

            int removeAt = -1;
            for (int i = 0; i < pairs.Count; i++)
            {
                PairPrefabEntry pair = pairs[i];
                bool halfEmpty = (pair.Key == null) != (pair.Lock == null);
                EditorGUILayout.BeginHorizontal();
                Color previous = GUI.color;
                if (halfEmpty)
                {
                    GUI.color = missingTint;
                }

                GUILayout.Label($"{i + 1:00}", EditorStyles.miniBoldLabel, GUILayout.Width(34f));
                EditorGUI.BeginChangeCheck();
                var key = (NetworkObject)EditorGUILayout.ObjectField(pair.Key, typeof(NetworkObject), false);
                var lockObject = (NetworkObject)EditorGUILayout.ObjectField(pair.Lock, typeof(NetworkObject), false);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(registry, "열쇠·자물쇠 페어 변경");
                    pair.Key = key;
                    pair.Lock = lockObject;
                    changed = true;
                }

                GUI.color = previous;
                if (i >= maxLockPairs)
                {
                    if (GUILayout.Button("−", EditorStyles.miniButton, GUILayout.Width(22f)))
                    {
                        removeAt = i;
                    }
                }
                else
                {
                    GUILayout.Space(26f);
                }

                EditorGUILayout.EndHorizontal();

                if (halfEmpty)
                {
                    EditorGUILayout.HelpBox(
                        $"페어 {i + 1:00} 한쪽만 등재 — {(pair.Key == null ? "열쇠가 없어 그 자물쇠를 열 수단이 없다" : "자물쇠가 없어 잠긴 문이 해정 불가가 된다")}",
                        MessageType.Warning);
                }
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("+ 페어 추가", EditorStyles.miniButton, GUILayout.Width(96f)))
            {
                Undo.RecordObject(registry, "페어 추가");
                pairs.Add(new PairPrefabEntry());
                changed = true;
            }

            GUILayout.Label($"자물쇠는 최대 {maxLockPairs}개까지 생성된다", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();

            if (removeAt >= 0)
            {
                Undo.RecordObject(registry, "페어 제거");
                pairs.RemoveAt(removeAt);
                changed = true;
            }

            if (changed)
            {
                registry.PairPrefabs = pairs.ToArray();
                EditorUtility.SetDirty(registry);
            }
        }

        /// <summary>같은 SpawnKind 의 중복 항목(첫 항목 이후)을 나열하고 개별 제거 버튼을 제공한다.</summary>
        /// <param name="registry">대상 레지스트리.</param>
        private static void DrawDuplicateSpawnEntries(MapPrefabRegistrySO registry)
        {
            List<int> duplicates = DuplicateSpawnIndices(registry);
            if (duplicates.Count == 0)
            {
                return;
            }

            EditorGUILayout.HelpBox($"중복 스폰 항목 {duplicates.Count}건 — 첫 항목만 조회에 쓰인다", MessageType.Warning);
            foreach (int i in duplicates)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.TextField(registry.SpawnPrefabs[i].Kind.ToString());
                EditorGUILayout.ObjectField(registry.SpawnPrefabs[i].Prefab, typeof(NetworkObject), false);
                EditorGUI.EndDisabledGroup();
                if (GUILayout.Button("제거", GUILayout.Width(48)))
                {
                    RemoveSpawnEntry(registry, i);
                    EditorGUILayout.EndHorizontal();
                    return; // 배열이 변형됐다 — 다음 리페인트에서 재계산
                }

                EditorGUILayout.EndHorizontal();
            }
        }

        /// <summary>SpawnKind 로 스폰 항목 인덱스를 찾는다.</summary>
        /// <param name="registry">대상 레지스트리.</param>
        /// <param name="kind">스폰 종류.</param>
        /// <returns>첫 일치 인덱스, 없으면 -1.</returns>
        private static int FindSpawnIndex(MapPrefabRegistrySO registry, SpawnKind kind)
        {
            if (registry.SpawnPrefabs == null)
            {
                return -1;
            }

            for (int i = 0; i < registry.SpawnPrefabs.Length; i++)
            {
                if (registry.SpawnPrefabs[i].Kind == kind)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>스폰 프리팹을 설정한다 — 할당 시 항목 추가/갱신, null 해제 시 항목 제거.</summary>
        /// <param name="registry">대상 레지스트리.</param>
        /// <param name="kind">스폰 종류.</param>
        /// <param name="prefab">할당 프리팹.</param>
        private static void SetSpawnPrefab(MapPrefabRegistrySO registry, SpawnKind kind, NetworkObject prefab)
        {
            int index = FindSpawnIndex(registry, kind);
            if (prefab == null)
            {
                if (index >= 0)
                {
                    RemoveSpawnEntry(registry, index);
                }

                return;
            }

            Undo.RecordObject(registry, "스폰 프리팹 변경");
            if (index >= 0)
            {
                registry.SpawnPrefabs[index].Prefab = prefab;
            }
            else
            {
                var list = new List<SpawnPrefabEntry>(registry.SpawnPrefabs ?? new SpawnPrefabEntry[0])
                {
                    new SpawnPrefabEntry { Kind = kind, Prefab = prefab },
                };
                registry.SpawnPrefabs = list.ToArray();
            }

            EditorUtility.SetDirty(registry);
        }

        /// <summary>스폰 항목을 인덱스로 제거한다.</summary>
        /// <param name="registry">대상 레지스트리.</param>
        /// <param name="index">제거 인덱스.</param>
        private static void RemoveSpawnEntry(MapPrefabRegistrySO registry, int index)
        {
            Undo.RecordObject(registry, "스폰 항목 제거");
            var list = new List<SpawnPrefabEntry>(registry.SpawnPrefabs);
            list.RemoveAt(index);
            registry.SpawnPrefabs = list.ToArray();
            EditorUtility.SetDirty(registry);
        }

        /// <summary>같은 SpawnKind 가 중복된 항목(첫 항목 이후)의 인덱스 목록을 만든다.</summary>
        /// <param name="registry">대상 레지스트리.</param>
        /// <returns>중복 항목 인덱스 목록.</returns>
        private static List<int> DuplicateSpawnIndices(MapPrefabRegistrySO registry)
        {
            var result = new List<int>();
            if (registry.SpawnPrefabs == null)
            {
                return result;
            }

            var seen = new HashSet<SpawnKind>();
            for (int i = 0; i < registry.SpawnPrefabs.Length; i++)
            {
                if (!seen.Add(registry.SpawnPrefabs[i].Kind))
                {
                    result.Add(i);
                }
            }

            return result;
        }

        /// <summary>프리팹이 등재되지 않은 SpawnKind 수를 센다.</summary>
        /// <param name="registry">대상 레지스트리.</param>
        /// <returns>미등재 종류 수.</returns>
        private static int CountUnassignedSpawnKinds(MapPrefabRegistrySO registry)
        {
            int count = 0;
            foreach ((string label, SpawnKind[] kinds) group in spawnGroups)
            {
                foreach (SpawnKind kind in group.kinds)
                {
                    int index = FindSpawnIndex(registry, kind);
                    if (index < 0 || registry.SpawnPrefabs[index].Prefab == null)
                    {
                        count++;
                    }
                }
            }

            return count;
        }
    }
}
