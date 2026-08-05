using System.Collections.Generic;
using EmptyHouse.MapGen.Core;
using EmptyHouse.MapGen.Runtime;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

namespace EmptyHouse.EditorTools
{
    /// <summary>
    /// MapPrefabRegistrySO 커스텀 인스펙터 — 카탈로그 TemplateId·SpawnKind 전 종을 고정 행으로 노출해
    /// ID 타이핑 없이 슬롯 할당만으로 설정하게 하고, 누락·중복·CellMeters 불일치(G1)를 즉시 표시한다.
    /// 직렬화 형식은 원본 배열 그대로 유지한다(MapGenRuntimeSetup·소비 컴포넌트 호환).
    /// </summary>
    [CustomEditor(typeof(MapPrefabRegistrySO))]
    public sealed class MapPrefabRegistrySOEditor : Editor
    {
        private static List<RoomTemplateDef> cachedCatalog; // 카탈로그 캐시 — 도메인 리로드까지 유지(Create() 반복 호출·로그 방지)
        private static readonly Color missingTint = new Color(1f, 0.55f, 0.55f); // 필수 슬롯 미할당 강조색

        /// <summary>SpawnKind 표시 그룹 — 인스펙터 행 순서의 단일 원천. HerdArea 는 구역 표지(스폰 오브젝트 아님)라 제외.</summary>
        private static readonly (string label, SpawnKind[] kinds)[] spawnGroups =
        {
            ("좀비", new[] { SpawnKind.ZombieWalker, SpawnKind.ZombieListener, SpawnKind.ZombieWatcher }),
            ("백신", new[] { SpawnKind.VaccineAntigen, SpawnKind.VaccineSerum, SpawnKind.VaccineStabilizer }),
            ("아이템", new[] { SpawnKind.Key, SpawnKind.Fuel, SpawnKind.Scrap, SpawnKind.Throwable }),
            ("설비", new[] { SpawnKind.CorpseStation, SpawnKind.Generator }),
        };

        private const int maxLockPairs = 5; // 자물쇠 최대 쌍 수 — ItemDoorLockCount(2) + ShortcutLockCountMax(3)

        /// <summary>카탈로그 템플릿 목록을 반환한다(최초 1회 생성 후 캐시).</summary>
        /// <returns>런타임 템플릿 목록.</returns>
        private static List<RoomTemplateDef> Catalog()
        {
            if (cachedCatalog == null)
            {
                cachedCatalog = MapTemplateCatalog.Create();
            }

            return cachedCatalog;
        }

        /// <summary>레지스트리 전용 인스펙터를 그린다 — 검증 요약·방·정적·스폰·셀 섹션 순.</summary>
        public override void OnInspectorGUI()
        {
            var registry = (MapPrefabRegistrySO)target;
            DrawSummary(registry);
            EditorGUILayout.Space();
            DrawRoomSection(registry);
            EditorGUILayout.Space();
            DrawStaticSection(registry);
            EditorGUILayout.Space();
            DrawSpawnSection(registry);
            EditorGUILayout.Space();
            DrawCellSection(registry);
        }

