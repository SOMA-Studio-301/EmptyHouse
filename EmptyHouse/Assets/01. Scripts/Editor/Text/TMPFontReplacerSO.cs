using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// 열려 있는 씬의 모든 TMP 텍스트 폰트를 지정 폰트로 일괄 교체하는 설정을 보관하는 에디터 전용 ScriptableObject이다.
/// 실제 교체 로직은 TMPFontReplacerEditor가 이 설정을 읽어 수행한다.
/// </summary>
[CreateAssetMenu(fileName = "TMPFontReplacer", menuName = "Tools/TMP Font Replacer")]
public class TMPFontReplacerSO : ScriptableObject
{
    [Header("Target Font")]
    [Tooltip("교체 대상이 될 폰트 에셋. 필수.")]
    [SerializeField] private TMP_FontAsset targetFont;

    [Tooltip("함께 적용할 폰트 머티리얼. 비우면 targetFont의 기본 머티리얼을 사용한다.")]
    [SerializeField] private Material fontMaterial;

    [Header("Scope")]
    [Tooltip("true: 열려 있는 모든 씬, false: 활성 씬만 처리")]
    [SerializeField] private bool allOpenScenes = true;

    [Tooltip("비활성 오브젝트의 TMP도 포함한다.")]
    [SerializeField] private bool includeInactive = true;

    [Tooltip("씬 TMP가 프리팹 인스턴스면 원본 프리팹 에셋을 직접 수정한다. false면 인스턴스에만 오버라이드로 적용.")]
    [SerializeField] private bool editPrefabSource = true;

    [Header("Filter")]
    [Tooltip("이 목록에 든 폰트를 쓰는 TMP만 교체한다. 비우면 모든 폰트를 교체.")]
    [SerializeField] private List<TMP_FontAsset> onlyReplaceFonts = new List<TMP_FontAsset>();

    /// <summary>교체 대상이 될 폰트 에셋이다.</summary>
    public TMP_FontAsset TargetFont => targetFont;

    /// <summary>함께 적용할 폰트 머티리얼(없으면 null → 기본 머티리얼 사용)이다.</summary>
    public Material FontMaterial => fontMaterial;

    /// <summary>열려 있는 모든 씬을 처리할지(true), 활성 씬만 처리할지(false) 여부이다.</summary>
    public bool AllOpenScenes => allOpenScenes;

    /// <summary>비활성 오브젝트의 TMP도 포함할지 여부이다.</summary>
    public bool IncludeInactive => includeInactive;

    /// <summary>프리팹 인스턴스일 때 원본 프리팹 에셋을 직접 수정할지 여부이다.</summary>
    public bool EditPrefabSource => editPrefabSource;

    /// <summary>교체 대상을 특정 폰트로 한정하는 필터 목록(비어 있으면 전체)이다.</summary>
    public IReadOnlyList<TMP_FontAsset> OnlyReplaceFonts => onlyReplaceFonts;
}
