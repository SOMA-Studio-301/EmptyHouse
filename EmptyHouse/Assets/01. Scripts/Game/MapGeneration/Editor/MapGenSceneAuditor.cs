using System.Collections.Generic;
using Border.Core;
using EmptyHouse.MapGen.Core;
using UnityEditor;
using UnityEngine;

namespace EmptyHouse.MapGen.Editor
{
    /// <summary>
    /// 생성 맵 기하 감사 — 조립된 씬을 블루프린트와 대조해 병합 결함을 자동 검출한다.
    /// 검출 항목: ① 간선 기하 불일치·낭떠러지(바닥 없음) ② 개구 막힘(전고 벽 잔존 = 절단 실패)
    /// ③ 복도 봉인 벽 누락 ④ 개구 경계 슬릿(미충진 갭) ⑤ 개구 상단 미충진(높은 벽 초과분).
    /// 문제마다 콘솔 [Audit] 라인(시드·간선·셀 좌표) + 씬 AuditMarkers 아래 빨간 구체를 남긴다 —
    /// 버그 리포트는 이 로그 라인을 그대로 전달하면 시드 단위로 재현된다.
    /// </summary>
    public static class MapGenSceneAuditor
    {
        private const float cell = PrefabRoomTemplates.CellMeters; // 셀 실측(m)
        private const float slitTolerance = 0.07f; // 이하 갭은 무시(프리팹 이음 허용 오차)

        /// <summary>
        /// 현재 씬의 GeneratedMaps 전체를 감사한다 — 맵 이름의 시드로 블루프린트를 결정론 재생성해 대조.
        /// </summary>
        [MenuItem("Tools/Map/절차 맵 감사(현재 씬)")]
        public static void AuditActiveScene()
        {
            Log.D("[MapGenSceneAuditor] AuditActiveScene");
            GameObject root = null;
            foreach (GameObject sceneRoot in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (sceneRoot.name == "GeneratedMaps")
                {
                    root = sceneRoot;
                }
            }

            if (root == null)
            {
                Log.W("[MapGenSceneAuditor] GeneratedMaps 루트가 없다 — 먼저 '절차 예시 맵 5개 생성' 실행.");
                return;
            }

            var generator = new MapGenerator();
            List<RoomTemplateDef> templates = PrefabRoomTemplates.Create();
            int total = 0;
            for (int i = 0; i < root.transform.childCount; i++)
            {
                GameObject map = root.transform.GetChild(i).gameObject;
                int seedTagIndex = map.name.LastIndexOf("Seed", System.StringComparison.Ordinal);
                if (seedTagIndex < 0 || !int.TryParse(map.name.Substring(seedTagIndex + 4), out int seed))
                {
                    Log.W($"[MapGenSceneAuditor] '{map.name}' — 이름에서 시드를 읽지 못해 건너뛴다.");
                    continue;
                }

                MapGenResult result = generator.Generate(MapGenSceneBuilder.CreateParams(seed), templates);
                if (!result.Success)
                {
                    Log.E($"[MapGenSceneAuditor] 시드 {seed} 재생성 실패 — 씬과 코드 버전이 다르다(생성기 버전 확인).");
                    continue;
                }

                total += AuditMap(map, result.Blueprint, templates, i);
            }

            if (total > 0)
            {
                Log.W($"[MapGenSceneAuditor] 감사 완료 — 문제 {total}건. 콘솔 [Audit] 라인과 씬 AuditMarkers(빨간 구체)를 확인.");
            }
            else
            {
                Log.D("[MapGenSceneAuditor] 감사 완료 — 문제 0건.");
            }
        }

