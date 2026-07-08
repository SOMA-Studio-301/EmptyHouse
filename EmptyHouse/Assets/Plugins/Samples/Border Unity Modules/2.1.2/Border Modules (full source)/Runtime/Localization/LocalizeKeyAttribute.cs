using UnityEngine;
using Border.Core;
using Border.Events;

namespace Border.Localization
{
    /// <summary>
    /// string 필드를 LocalizationTable 키 피커로 표시하도록 만드는 마킹용 PropertyAttribute.
    /// Editor 어셈블리의 LocalizeKeyDrawer 가 본 attribute 를 가진 SerializedProperty 를 키 검색/필터/검증 UI 로 그린다.
    /// </summary>
    public sealed class LocalizeKeyAttribute : PropertyAttribute
    {
    }

}
