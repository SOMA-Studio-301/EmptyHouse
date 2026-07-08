using UnityEditor;
using UnityEngine;
using Border.Localization;

namespace Border.Localization.Editor
{
    /// <summary>
    /// UILocalizeText 인스펙터를 prefix / 본문 / suffix 3 단 구성으로 그리는 커스텀 에디터이다.
    /// prefix·suffix 는 토글로 활성화하며, 활성 시에만 키 피커([LocalizeKey] 드로어)가 노출된다.
    /// 정적/동적 조각의 합성 순서 토글과 대상 TMP 필드도 함께 표시한다.
    /// </summary>
    [CustomEditor(typeof(UILocalizeText))]
    [CanEditMultipleObjects]
    public class UILocalizeTextEditor : UnityEditor.Editor
    {
        private SerializedProperty usePrefixProperty;
        private SerializedProperty prefixKeyProperty;
        private SerializedProperty keyProperty;
        private SerializedProperty useSuffixProperty;
        private SerializedProperty suffixKeyProperty;
        private SerializedProperty prefixDynamicFirstProperty;
        private SerializedProperty suffixDynamicFirstProperty;
        private SerializedProperty tmpTextProperty;

        /// <summary>
        /// 인스펙터 활성화 시 직렬화 프로퍼티 참조를 캐시한다.
        /// </summary>
        private void OnEnable()
        {
            usePrefixProperty = serializedObject.FindProperty("usePrefix");
            prefixKeyProperty = serializedObject.FindProperty("prefixKey");
            keyProperty = serializedObject.FindProperty("key");
            useSuffixProperty = serializedObject.FindProperty("useSuffix");
            suffixKeyProperty = serializedObject.FindProperty("suffixKey");
            prefixDynamicFirstProperty = serializedObject.FindProperty("prefixDynamicFirst");
            suffixDynamicFirstProperty = serializedObject.FindProperty("suffixDynamicFirst");
            tmpTextProperty = serializedObject.FindProperty("tmpText");
        }

        /// <summary>
        /// 3 단(prefix / 본문 / suffix) 토글 기반 인스펙터를 렌더링한다.
        /// </summary>
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawAffixRow("Prefix", usePrefixProperty, prefixKeyProperty, prefixDynamicFirstProperty);
            EditorGUILayout.Space(6f);

            EditorGUILayout.LabelField("본문 (Body)", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(keyProperty, GUIContent.none);
            EditorGUILayout.Space(6f);

            DrawAffixRow("Suffix", useSuffixProperty, suffixKeyProperty, suffixDynamicFirstProperty);
            EditorGUILayout.Space(8f);

            EditorGUILayout.PropertyField(tmpTextProperty);

            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>
        /// prefix/suffix 한 행을 그린다. 토글이 켜져 있을 때만 키 피커와 동적 우선순위 토글을 노출한다.
        /// </summary>
        /// <param name="title">행 제목(Prefix/Suffix).</param>
        /// <param name="useProperty">사용 여부 토글 프로퍼티.</param>
        /// <param name="keyProp">로컬라이즈 키 프로퍼티([LocalizeKey] 드로어로 렌더).</param>
        /// <param name="dynamicFirstProp">동적 조각 우선 여부 토글 프로퍼티.</param>
        private void DrawAffixRow(string title, SerializedProperty useProperty, SerializedProperty keyProp, SerializedProperty dynamicFirstProp)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(title, EditorStyles.boldLabel, GUILayout.Width(80f));
                useProperty.boolValue = EditorGUILayout.ToggleLeft("사용", useProperty.boolValue, GUILayout.Width(60f));
                GUILayout.FlexibleSpace();
            }

            if (!useProperty.boolValue)
            {
                return;
            }

            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(keyProp, GUIContent.none);
                dynamicFirstProp.boolValue = EditorGUILayout.ToggleLeft(
                    "동적 조각을 정적 키보다 앞에 배치",
                    dynamicFirstProp.boolValue);
            }
        }
    }

}