        /// <summary>
        /// 맵 하나를 블루프린트와 대조 감사한다. 기존 AuditMarkers 는 지우고 다시 만든다.
        /// </summary>
        /// <param name="mapRoot">조립된 맵 루트.</param>
        /// <param name="blueprint">이 맵의 블루프린트(같은 시드 재생성).</param>
        /// <param name="templates">템플릿 목록.</param>
        /// <param name="mapIndex">맵 번호(로그용).</param>
        /// <returns>검출한 문제 수.</returns>
        public static int AuditMap(GameObject mapRoot, MapBlueprint blueprint, IReadOnlyList<RoomTemplateDef> templates, int mapIndex)
        {
            Transform oldMarkers = mapRoot.transform.Find("AuditMarkers");
            if (oldMarkers != null)
            {
                Object.DestroyImmediate(oldMarkers.gameObject);
            }

            var markersRoot = new GameObject("AuditMarkers");
            markersRoot.transform.SetParent(mapRoot.transform, false);

            int minX = int.MaxValue;
            int minY = int.MaxValue;
            for (int r = 0; r < blueprint.Rooms.Count; r++)
            {
                minX = Mathf.Min(minX, blueprint.Rooms[r].Cell.X);
                minY = Mathf.Min(minY, blueprint.Rooms[r].Cell.Y);
            }

            Vector3 origin = mapRoot.transform.position;
            float floorY = origin.y;
            Renderer[] renderers = mapRoot.GetComponentsInChildren<Renderer>(true);
            int seed = blueprint.Meta.Seed;
            int issues = 0;

            for (int e = 0; e < blueprint.Edges.Count; e++)
            {
                BlueprintEdge edge = blueprint.Edges[e];
                RoomTemplateDef templateA = FindTemplate(templates, blueprint.Rooms[edge.RoomA].TemplateId);
                SocketDef socketA = FindSocket(templateA, edge.SocketA);
                CellCoord worldCell = CellMath.WorldCell(blueprint.Rooms[edge.RoomA], templateA, socketA.LocalCell);
                SocketDirection dir = CellMath.RotateDirection(socketA.Direction, blueprint.Rooms[edge.RoomA].Rotation);
                int cx = worldCell.X - minX;
                int cy = worldCell.Y - minY;

                if (edge.RoomB < 0)
                {
                    // 봉인 소켓 — 복도만 물리 봉인 벽이 필요하다(방 봉인은 원래 벽이 남아 있음)
                    if (templateA.IsCorridor && edge.State == EdgeState.BlockedWall)
                    {
                        issues += AuditCorridorSeal(mapRoot, edge, e, cx, cy, dir, origin, markersRoot.transform, mapIndex, seed);
                    }

                    continue;
                }

                if (edge.State == EdgeState.BlockedWall)
                {
                    continue;
                }

                issues += AuditOpening(mapRoot, blueprint, templates, edge, e, cx, cy, dir, origin, floorY, renderers, markersRoot.transform, mapIndex, seed, minX, minY);
            }

            if (issues > 0)
            {
                Log.W($"[Audit][맵{mapIndex} 시드{seed}] 문제 {issues}건 — AuditMarkers 구체 위치 참조.");
            }

            return issues;
        }

