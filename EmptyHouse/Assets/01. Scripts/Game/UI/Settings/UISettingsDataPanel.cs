using UnityEngine;

/// <summary>
/// 설정 창 DATA 탭. 세이브 데이터 삭제를 담당한다.
/// 삭제는 되돌릴 수 없으므로 확인 팝업을 한 단계 거친다 — 버튼 한 번으로는 절대 지워지지 않는다.
/// 지우는 것은 RunSave(런 진행)뿐이고 ProfileSave(설정)는 보존된다. 두 파일을 나눈 이유가 바로 이것이다.
/// </summary>
public class UISettingsDataPanel : MonoBehaviour
{
    [Header("Save")]
    [SerializeField] private SaveLoadSystem saveLoadSystem;

    [Header("Buttons")]
    [SerializeField] private UIGenericButton deleteDataButton;

    [Header("Confirm Popup")]
    [SerializeField] private GameObject confirmPopup;
    [SerializeField] private UIGenericButton confirmButton;
    [SerializeField] private UIGenericButton cancelButton;

    /// <summary>버튼 구독을 걸고 팝업은 닫힌 상태로 시작한다. 탭을 다시 열었을 때 팝업이 떠 있으면 안 된다.</summary>
    private void OnEnable()
    {
        deleteDataButton.Clicked += OpenConfirmPopup;
        confirmButton.Clicked += ConfirmDelete;
        cancelButton.Clicked += CloseConfirmPopup;

        confirmPopup.SetActive(false);
    }

    /// <summary>버튼 구독을 해제한다.</summary>
    private void OnDisable()
    {
        deleteDataButton.Clicked -= OpenConfirmPopup;
        confirmButton.Clicked -= ConfirmDelete;
        cancelButton.Clicked -= CloseConfirmPopup;
    }

    /// <summary>삭제 버튼. 바로 지우지 않고 확인 팝업만 연다.</summary>
    private void OpenConfirmPopup()
    {
        confirmPopup.SetActive(true);
    }

    /// <summary>취소 버튼. 아무것도 지우지 않고 팝업만 닫는다.</summary>
    private void CloseConfirmPopup()
    {
        confirmPopup.SetActive(false);
    }

    /// <summary>확인 버튼. 런 데이터를 삭제하고 팝업을 닫는다. 설정(ProfileSave)은 건드리지 않는다.</summary>
    private void ConfirmDelete()
    {
        saveLoadSystem.DeleteRun();
        confirmPopup.SetActive(false);
    }
}