        /// <summary>필수 항목 누락·불일치를 집계해 상단 요약 HelpBox 로 표시한다.</summary>
        /// <param name="registry">대상 레지스트리.</param>
        private static void DrawSummary(MapPrefabRegistrySO registry)
        {
            int missingRooms = 0;
            foreach (RoomTemplateDef def in Catalog())
            {
                int index = FindRoomIndex(registry, def.TemplateId);
                if (index < 0 || registry.RoomPrefabs[index].Prefab == null)
                {
                    missingRooms++;
                }
            }

            var problems = new List<string>();
            if (missingRooms > 0)
            {
                problems.Add($"방 프리팹 누락 {missingRooms}종");
            }

            if (registry.SealWallPrefab == null)
            {
                problems.Add("봉인 벽 누락");
            }

            if (registry.CornerColumnPrefab == null)
            {
                problems.Add("기둥 누락");
            }

            if (registry.DoorPrefab == null)
            {
                problems.Add("문 누락");
            }

            if (!Mathf.Approximately(registry.CellMeters, MapTemplateCatalog.CellMeters))
            {
                problems.Add("CellMeters 불일치");
            }

            if (problems.Count == 0)
            {
                EditorGUILayout.HelpBox("필수 항목 모두 등재됨", MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox("미해결: " + string.Join(" · ", problems), MessageType.Warning);
            }
        }

        /// <summary>방/복도 프리팹 섹션 — 카탈로그 TemplateId 고정 행 + 잔존/중복 항목 정리 UI.</summary>
        /// <param name="registry">대상 레지스트리.</param>
        private static void DrawRoomSection(MapPrefabRegistrySO registry)
        {
            EditorGUILayout.LabelField("방/복도 프리팹 — 카탈로그 기준", EditorStyles.boldLabel);
            foreach (RoomTemplateDef def in Catalog())
            {
                int index = FindRoomIndex(registry, def.TemplateId);
                GameObject current = index >= 0 ? registry.RoomPrefabs[index].Prefab : null;
                string suffix = def.IsCorridor ? " · 복도" : def.IsEntranceAnchor ? " · 입구" : "";
                var label = new GUIContent($"{def.TemplateId} ({def.WidthCells}×{def.HeightCells}{suffix})");

                Color prev = GUI.color;
                if (current == null)
                {
                    GUI.color = missingTint;
                }

                EditorGUI.BeginChangeCheck();
                var next = (GameObject)EditorGUILayout.ObjectField(label, current, typeof(GameObject), false);
                GUI.color = prev;
                if (EditorGUI.EndChangeCheck())
                {
                    SetRoomPrefab(registry, def.TemplateId, next);
                }
            }

            DrawOrphanRoomEntries(registry);
        }

        /// <summary>카탈로그에 없는 ID·중복 ID 방 항목을 나열하고 개별 제거 버튼을 제공한다.</summary>
        /// <param name="registry">대상 레지스트리.</param>
        private static void DrawOrphanRoomEntries(MapPrefabRegistrySO registry)
        {
            List<int> orphans = OrphanRoomIndices(registry);
            if (orphans.Count == 0)
            {
                return;
            }

            EditorGUILayout.HelpBox($"카탈로그에 없는/중복 항목 {orphans.Count}건 — 런타임 조회에 쓰이지 않는다", MessageType.Warning);
            foreach (int i in orphans)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.TextField(string.IsNullOrEmpty(registry.RoomPrefabs[i].TemplateId) ? "(빈 ID)" : registry.RoomPrefabs[i].TemplateId);
                EditorGUILayout.ObjectField(registry.RoomPrefabs[i].Prefab, typeof(GameObject), false);
                EditorGUI.EndDisabledGroup();
                if (GUILayout.Button("제거", GUILayout.Width(48)))
                {
                    RemoveRoomEntry(registry, i);
                    EditorGUILayout.EndHorizontal();
                    return; // 배열이 변형됐다 — 다음 리페인트에서 재계산
                }

                EditorGUILayout.EndHorizontal();
            }
        }

        /// <summary>봉인 벽·기둥 등 정적 지오메트리 섹션을 그린다.</summary>
        /// <param name="registry">대상 레지스트리.</param>
        private static void DrawStaticSection(MapPrefabRegistrySO registry)
        {
            EditorGUILayout.LabelField("정적 지오메트리 — 로컬 인스턴스화", EditorStyles.boldLabel);

            Color prev = GUI.color;
            if (registry.SealWallPrefab == null)
            {
                GUI.color = missingTint;
            }

            EditorGUI.BeginChangeCheck();
            var wall = (GameObject)EditorGUILayout.ObjectField("봉인 벽 (SealWall)", registry.SealWallPrefab, typeof(GameObject), false);
            GUI.color = prev;
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(registry, "봉인 벽 변경");
                registry.SealWallPrefab = wall;
                EditorUtility.SetDirty(registry);
            }

            if (registry.CornerColumnPrefab == null)
            {
                GUI.color = missingTint;
            }