        /// <summary>
        /// 연결 간선(문·통로) 하나의 개구 기하를 감사한다 — 대면 불일치·바닥 유무·벽 잔존·슬릿·상단 미충진.
        /// </summary>
        /// <returns>검출한 문제 수.</returns>
        private static int AuditOpening(GameObject mapRoot, MapBlueprint blueprint, IReadOnlyList<RoomTemplateDef> templates, BlueprintEdge edge, int edgeIndex, int cx, int cy, SocketDirection dir, Vector3 origin, float floorY, Renderer[] renderers, Transform markersRoot, int mapIndex, int seed, int minX, int minY)
        {
            int issues = 0;
            string where = $"e{edgeIndex} 방{edge.RoomA}s{edge.SocketA}↔방{edge.RoomB}s{edge.SocketB} @셀({cx},{cy})";

            // 경계 기하 — 셀 경계선 위치와 축
            bool boundaryAlongX = dir == SocketDirection.North || dir == SocketDirection.South;
            float axisMin = boundaryAlongX ? origin.x + cx * cell : origin.z + cy * cell;
            float axisMax = axisMin + cell;
            float boundaryPerp = dir switch
            {
                SocketDirection.North => origin.z + (cy + 1) * cell,
                SocketDirection.South => origin.z + cy * cell,
                SocketDirection.East => origin.x + (cx + 1) * cell,
                _ => origin.x + cx * cell,
            };
            Vector3 gateCenter = boundaryAlongX
                ? new Vector3((axisMin + axisMax) * 0.5f, floorY, boundaryPerp)
                : new Vector3(boundaryPerp, floorY, (axisMin + axisMax) * 0.5f);

            // ① 소켓 대면 검증(데이터) — 빌더 가드와 동일 기준
            RoomTemplateDef templateB = FindTemplate(templates, blueprint.Rooms[edge.RoomB].TemplateId);
            SocketDef socketB = FindSocket(templateB, edge.SocketB);
            CellCoord worldCellB = CellMath.WorldCell(blueprint.Rooms[edge.RoomB], templateB, socketB.LocalCell);
            RoomTemplateDef templateA = FindTemplate(templates, blueprint.Rooms[edge.RoomA].TemplateId);
            SocketDef socketA = FindSocket(templateA, edge.SocketA);
            CellCoord worldCellA = CellMath.WorldCell(blueprint.Rooms[edge.RoomA], templateA, socketA.LocalCell);
            CellCoord facing = Step(worldCellA, dir);
            SocketDirection dirB = CellMath.RotateDirection(socketB.Direction, blueprint.Rooms[edge.RoomB].Rotation);
            if (worldCellB.X != facing.X || worldCellB.Y != facing.Y || dirB != Opposite(dir))
            {
                issues++;
                Report(markersRoot, gateCenter + Vector3.up * 2.5f, $"기하불일치", edgeIndex, mapIndex, seed, where, "소켓 쌍이 셀·방향 대면하지 않는다(빌더가 개구를 생략했을 것)");
            }

            // ② 낭떠러지 — 양쪽 소켓 셀 중심에 활성 바닥이 있어야 한다
            Vector3 floorPointA = CellCenter(worldCellA, blueprint, origin);
            Vector3 floorPointB = CellCenter(worldCellB, blueprint, origin);
            if (!HasFloorAt(renderers, floorPointA, floorY))
            {
                issues++;
                Report(markersRoot, floorPointA + Vector3.up * 1.5f, "바닥없음", edgeIndex, mapIndex, seed, where, $"A측 셀에 바닥 없음(낭떠러지) @({floorPointA.x:0.0},{floorPointA.z:0.0})");
            }

            if (!HasFloorAt(renderers, floorPointB, floorY))
            {
                issues++;
                Report(markersRoot, floorPointB + Vector3.up * 1.5f, "바닥없음", edgeIndex, mapIndex, seed, where, $"B측 셀에 바닥 없음(낭떠러지) @({floorPointB.x:0.0},{floorPointB.z:0.0})");
            }

            // 개구 스팬 — 문이면 문틀 실측, 통로면 셀 중심 4m 슬롯
            float openMin;
            float openMax;
            float openTop;
            if (edge.State == EdgeState.OpenPassage)
            {
                float center = (axisMin + axisMax) * 0.5f;
                openMin = center - 2f;
                openMax = center + 2f;
                openTop = floorY + 6f;
            }
            else
            {
                Transform door = FindDoor(mapRoot, edgeIndex);
                if (door == null)
                {
                    issues++;
                    Report(markersRoot, gateCenter + Vector3.up * 2.5f, "문누락", edgeIndex, mapIndex, seed, where, "문 상태 간선인데 문 프리팹이 없다");
                    return issues;
                }

                Bounds frame = MapGenSceneBuilder.FrameBounds(door.gameObject);
                openMin = boundaryAlongX ? frame.min.x : frame.min.z;
                openMax = boundaryAlongX ? frame.max.x : frame.max.z;
                openTop = frame.max.y;
            }

            // 이 간선의 두 방 풋프린트(월드 XZ) — 벽 최고점·개구막힘은 이 안의 벽만 센다.
            // 같은 벽 평면에 이웃한 제3의 방(예: 8m 입구방) 벽이 끼어들어 오탐을 만드는 것을 막는다
            Rect rectA = FootprintRect(blueprint.Rooms[edge.RoomA], templateA, origin, minX, minY);
            Rect rectB = FootprintRect(blueprint.Rooms[edge.RoomB], templateB, origin, minX, minY);

            // 경계 평면의 활성 렌더러 수집(스폰·감사 표식 제외, 낮은 걸레받이 제외)
            var covered = new List<(float min, float max)>();
            var planeWallTops = new List<float>();
            float segMin = axisMin - 1f;
            float segMax = axisMax + 1f;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (!renderers[i].gameObject.activeInHierarchy)
                {
                    continue;
                }

                string name = renderers[i].name;
                if (name.StartsWith("Spawn_") || name.StartsWith("Audit_") || name.StartsWith("Hall_Door_L") || name.StartsWith("Hall_Door_R"))
                {
                    continue;
                }

                Bounds b = renderers[i].bounds;
                float perpLo = boundaryAlongX ? b.min.z : b.min.x;
                float perpHi = boundaryAlongX ? b.max.z : b.max.x;
                if (perpHi < boundaryPerp - 0.85f || perpLo > boundaryPerp + 0.85f)
                {
                    continue;
                }

                float lo = boundaryAlongX ? b.min.x : b.min.z;
                float hi = boundaryAlongX ? b.max.x : b.max.z;
                if (hi < segMin || lo > segMax || b.max.y < floorY + 2f)
                {
                    continue;
                }

                // 벽 최고점은 이 간선 두 방 소유의 벽 조각만 수집(2m 상단 조각이 8m 를 만든다) — ⑤ 검사 기준
                if (name.Contains("Wall") && (InRect(rectA, b.center) || InRect(rectB, b.center)))
                {
                    planeWallTops.Add(b.max.y);
                    if (b.size.y >= 4f)
                    {
                        // ③ 개구 막힘 — 전고 벽이 개구 스팬 한복판을 여전히 막고 있다(절단 실패/빌더 생략)
                        float overlap = Mathf.Min(hi, openMax - 0.3f) - Mathf.Max(lo, openMin + 0.3f);
                        if (overlap > 0.6f)
                        {
                            issues++;
                            Report(markersRoot, gateCenter + Vector3.up * 3f, "개구막힘", edgeIndex, mapIndex, seed, where, $"전고 벽 '{name}' 이 개구를 {overlap:0.0}m 막고 있다(미병합)");
                        }
                    }
                }

                // 슬릿 커버리지는 바닥면(floorY+2) 평면을 가로지르는 조각만 — 천장·상단 조각이
                // 지상 관통 갭을 가리는 오판(마스킹)을 막는다
                if (b.min.y > floorY + 2f)
                {
                    continue;
                }

                covered.Add((lo, hi));
            }

