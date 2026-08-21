using UnityEngine;

namespace EmptyHouse.Environment
{
    /// <summary>조명 픽스처 종류 — 프로파일에서 기본 색을 찾는 키.</summary>
    public enum LightFixtureKind
    {
        CeilingBulb,
        Chandelier,
        WallLight,
        FloorLamp,
        DeskLamp,
        Lantern,
        TV,
        Hallway,
        SafeZone
    }

    /// <summary>방 조명 테마 — 프로파일에서 틴트를 찾는 키. Inherit는 바인더 전용(부모 테마를 따른다).</summary>
    public enum LightThemeKind
    {
        Inherit = -1,
        Default = 0,
        Book,
        Warehouse,
        Pillar,
        Hospital,
        Entrance,
        Hallway,
        SafeZone
    }

    /// <summary>
    /// 라이트 색을 <see cref="LightingProfileSO"/>에서 받아 적용하는 컴포넌트.
    /// 픽스처 종류 × 방 테마로 색이 결정되며, 테마는 부모의 <see cref="RoomLightTheme"/>에서 읽는다.
    /// 편집 모드에서도 동작하므로(ExecuteAlways) 라이트맵을 구울 때 프로파일 색이 그대로 구워진다.
    /// 강도·범위·깜빡임은 건드리지 않는다(프리팹 값 유지).
    /// </summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(-200)] // FlickeringLight가 Awake에서 기준 강도를 캐시하기 전에 적용
    [RequireComponent(typeof(Light))]
    public sealed class LightProfileBinder : MonoBehaviour
    {
        [Header("Profile")]
        [SerializeField] private LightingProfileSO profile;                       // 색 프로파일(프로젝트 에셋)
        [SerializeField] private LightFixtureKind kind = LightFixtureKind.CeilingBulb; // 픽스처 종류
        [SerializeField] private LightThemeKind themeOverride = LightThemeKind.Inherit; // 부모 테마 대신 강제할 테마

        private Light targetLight; // 대상 라이트(자기 자신)

        /// <summary>
        /// 라이트를 캐시하고 프로파일 변경 구독을 건 뒤 색을 즉시 적용한다.
        /// </summary>
        private void OnEnable()
        {
            targetLight = GetComponent<Light>();
            LightingProfileSO.Changed += ApplyProfile;
            ApplyProfile();
        }

        /// <summary>
        /// 프로파일 변경 구독을 해제한다.
        /// </summary>
        private void OnDisable()
        {
            LightingProfileSO.Changed -= ApplyProfile;
        }

        /// <summary>
        /// 프로파일에서 색을 해석해 라이트에 적용한다.
        /// 이미 구워진 Baked/Mixed 라이트는 플레이 중 색을 바꾸면 라이트맵 성분과 어긋나므로 건너뛴다
        /// (색을 바꿨다면 재베이크가 정답이다). 편집 모드에서는 베이크 입력이 되어야 하므로 항상 적용한다.
        /// </summary>
        public void ApplyProfile()
        {
            if (targetLight == null) targetLight = GetComponent<Light>();
#if UNITY_EDITOR
            // 프리팹에 막 붙인 직후(프로파일 할당 전) 편집 모드 한정 예외. 플레이 중엔 NRE로 드러낸다.
            if (!Application.isPlaying && profile == null) return;
#endif

            LightBakingOutput baking = targetLight.bakingOutput;
            if (Application.isPlaying && baking.isBaked && baking.lightmapBakeType != LightmapBakeType.Realtime) return;

            targetLight.color = profile.Resolve(kind, ResolveTheme());
        }

        /// <summary>
        /// 적용할 테마를 결정한다: 오버라이드가 있으면 그것, 없으면 부모의 <see cref="RoomLightTheme"/>,
        /// 그마저 없으면 Default.
        /// </summary>
        /// <returns>적용할 테마.</returns>
        private LightThemeKind ResolveTheme()
        {
            if (themeOverride != LightThemeKind.Inherit) return themeOverride;

            RoomLightTheme room = GetComponentInParent<RoomLightTheme>(true);
            if (room == null || room.Theme == LightThemeKind.Inherit) return LightThemeKind.Default;
            return room.Theme;
        }

#if UNITY_EDITOR
        /// <summary>
        /// 컴포넌트를 처음 붙일 때 프로젝트의 프로파일 에셋을 자동 연결한다(에디터 편의).
        /// </summary>
        private void Reset()
        {
            string[] guids = UnityEditor.AssetDatabase.FindAssets("t:LightingProfileSO");
            if (guids.Length == 0) return;
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
            profile = UnityEditor.AssetDatabase.LoadAssetAtPath<LightingProfileSO>(path);
        }

        /// <summary>
        /// 인스펙터에서 종류·테마를 바꾸면 즉시 반영한다.
        /// </summary>
        private void OnValidate()
        {
            if (isActiveAndEnabled && profile != null) ApplyProfile();
        }
#endif
    }
}
