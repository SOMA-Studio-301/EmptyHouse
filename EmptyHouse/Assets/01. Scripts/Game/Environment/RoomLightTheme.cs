using UnityEngine;

namespace EmptyHouse.Environment
{
    /// <summary>
    /// 방 프리팹 루트에 붙는 조명 테마 마커.
    /// 자식의 <see cref="LightProfileBinder"/>들이 이 값을 읽어 테마 틴트를 적용한다.
    /// 방 하나에 한 번만 지정하면 그 안의 모든 조명이 따라온다.
    /// </summary>
    public sealed class RoomLightTheme : MonoBehaviour
    {
        [SerializeField] private LightThemeKind theme = LightThemeKind.Default; // 이 방의 조명 테마

        /// <summary>이 방의 조명 테마.</summary>
        public LightThemeKind Theme => theme;

#if UNITY_EDITOR
        /// <summary>
        /// 인스펙터에서 테마를 바꾸면 자식 조명에 즉시 반영한다.
        /// </summary>
        private void OnValidate()
        {
            LightProfileBinder[] binders = GetComponentsInChildren<LightProfileBinder>(true);
            for (int i = 0; i < binders.Length; i++) binders[i].ApplyProfile();
        }
#endif
    }
}
