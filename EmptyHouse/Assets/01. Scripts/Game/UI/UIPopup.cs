using System;
using Border.Core;
using Border.Localization;
using UnityEngine;
using UnityEngine.Events;

/// <summary>공용 팝업 종류. UIPopup 의 프리셋 배열이 이 값으로 문구와 버튼 구성을 고른다.</summary>
public enum PopupType
{
    None = 0,
    DeleteRunData = 1, // 저장된 런 데이터 삭제 확인
    ExitRoom = 2,      // 방 나가기 확인
}

/// <summary>
/// 씬에 하나만 두는 공용 확인 팝업. 채널로 들어온 PopupType 에 맞춰 내용을 구성하고 자기 자신을 띄운다.
/// 확인 동작은 요청자가 콜백으로 실어 보내므로 이 컴포넌트는 어떤 기능도 알지 못한다 — 표시와 전달만 한다.
/// 문구는 코드가 아니라 인스펙터 프리셋에 둔다. 로컬라이즈 키를 코드에 박으면 문구 수정에 컴파일이 필요해진다.
/// 이 컴포넌트는 항상 켜져 있어야 채널을 구독할 수 있으므로, 껐다 켜는 대상은 자식 popupRoot 다.
/// Esc 로 닫는 것은 각 씬의 UI 매니저 몫이다 — Cancel 입력의 소유자가 거기이기 때문이다.
/// </summary>
public class UIPopup : MonoBehaviour
{
    /// <summary>팝업 한 종류의 표시 구성.</summary>
    [Serializable]
    private class PopupPreset
    {
        public PopupType Type;                        // 이 프리셋이 담당하는 팝업 종류
        [LocalizeKey] public string BodyKey;          // 본문 로컬라이즈 키
        [LocalizeKey] public string ConfirmKey;       // 확인 버튼 라벨 키
        [LocalizeKey] public string CancelKey;        // 취소 버튼 라벨 키. ShowCancel 이 false 면 무시된다
        public bool ShowCancel = true;                // false 면 확인 단일 버튼(알림형)
    }

    [Header("Listening to")]
    [SerializeField] private PopupEventChannelSO popupRequested;

    [Header("Root")]
    [SerializeField] private GameObject popupRoot; // 실제로 켜고 끄는 팝업 본체. 이 컴포넌트는 항상 켜져 있어야 한다

    [Header("Texts")]
    [SerializeField] private UILocalizeText bodyText;

    [Header("Buttons")]
    [SerializeField] private UIGenericButton confirmButton;
    [SerializeField] private UIGenericButton cancelButton;

    [Header("Presets")]
    [SerializeField] private PopupPreset[] presets;

    private UnityAction pendingConfirm; // 요청자가 맡긴 확인 동작. 알림형이면 null

    /// <summary>팝업이 떠 있는지. 각 씬 UI 매니저가 Esc 처리 우선순위를 정할 때 읽는다.</summary>
    public bool IsOpen => popupRoot.activeSelf;

    /// <summary>요청 채널과 버튼을 구독하고 팝업은 닫힌 상태로 시작한다.</summary>
    private void OnEnable()
    {
        popupRequested.OnEventRaised += Open;

        confirmButton.Clicked += Confirm;
        cancelButton.Clicked += Close;

        popupRoot.SetActive(false);
    }

    /// <summary>구독을 해제한다. 채널은 SO 라 씬 밖에서 살아남으므로 죽은 델리게이트를 남기지 않는다.</summary>
    private void OnDisable()
    {
        popupRequested.OnEventRaised -= Open;

        confirmButton.Clicked -= Confirm;
        cancelButton.Clicked -= Close;
    }

    /// <summary>팝업을 닫고 맡아둔 확인 동작을 버린다. 취소 버튼과 Esc 가 함께 쓰는 경로다.</summary>
    public void Close()
    {
        pendingConfirm = null;
        popupRoot.SetActive(false);
    }

    /// <summary>
    /// 요청받은 종류의 프리셋으로 내용을 구성하고 팝업을 띄운다.
    /// 프리셋이 없으면 경고만 남기고 띄우지 않는다 — 빈 팝업이 뜨는 것보다 안 뜨는 편이 원인을 찾기 쉽다.
    /// </summary>
    /// <param name="type">띄울 팝업 종류.</param>
    /// <param name="onConfirm">확인 시 실행할 동작. null 이면 확인은 닫기와 같다.</param>
    private void Open(PopupType type, UnityAction onConfirm)
    {
        PopupPreset preset = FindPreset(type);
        if (preset == null)
        {
            Log.W($"[UIPopup] {type} 프리셋이 없어 팝업을 띄우지 않습니다.");
            return;
        }

        pendingConfirm = onConfirm;

        bodyText.SetKey(preset.BodyKey);
        confirmButton.SetButton(preset.ConfirmKey);
        confirmButton.gameObject.SetActive(true);

        cancelButton.SetButton(preset.CancelKey);
        cancelButton.gameObject.SetActive(preset.ShowCancel);

        popupRoot.SetActive(true);
    }

    /// <summary>확인 버튼. 맡아둔 동작을 실행하고 닫는다. 닫기를 먼저 해 동작 안에서 팝업을 다시 띄워도 살아남게 한다.</summary>
    private void Confirm()
    {
        UnityAction confirm = pendingConfirm;
        Close();

        confirm?.Invoke();
    }

    /// <summary>해당 종류의 프리셋을 찾는다.</summary>
    /// <param name="type">찾을 팝업 종류.</param>
    /// <returns>프리셋. 없으면 null.</returns>
    private PopupPreset FindPreset(PopupType type)
    {
        for (int i = 0; i < presets.Length; i++)
        {
            if (presets[i].Type == type)
            {
                return presets[i];
            }
        }

        return null;
    }
}