            // ④ 슬릿 — 경계 구간에서 [활성 렌더러 ∪ 개구 스팬] 이 못 덮는 갭
            covered.Add((openMin, openMax));
            covered.Sort((a, b) => a.min.CompareTo(b.min));
            float cursor = segMin;
            for (int i = 0; i < covered.Count; i++)
            {
                if (covered[i].min - cursor > slitTolerance && cursor >= axisMin - 0.5f && covered[i].min <= axisMax + 0.5f)
                {
                    issues++;
                    float gapCenter = (cursor + covered[i].min) * 0.5f;
                    Vector3 pos = boundaryAlongX
                        ? new Vector3(gapCenter, floorY + 2.5f, boundaryPerp)
                        : new Vector3(boundaryPerp, floorY + 2.5f, gapCenter);
                    Report(markersRoot, pos, "슬릿", edgeIndex, mapIndex, seed, where, $"경계 미충진 갭 {covered[i].min - cursor:0.00}m @축 {gapCenter:0.0}");
                }

                cursor = Mathf.Max(cursor, covered[i].max);
            }

            // ⑤ 상단 미충진 — 이 평면 벽 최고점이 개구 상단보다 높으면 그 밴드가 덮여 있어야 한다(입구방 8m 벽)
            float wallTop = 0f;
            for (int i = 0; i < planeWallTops.Count; i++)
            {
                wallTop = Mathf.Max(wallTop, planeWallTops[i]);
            }

            if (wallTop > openTop + 0.3f)
            {
                float bandY = (openTop + wallTop) * 0.5f;
                for (float axis = openMin + 0.3f; axis <= openMax - 0.3f; axis += 0.5f)
                {
                    // 커버 판정은 경계 평면 창(perp ±0.85) 기준 — 벽·슬래브는 경계선에서 0.2~0.8m 비껴 있다
                    if (!PlaneCovered(renderers, boundaryAlongX, boundaryPerp, axis, bandY))
                    {
                        Vector3 point = boundaryAlongX
                            ? new Vector3(axis, bandY, boundaryPerp)
                            : new Vector3(boundaryPerp, bandY, axis);
                        issues++;
                        Report(markersRoot, point, "상단개구", edgeIndex, mapIndex, seed, where, $"개구 상단 밴드({openTop - floorY:0.0}~{wallTop - floorY:0.0}m) 미충진 @축 {axis:0.0}");
                        break;
                    }
                }
            }

            return issues;
        }