            EditorGUI.BeginChangeCheck();
            var column = (GameObject)EditorGUILayout.ObjectField("이음 기둥 (CornerColumn)", registry.CornerColumnPrefab, typeof(GameObject), false);
            GUI.color = prev;
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(registry, "이음 기둥 변경");
                registry.CornerColumnPrefab = column;
                EditorUtility.SetDirty(registry);
            }
        }

        /// <summary>문·SpawnKind 전 종 상태 오브젝트 섹션 — 그룹별 고정 행 + 중복 정리 + 미등재 안내.</summary>
        /// <param name="registry">대상 레지스트리.</param>
        private static void DrawSpawnSection(MapPrefabRegistrySO registry)
        {
            EditorGUILayout.LabelField("상태 오브젝트 — 서버 스폰(NetworkPrefabs 등록 필수)", EditorStyles.boldLabel);

            Color prev = GUI.color;
            if (registry.DoorPrefab == null)
            {
                GUI.color = missingTint;
            }

            EditorGUI.BeginChangeCheck();
            var door = (NetworkObject)EditorGUILayout.ObjectField("문 (DoorPrefab)", registry.DoorPrefab, typeof(NetworkObject), false);
            GUI.color = prev;
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(registry, "문 프리팹 변경");
                registry.DoorPrefab = door;
                EditorUtility.SetDirty(registry);
            }

            if (registry.DoorPrefab == null)
            {
                EditorGUILayout.HelpBox("DoorPrefab 미등재 — 문이 스폰되지 않는다", MessageType.Warning);
            }

            foreach ((string label, SpawnKind[] kinds) group in spawnGroups)
            {
                EditorGUILayout.Space(2f);
                EditorGUILayout.LabelField(group.label, EditorStyles.miniBoldLabel);
                foreach (SpawnKind kind in group.kinds)
                {
                    DrawSpawnRow(registry, kind);
                }
            }

            DrawKeyVariantSection(registry);
            DrawDuplicateSpawnEntries(registry);

            int unassigned = CountUnassignedSpawnKinds(registry);
            if (unassigned > 0)
            {
                EditorGUILayout.HelpBox($"미등재 스폰 종류 {unassigned}종 — 해당 종류는 스폰이 생략된다", MessageType.Info);
            }
        }

        /// <summary>SpawnKind 한 종의 프리팹 슬롯 행을 그린다 — 할당 시 항목 추가, 해제 시 항목 제거.</summary>
        /// <param name="registry">대상 레지스트리.</param>
        /// <param name="kind">스폰 종류.</param>
        private static void DrawSpawnRow(MapPrefabRegistrySO registry, SpawnKind kind)
        {
            int index = FindSpawnIndex(registry, kind);
            NetworkObject current = index >= 0 ? registry.SpawnPrefabs[index].Prefab : null;
            EditorGUI.BeginChangeCheck();
            var next = (NetworkObject)EditorGUILayout.ObjectField(kind.ToString(), current, typeof(NetworkObject), false);
            if (EditorGUI.EndChangeCheck())
            {
                SetSpawnPrefab(registry, kind, next);
            }
        }

        /// <summary>
        /// 열쇠 변종 섹션 — 인덱스 + 1 = 페어 번호(비주얼 구분). 미등재 번호는 공용 Key 프리팹으로 폴백되고,
        /// pairId 는 어느 쪽이든 스포너가 번호로 주입한다.
        /// </summary>
        /// <param name="registry">대상 레지스트리.</param>
        private static void DrawKeyVariantSection(MapPrefabRegistrySO registry)
        {
            EditorGUILayout.Space(2f);
            EditorGUILayout.LabelField("열쇠 변종 (인덱스 + 1 = 페어 번호)", EditorStyles.miniBoldLabel);

            if (registry.KeyPrefabs == null || registry.KeyPrefabs.Length < maxLockPairs)
            {
                // 표시 편의 확장 — 빈 칸은 공용 폴백이라 무해. 실제 저장은 슬롯 할당 시 SetDirty 로 확정된다
                var resized = new NetworkObject[maxLockPairs];
                for (int i = 0; registry.KeyPrefabs != null && i < registry.KeyPrefabs.Length; i++)
                {
                    resized[i] = registry.KeyPrefabs[i];
                }

                registry.KeyPrefabs = resized;
            }

            for (int i = 0; i < registry.KeyPrefabs.Length; i++)
            {
                EditorGUI.BeginChangeCheck();
                var next = (NetworkObject)EditorGUILayout.ObjectField($"열쇠_{i + 1:00}", registry.KeyPrefabs[i], typeof(NetworkObject), false);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(registry, "열쇠 변종 변경");
                    registry.KeyPrefabs[i] = next;
                    EditorUtility.SetDirty(registry);
                }
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

        /// <summary>CellMeters 섹션 — 카탈로그 값(G1)과 불일치 시 에러 표시·원클릭 보정.</summary>
        /// <param name="registry">대상 레지스트리.</param>
        private static void DrawCellSection(MapPrefabRegistrySO registry)
        {
            EditorGUILayout.LabelField("셀 실측", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            float next = EditorGUILayout.FloatField("CellMeters", registry.CellMeters);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(registry, "CellMeters 변경");
                registry.CellMeters = next;
                EditorUtility.SetDirty(registry);
            }

            if (!Mathf.Approximately(registry.CellMeters, MapTemplateCatalog.CellMeters))
            {
                EditorGUILayout.HelpBox($"MapTemplateCatalog.CellMeters({MapTemplateCatalog.CellMeters})와 불일치 — 서버·클라 조립 좌표가 어긋난다(G1)", MessageType.Error);
                if (GUILayout.Button($"카탈로그 값({MapTemplateCatalog.CellMeters})으로 보정"))
                {
                    Undo.RecordObject(registry, "CellMeters 보정");
                    registry.CellMeters = MapTemplateCatalog.CellMeters;
                    EditorUtility.SetDirty(registry);
                }
            }
        }

        /// <summary>TemplateId 로 방 항목 인덱스를 찾는다.</summary>
        /// <param name="registry">대상 레지스트리.</param>
        /// <param name="templateId">템플릿 ID.</param>
        /// <returns>첫 일치 인덱스, 없으면 -1.</returns>
        private static int FindRoomIndex(MapPrefabRegistrySO registry, string templateId)
        {
            if (registry.RoomPrefabs == null)
            {
                return -1;
            }

            for (int i = 0; i < registry.RoomPrefabs.Length; i++)
            {
                if (registry.RoomPrefabs[i].TemplateId == templateId)
                {
                    return i;
                }
            }

            return -1;
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

        /// <summary>방 프리팹을 설정한다 — 항목이 없으면 추가.</summary>
        /// <param name="registry">대상 레지스트리.</param>
        /// <param name="templateId">템플릿 ID.</param>
        /// <param name="prefab">할당 프리팹(null 허용 — 항목 유지, 요약에 누락 표시).</param>
        private static void SetRoomPrefab(MapPrefabRegistrySO registry, string templateId, GameObject prefab)
        {
            Undo.RecordObject(registry, "방 프리팹 변경");
            int index = FindRoomIndex(registry, templateId);
            if (index >= 0)
            {
                registry.RoomPrefabs[index].Prefab = prefab;
            }
            else
            {
                var list = new List<RoomPrefabEntry>(registry.RoomPrefabs ?? new RoomPrefabEntry[0])
                {
                    new RoomPrefabEntry { TemplateId = templateId, Prefab = prefab },
                };
                registry.RoomPrefabs = list.ToArray();
            }

            EditorUtility.SetDirty(registry);
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

        /// <summary>방 항목을 인덱스로 제거한다.</summary>
        /// <param name="registry">대상 레지스트리.</param>
        /// <param name="index">제거 인덱스.</param>
        private static void RemoveRoomEntry(MapPrefabRegistrySO registry, int index)
        {
            Undo.RecordObject(registry, "방 항목 제거");
            var list = new List<RoomPrefabEntry>(registry.RoomPrefabs);
            list.RemoveAt(index);
            registry.RoomPrefabs = list.ToArray();
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

        /// <summary>카탈로그에 없는 ID·빈 ID·중복 ID 방 항목의 인덱스 목록을 만든다.</summary>
        /// <param name="registry">대상 레지스트리.</param>
        /// <returns>잔존/중복 항목 인덱스 목록.</returns>
        private static List<int> OrphanRoomIndices(MapPrefabRegistrySO registry)
        {
            var result = new List<int>();
            if (registry.RoomPrefabs == null)
            {
                return result;
            }

            var catalogIds = new HashSet<string>();
            foreach (RoomTemplateDef def in Catalog())
            {
                catalogIds.Add(def.TemplateId);
            }

            var seen = new HashSet<string>();
            for (int i = 0; i < registry.RoomPrefabs.Length; i++)
            {
                string id = registry.RoomPrefabs[i].TemplateId;
                if (string.IsNullOrEmpty(id) || !catalogIds.Contains(id) || !seen.Add(id))
                {
                    result.Add(i);
                }
            }

            return result;
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
