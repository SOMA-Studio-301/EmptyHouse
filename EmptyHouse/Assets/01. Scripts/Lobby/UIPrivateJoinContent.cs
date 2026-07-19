using System;
using TMPro;
using UnityEngine;

/// <summary>
/// 비밀번호 입력 팝업 뷰. 비밀번호 입력창·Join/Back 버튼을 소유하고 확정/취소 의도만 액션으로 올린다.
/// 팝업 여닫기는 부모(UILobby)가 이 오브젝트를 켜고 끄며 하고, 각 액션이 할 일도 부모가 정한다.
/// </summary>
public class UIPrivateJoinContent : MonoBehaviour
{
    [SerializeField] private TMP_InputField passwordInput;    // 비밀번호 입력창
    [SerializeField] private UIGenericButton confirmButton;   // Join 확정 버튼
    [SerializeField] private UIGenericButton cancelButton;    // Back(취소) 버튼
    [SerializeField] private GameObject passwordWarningLabel; // 비밀번호 불일치 경고

    /// <summary>발행: Join 확정. 입력된 비밀번호를 싣는다.</summary>
    public event Action<string> JoinConfirmed;

    /// <summary>발행: 취소.</summary>
    public event Action JoinCancelled;

    /// <summary>버튼 리스너를 등록한다.</summary>
    private void Awake()
    {
        confirmButton.Clicked += RaiseJoinConfirmed;
        cancelButton.Clicked += RaiseJoinCancelled;
    }

    /// <summary>리스너를 해제한다.</summary>
    private void OnDestroy()
    {
        confirmButton.Clicked -= RaiseJoinConfirmed;
        cancelButton.Clicked -= RaiseJoinCancelled;
    }

    /// <summary>팝업을 연다. 입력·경고를 비운 상태로 시작한다.</summary>
    public void Show()
    {
        passwordInput.text = "";
        passwordWarningLabel.SetActive(false);
        gameObject.SetActive(true);
    }

    /// <summary>팝업을 닫고 입력·경고를 청소한다.</summary>
    public void Hide()
    {
        passwordInput.text = "";
        passwordWarningLabel.SetActive(false);
        gameObject.SetActive(false);
    }

    /// <summary>비밀번호 불일치 경고를 토글한다.</summary>
    /// <param name="visible">표시 여부</param>
    public void SetPasswordWarning(bool visible)
    {
        passwordWarningLabel.SetActive(visible);
    }

    /// <summary>입력된 비밀번호를 실어 Join 확정 의도를 올린다.</summary>
    private void RaiseJoinConfirmed()
    {
        JoinConfirmed?.Invoke(passwordInput.text.Trim());
    }

    /// <summary>취소 의도를 올린다.</summary>
    private void RaiseJoinCancelled()
    {
        JoinCancelled?.Invoke();
    }
}
