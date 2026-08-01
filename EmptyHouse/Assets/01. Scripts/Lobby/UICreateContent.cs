using System;
using TMPro;
using UnityEngine;

/// <summary>
/// 방 만들기 팝업 뷰. 이름/비밀번호 입력·토글·생성 버튼을 소유하고 의도만 액션으로 올린다.
/// 팝업 여닫기는 부모(UILobby)가 이 오브젝트를 켜고 끄며 하고, 각 액션이 할 일도 부모가 정한다.
/// 닫기 버튼은 두지 않는다 — 닫기는 부모의 뒤로 가기 스택(Back 버튼·ESC)이 전담한다.
/// </summary>
public class UICreateContent : MonoBehaviour
{
    [SerializeField] private TMP_InputField lobbyNameInput;        // 방 이름 입력창
    [SerializeField] private UIPasswordSlideField passwordField;   // 슬라이드 비밀번호 입력. 토글·입력창·연출을 통째로 소유한다
    [SerializeField] private UIGenericButton createButton;         // 방 생성 확정 버튼
    [SerializeField] private GameObject forbiddenWordWarningLabel; // 금칙어 경고

    public event Action<string, string> CreateConfirmed; // 방 생성 확정. (방 이름, 비밀번호)

    /// <summary>버튼 리스너를 등록한다. 토글·슬라이드는 위젯이 스스로 처리한다.</summary>
    private void OnEnable()
    {
        createButton.Clicked += RaiseCreateConfirmed;
    }

    /// <summary>리스너를 해제한다.</summary>
    private void OnDisable()
    {
        createButton.Clicked -= RaiseCreateConfirmed;
    }

    /// <summary>팝업을 연다. 이전 입력이 남지 않도록 매번 비우고, 방 이름 입력에 바로 포커스를 준다.</summary>
    public void Show()
    {
        ResetInputs();
        gameObject.SetActive(true);
        lobbyNameInput.ActivateInputField(); // 활성화 뒤에 호출해야 포커스가 잡힌다
    }

    /// <summary>팝업을 닫는다.</summary>
    public void Hide()
    {
        gameObject.SetActive(false);
    }

    /// <summary>입력·토글·경고를 초기 상태로 되돌린다.</summary>
    public void ResetInputs()
    {
        lobbyNameInput.DeactivateInputField();
        lobbyNameInput.text = "";
        forbiddenWordWarningLabel.SetActive(false);

        passwordField.ResetField();
    }

    /// <summary>금칙어 경고를 토글한다. 켤 때는 이름 입력 포커스를 먼저 푼다.</summary>
    /// <param name="visible">표시 여부</param>
    public void SetForbiddenWordWarning(bool visible)
    {
        if (visible) lobbyNameInput.DeactivateInputField();

        forbiddenWordWarningLabel.SetActive(visible);
    }

    /// <summary>입력값을 읽어 방 생성 의도를 올린다. 유효성·금칙어 판정은 상위 몫이다.</summary>
    private void RaiseCreateConfirmed()
    {
        string password = passwordField.IsOn ? passwordField.Password : "";
        CreateConfirmed?.Invoke(lobbyNameInput.text.Trim(), password);
    }
}
