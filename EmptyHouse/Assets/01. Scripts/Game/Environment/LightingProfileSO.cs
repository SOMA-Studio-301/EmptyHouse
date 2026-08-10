using System;
using Border.Core;
using UnityEngine;

namespace EmptyHouse.Environment
{
    /// <summary>
    /// 맵 조명의 색을 한 곳에서 관리하는 프로파일.
    /// 최종 색 = Lerp(픽스처 기본색, 테마 틴트, 혼합비) × 전역 틴트.
    /// 테마·픽스처 엔트리가 각각 독립이라 테마를 바꿔도 픽스처 간 색 관계가 유지된다.
    /// 값을 바꾸면 <see cref="Changed"/>로 씬의 모든 <see cref="LightProfileBinder"/>가 즉시 재적용한다.
    /// </summary>
    [CreateAssetMenu(fileName = "SO_LightingProfile", menuName = "EmptyHouse/Environment/Lighting Profile")]
    public sealed class LightingProfileSO : ScriptableObject
    {
        /// <summary>픽스처 종류별 기본 색.</summary>
        [Serializable]
        public struct FixtureEntry
        {
            public LightFixtureKind kind; // 픽스처 종류
            public Color baseColor;       // 기본 색
        }

        /// <summary>방 테마별 틴트와 혼합 비율.</summary>
        [Serializable]
        public struct ThemeEntry
        {
            public LightThemeKind theme;         // 방 테마
            public Color tint;                   // 틴트 색
            [Range(0f, 1f)] public float blend;  // 기본 색과의 혼합 비율(0=기본색 그대로)
        }

        [Header("Fixture Base Colors")]
        [SerializeField]
        private FixtureEntry[] fixtures = // 픽스처 종류별 기본 색(초기값은 호러팩 프리팹 색)
        {
            new FixtureEntry { kind = LightFixtureKind.CeilingBulb, baseColor = new Color(0.549f, 0.522f, 0.302f) },
            new FixtureEntry { kind = LightFixtureKind.Chandelier, baseColor = new Color(0.592f, 0.576f, 0.376f) },
            new FixtureEntry { kind = LightFixtureKind.WallLight, baseColor = new Color(0.549f, 0.522f, 0.302f) },
            new FixtureEntry { kind = LightFixtureKind.FloorLamp, baseColor = new Color(0.592f, 0.576f, 0.376f) },
            new FixtureEntry { kind = LightFixtureKind.DeskLamp, baseColor = new Color(0.592f, 0.576f, 0.376f) },
            new FixtureEntry { kind = LightFixtureKind.Lantern, baseColor = new Color(0.640f, 0.520f, 0.300f) },
            new FixtureEntry { kind = LightFixtureKind.TV, baseColor = new Color(0.255f, 0.302f, 0.122f) }
        };

        [Header("Room Theme Tints")]
        [SerializeField]
        private ThemeEntry[] themes = // 방 테마별 틴트(초기값은 기존 MapLightingInstaller 팔레트)
        {
            new ThemeEntry { theme = LightThemeKind.Default, tint = Color.white, blend = 0f },
            new ThemeEntry { theme = LightThemeKind.Book, tint = new Color(1.00f, 0.86f, 0.62f), blend = 0.35f },
            new ThemeEntry { theme = LightThemeKind.Warehouse, tint = new Color(0.55f, 0.72f, 1.00f), blend = 0.40f },
            new ThemeEntry { theme = LightThemeKind.Pillar, tint = new Color(0.55f, 0.72f, 1.00f), blend = 0.30f },
            new ThemeEntry { theme = LightThemeKind.Hospital, tint = new Color(0.55f, 1.00f, 0.72f), blend = 0.50f },
            new ThemeEntry { theme = LightThemeKind.Entrance, tint = new Color(0.55f, 0.72f, 1.00f), blend = 0.30f },
            new ThemeEntry { theme = LightThemeKind.Hallway, tint = new Color(0.42f, 0.58f, 0.95f), blend = 0.45f }
        };

        [Header("Global")]
        [SerializeField] private Color globalTint = Color.white; // 맵 전체 색 보정(곱)

        [Header("Fixture State")]
        [Range(0f, 1f)][SerializeField] private float offChance = 0.25f;     // 소등될 확률(픽스처에서 개별 오버라이드 가능)
        [Range(0f, 1f)][SerializeField] private float flickerChance = 0.25f; // 점등된 것 중 깜빡일 확률

        /// <summary>소등될 확률.</summary>
        public float OffChance => offChance;

        /// <summary>점등된 것 중 깜빡일 확률.</summary>
        public float FlickerChance => flickerChance;

        [Header("Culling")]
        [SerializeField] private float cullUpdateInterval = 0.4f; // 컬링 갱신 주기(초)
        [SerializeField] private float cullEnableRadius = 18f;    // 조명을 켜는 반경(m)
        [SerializeField] private float cullDisableRadius = 24f;   // 조명을 끄는 반경(m) — 켜는 반경보다 커야 히스테리시스가 성립
        [SerializeField] private int cullMaxActiveRooms = 12;     // 동시에 조명을 켤 방 수 상한(Forward+ 라이트 한도 안전판)

        /// <summary>컬링 갱신 주기(초).</summary>
        public float CullUpdateInterval => cullUpdateInterval;

        /// <summary>조명을 켜는 반경(m).</summary>
        public float CullEnableRadius => cullEnableRadius;

        /// <summary>조명을 끄는 반경(m).</summary>
        public float CullDisableRadius => cullDisableRadius;

        /// <summary>동시에 조명을 켤 방 수 상한.</summary>
        public int CullMaxActiveRooms => cullMaxActiveRooms;

        /// <summary>프로파일 값이 바뀌었을 때 발생한다(에디터 인스펙터 편집 · 플레이 중 포함).</summary>
        public static event Action Changed;

        /// <summary>
        /// 픽스처 종류와 방 테마로 최종 라이트 색을 계산한다.
        /// 엔트리 수가 10개 안팎이라 선형 탐색으로 충분하다(할당 없음).
        /// </summary>
        /// <param name="kind">픽스처 종류.</param>
        /// <param name="theme">방 테마.</param>
        /// <returns>적용할 라이트 색.</returns>
        public Color Resolve(LightFixtureKind kind, LightThemeKind theme)
        {
            Color result = FindBaseColor(kind);

            for (int i = 0; i < themes.Length; i++)
            {
                if (themes[i].theme != theme) continue;
                result = Color.Lerp(result, themes[i].tint, themes[i].blend);
                break;
            }

            result *= globalTint;
            result.a = 1f;
            return result;
        }

        /// <summary>
        /// 픽스처 종류의 기본 색을 찾는다. 엔트리가 없으면 흰색으로 대체하고 로그를 남긴다.
        /// </summary>
        /// <param name="kind">픽스처 종류.</param>
        /// <returns>기본 색.</returns>
        private Color FindBaseColor(LightFixtureKind kind)
        {
            for (int i = 0; i < fixtures.Length; i++)
                if (fixtures[i].kind == kind) return fixtures[i].baseColor;

            Log.D($"[Lighting] 픽스처 엔트리 없음: {kind} — 흰색으로 대체");
            return Color.white;
        }

#if UNITY_EDITOR
        /// <summary>
        /// 인스펙터 편집 시 씬의 모든 바인더에 즉시 반영한다(플레이 중 톤 조절용).
        /// </summary>
        private void OnValidate()
        {
            Changed?.Invoke();
        }
#endif
    }
}
