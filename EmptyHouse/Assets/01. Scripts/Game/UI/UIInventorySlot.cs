using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HUD 인벤토리 한 칸의 뷰. 인벤 칸 상시 노출 + 손에 든 칸 하이라이트
/// 슬롯 프리팹 루트에 붙으며, <see cref="UIInventory"/> 가 슬롯 수만큼 복제해 배치한다.
/// 상태를 소유하지 않고 전달받은 것을 그리기만 한다 — 채움/비움·
/// 손 규칙은 <see cref="PlayerInventory"/> 가 갖는다.
/// </summary>
public class UIInventorySlot : MonoBehaviour
{
    [Header("Widgets")]
    [SerializeField] private Image iconImage;
    [SerializeField] private GameObject highlightRoot;
    [SerializeField] private TMP_Text numberLabel;

    /// <summary>칸 번호를 라벨에 표시한다. Tab 홀드 + 숫자 키의 그 번호이므로 1부터 셈</summary>
    /// <param name="number">표시할 칸 번호(1-based).</param>
    public void SetNumber(int number)
    {
        numberLabel.text = number.ToString();
    }

    /// <summary>칸에 든 아이템의 아이콘을 그림</summary>
    /// <param name="item">칸에 든 아이템 데이터</param>
    public void SetItem(ItemDataSO item)
    {
        iconImage.sprite = item.Icon;
        iconImage.enabled = true;
    }

    /// <summary>칸을 빈 칸으로 그림. 아이콘을 지우고 숨김</summary>
    public void SetEmpty()
    {
        iconImage.sprite = null;
        iconImage.enabled = false;
    }

    /// <summary>손에 든 칸 하이라이트를 켜고 끔</summary>
    /// <param name="isHeld">이 칸을 손에 들고 있는지 여부.</param>
    public void SetHighlight(bool isHeld)
    {
        highlightRoot.SetActive(isHeld);
    }
}