        /// <summary>봉인된 복도 소켓의 물리 봉인 벽 존재를 감사한다.</summary>
        /// <returns>검출한 문제 수(0/1).</returns>
        private static int AuditCorridorSeal(GameObject mapRoot, BlueprintEdge edge, int edgeIndex, int cx, int cy, SocketDirection dir, Vector3 origin, Transform markersRoot, int mapIndex, int seed)
        {
            bool boundaryAlongX = dir == SocketDirection.North || dir == SocketDirection.South;
            float boundaryPerp = dir switch
            {
                SocketDirection.North => origin.z + (cy + 1) * cell,
                SocketDirection.South => origin.z + cy * cell,
                SocketDirection.East => origin.x + (cx + 1) * cell,
                _ => origin.x + cx * cell,
            };
            float axisCenter = boundaryAlongX ? origin.x + (cx + 0.5f) * cell : origin.z + (cy + 0.5f) * cell;
            Vector3 expected = boundaryAlongX
                ? new Vector3(axisCenter, origin.y, boundaryPerp)
                : new Vector3(boundaryPerp, origin.y, axisCenter);

            // 빌더는 4m 개구를 2m 벽 2장으로 막는다 — 2장 미만이면 절반이 뚫린 낭떠러지다
            string sealName = $"SealWall_{edge.RoomA}_{edge.SocketA}";
            int pieces = 0;
            foreach (Transform t in mapRoot.GetComponentsInChildren<Transform>(false))
            {
                if (t.name == sealName && Vector2.Distance(new Vector2(t.position.x, t.position.z), new Vector2(expected.x, expected.z)) < 3f)
                {
                    pieces++;
                }
            }

            if (pieces >= 2)
            {
                return 0;
            }

            string where = $"e{edgeIndex} 방{edge.RoomA}s{edge.SocketA} @셀({cx},{cy})";
            Report(markersRoot, expected + Vector3.up * 2.5f, "봉인누락", edgeIndex, mapIndex, seed, where, $"복도 봉인 벽 {pieces}/2장 — 개구가 뚫린 낭떠러지");
            return 1;
        }

        /// <summary>문제 지점에 빨간 구체 표식을 놓고 [Audit] 로그를 남긴다.</summary>
        private static void Report(Transform markersRoot, Vector3 position, string tag, int edgeIndex, int mapIndex, int seed, string where, string message)
        {
            Log.W($"[Audit][맵{mapIndex} 시드{seed}] {where}: {message}");
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(sphere.GetComponent<Collider>());
            sphere.transform.SetParent(markersRoot, false);
            sphere.transform.position = position;
            sphere.transform.localScale = Vector3.one * 0.9f;
            sphere.name = $"Audit_e{edgeIndex}_{tag}";
            sphere.GetComponent<Renderer>().sharedMaterial = MapGenSceneBuilder.GetOrCreateMaterial("MG_Audit", new Color(0.95f, 0.08f, 0.08f));
        }

        /// <summary>
        /// 경계 평면 창(perp ±0.85) 안의 활성 렌더러가 (경계 축, 높이) 지점을 덮는지 검사한다 —
        /// 벽·충진 슬래브는 경계선에서 프리팹별로 0.2~0.8m 비껴 있어 점 검사로는 오판한다.
        /// </summary>
        private static bool PlaneCovered(Renderer[] renderers, bool boundaryAlongX, float boundaryPerp, float axis, float y)
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                if (!renderers[i].gameObject.activeInHierarchy || renderers[i].name.StartsWith("Spawn_") || renderers[i].name.StartsWith("Audit_"))
                {
                    continue;
                }

                Bounds b = renderers[i].bounds;
                float perpLo = boundaryAlongX ? b.min.z : b.min.x;
                float perpHi = boundaryAlongX ? b.max.z : b.max.x;
                if (perpHi < boundaryPerp - 0.85f || perpLo > boundaryPerp + 0.85f)
                {
                    continue;
                }

                float lo = boundaryAlongX ? b.min.x : b.min.z;
                float hi = boundaryAlongX ? b.max.x : b.max.z;
                if (axis >= lo - 0.05f && axis <= hi + 0.05f && y >= b.min.y - 0.05f && y <= b.max.y + 0.05f)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>지점(XZ)에 활성 바닥(이름에 Floor)이 있는지 검사한다 — 낭떠러지 판정.</summary>
        private static bool HasFloorAt(Renderer[] renderers, Vector3 point, float floorY)
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                if (!renderers[i].gameObject.activeInHierarchy || !renderers[i].name.Contains("Floor"))
                {
                    continue;
                }

