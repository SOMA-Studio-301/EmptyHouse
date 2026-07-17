using UnityEngine;

/// <summary>
/// 설정 창 탭 바의 버튼 하나. 클릭 처리는 UISettings 가 하고, 이쪽은 선택 상태 표시만 책임진다.
/// </summary>
public class UISettingsTabButton : MonoBehaviour
{
    [SerializeField] private UIGenericButton button;
    [SerializeField] private GameObject selectedIndicator;

    public UIGenericButton Button => button; // UISettings 가 Clicked 를 구독하기 위한 통로

    /// <summary>선택 표시(밑줄)를 켜고 끈다.</summary>
    /// <param name="selected">이 탭이 선택되었는지 여부.</param>
    public void SetSelected(bool selected)
    {
        selectedIndicator.SetActive(selected);
    }
}
