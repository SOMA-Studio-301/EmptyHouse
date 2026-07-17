using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// 방 만들기 팝업 뷰. 이름/비밀번호 입력·토글·생성/닫기 버튼을 소유하고 의도만 액션으로 올린다.
/// 팝업 여닫기는 부모(UILobby)가 이 오브젝트를 켜고 끄며 하고, 각 액션이 할 일도 부모가 정한다.
/// </summary>
public class UICreateContent : MonoBehaviour
{
    [SerializeField] private TMP_InputField lobbyNameInput;        // 방 이름 입력창
    [SerializeField] private TMP_InputField lobbyPasswordInput;    // 방 비밀번호 입력창
    [SerializeField] private Toggle passwordToggle;               // 비밀번호 방 여부
    [SerializeField] private UIGenericButton createButton;         // 방 생성 확정 버튼
    [SerializeField] private UIGenericButton closeButton;          // 팝업 닫기 버튼
    [SerializeField] private GameObject forbiddenWordWarningLabel; // 금칙어 경고

    /// <summary>부모가 주입: 방 생성 확정. (방 이름, 비밀번호) — 비밀번호를 쓰지 않으면 빈 문자열.</summary>
    public UnityAction<string, string> CreateConfirmed;

    /// <summary>부모가 주입: 팝업 닫기.</summary>
    public UnityAction CloseRequested;

    /// <summary>버튼·토글 리스너를 등록한다.</summary>
    private void Awake()
    {
        createButton.Clicked += RaiseCreateConfirmed;
        closeButton.Clicked += RaiseCloseRequested;
        passwordToggle.onValueChanged.AddListener(HandlePasswordToggleChanged);
    }

    /// <summary>리스너를 해제한다.</summary>
    private void OnDestroy()
    {
        createButton.Clicked -= RaiseCreateConfirmed;
        closeButton.Clicked -= RaiseCloseRequested;
        passwordToggle.onValueChanged.RemoveListener(HandlePasswordToggleChanged);
    }

    /// <summary>팝업을 연다. 이전 입력이 남지 않도록 매번 비운다.</summary>
    public void Show()
    {
        ResetInputs();
        gameObject.SetActive(true);
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
        lobbyPasswordInput.DeactivateInputField();

        lobbyNameInput.text = "";
        lobbyPasswordInput.text = "";
        forbiddenWordWarningLabel.SetActive(false);

        // isOn 을 끄면 onValueChanged 를 타고 비밀번호 입력창도 함께 닫힌다. 이미 꺼져 있어도 확실히 닫는다.
        passwordToggle.isOn = false;
        HandlePasswordToggleChanged(false);
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
        string password = passwordToggle.isOn ? lobbyPasswordInput.text.Trim() : "";
        CreateConfirmed?.Invoke(lobbyNameInput.text.Trim(), password);
    }

    /// <summary>팝업 닫기 의도를 올린다.</summary>
    private void RaiseCloseRequested()
    {
        CloseRequested?.Invoke();
    }

    /// <summary>비밀번호 사용 여부에 따라 비밀번호 입력창을 여닫는다.</summary>
    /// <param name="isOn">비밀번호 방 여부</param>
    private void HandlePasswordToggleChanged(bool isOn)
    {
        lobbyPasswordInput.gameObject.SetActive(isOn);
    }
}