                Bounds b = renderers[i].bounds;
                if (b.max.y > floorY + 1.5f)
                {
                    continue; // 바닥이 아니라 높은 조형물
                }

                if (point.x >= b.min.x - 0.05f && point.x <= b.max.x + 0.05f
                    && point.z >= b.min.z - 0.05f && point.z <= b.max.z + 0.05f)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>정규화 전 전역 셀의 월드 셀 중심(XZ, Y = 맵 원점).</summary>
        private static Vector3 CellCenter(CellCoord worldCell, MapBlueprint blueprint, Vector3 origin)
        {
            int minX = int.MaxValue;
            int minY = int.MaxValue;
            for (int r = 0; r < blueprint.Rooms.Count; r++)
            {
                minX = Mathf.Min(minX, blueprint.Rooms[r].Cell.X);
                minY = Mathf.Min(minY, blueprint.Rooms[r].Cell.Y);
            }

            return origin + new Vector3((worldCell.X - minX + 0.5f) * cell, 0f, (worldCell.Y - minY + 0.5f) * cell);
        }

        /// <summary>간선 인덱스로 문 인스턴스를 찾는다(이름 Door_e{N}_ 프리픽스).</summary>
        private static Transform FindDoor(GameObject mapRoot, int edgeIndex)
        {
            string prefix = $"Door_e{edgeIndex}_";
            foreach (Transform t in mapRoot.GetComponentsInChildren<Transform>(false))
            {
                if (t.name.StartsWith(prefix, System.StringComparison.Ordinal))
                {
                    return t;
                }
            }

            return null;
        }

        /// <summary>반대 방향(빌더 낭떠러지 가드와 동일 기준).</summary>
        private static SocketDirection Opposite(SocketDirection dir)
        {
            return (SocketDirection)(((int)dir + 2) % 4);
        }

        /// <summary>배치된 방의 월드 XZ 풋프린트 사각형(회전 적용 크기).</summary>
        private static Rect FootprintRect(BlueprintRoom room, RoomTemplateDef template, Vector3 origin, int minX, int minY)
        {
            (int w, int h) = CellMath.RotatedSize(template.WidthCells, template.HeightCells, room.Rotation);
            return new Rect(origin.x + (room.Cell.X - minX) * cell, origin.z + (room.Cell.Y - minY) * cell, w * cell, h * cell);
        }

        /// <summary>월드 지점(XZ)이 사각형 안(0.3m 여유)에 있는지 검사한다.</summary>
        private static bool InRect(Rect rect, Vector3 point)
        {
            return point.x >= rect.xMin - 0.3f && point.x <= rect.xMax + 0.3f
                && point.z >= rect.yMin - 0.3f && point.z <= rect.yMax + 0.3f;
        }

        /// <summary>셀에서 방향으로 한 칸 이동한 셀.</summary>
        private static CellCoord Step(CellCoord cellCoord, SocketDirection dir)
        {
            switch (dir)
            {
                case SocketDirection.North: return new CellCoord(cellCoord.X, cellCoord.Y + 1);
                case SocketDirection.East: return new CellCoord(cellCoord.X + 1, cellCoord.Y);
                case SocketDirection.South: return new CellCoord(cellCoord.X, cellCoord.Y - 1);
                default: return new CellCoord(cellCoord.X - 1, cellCoord.Y);
            }
        }

        /// <summary>TemplateId 로 템플릿을 찾는다.</summary>
        private static RoomTemplateDef FindTemplate(IReadOnlyList<RoomTemplateDef> templates, string templateId)
        {
            for (int t = 0; t < templates.Count; t++)
            {
                if (templates[t].TemplateId == templateId)
                {
                    return templates[t];
                }
            }

            return null;
        }

        /// <summary>템플릿에서 소켓 Id 로 소켓을 찾는다.</summary>
        private static SocketDef FindSocket(RoomTemplateDef template, int socketId)
        {
            for (int s = 0; s < template.Sockets.Length; s++)
            {
                if (template.Sockets[s].Id == socketId)
                {
                    return template.Sockets[s];
                }
            }

            return null;
        }
    }
}
