using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 토글로 여닫는 슬라이드 비밀번호 입력 위젯. 입력창이 토글 왼쪽에서 펼쳐지고 접힌다.
/// 방 만들기 패널과 방 목록 셀이 같은 프리팹 구조(SecretToggleContainer > PasswordInput + SecretToggle)를 쓰므로
/// 양쪽이 이 컴포넌트 하나를 붙여 재사용한다. 열림 폭은 프리팹에 작성된 입력창 너비를 그대로 읽어 쓴다.
/// 입력창 pivot.x 가 1(오른쪽 끝)이라 폭만 키우면 토글에 붙은 채 왼쪽으로 뻗는다.
/// </summary>
public class UIPasswordSlideField : MonoBehaviour
{
    [SerializeField] private Toggle secretToggle;          // 입력창 여닫기 토글
    [SerializeField] private TMP_InputField passwordInput; // 비밀번호 입력창
    [SerializeField] private float slideDuration = 0.2f;   // 펼침/접힘 시간(초)
    [SerializeField] private float shakeDuration = 0.4f;   // 경고 흔들기 시간(초)
    [SerializeField] private float shakeStrength = 14f;    // 경고 흔들기 세기(px)

    public event Action<bool> ToggleChanged; // 발행: 토글 상태 변경. 열림 여부를 싣는다
    public event Action Submitted;           // 발행: 입력창에서 Enter 로 확정

    public bool IsOn => secretToggle.isOn;               // 입력창이 열려 있는지
    public string Password => passwordInput.text.Trim(); // 입력된 비밀번호

    private RectTransform inputRect; // 폭을 여닫는 입력창 RectTransform
    private Tween slideTween;        // 재사용하는 폭 트윈. PlayForward = 펼침, PlayBackwards = 접힘
    private Tween shakeTween;        // 경고 흔들기 트윈. 실패 시에만 만들어 쓴다

    /// <summary>폭 트윈을 미리 만들고 입력창을 접힌 상태로 맞춘다.</summary>
    private void Awake()
    {
        inputRect = passwordInput.GetComponent<RectTransform>();

        // 프리팹에 작성된 너비가 곧 열림 폭이다 — 코드에 폭 상수를 두지 않는다
        Vector2 openSize = inputRect.sizeDelta;
        inputRect.sizeDelta = new Vector2(0f, openSize.y);

        slideTween = inputRect.DOSizeDelta(openSize, slideDuration)
            .SetAutoKill(false)
            .SetLink(gameObject)
            .SetUpdate(true)
            .OnRewind(() => passwordInput.gameObject.SetActive(false)) // 다 접힌 뒤에 꺼야 폭 0 이 안 보인다
            .Pause();

        // 접힌 상태(0)를 시작값으로 확정시킨다. 건너뛰면 첫 재생 시점의 폭이 시작값으로 잡힌다
        slideTween.Complete();
        slideTween.Rewind();
    }

    /// <summary>토글·입력창 리스너를 등록한다.</summary>
    private void OnEnable()
    {
        secretToggle.onValueChanged.AddListener(HandleToggleChanged);
        passwordInput.onSubmit.AddListener(RaiseSubmitted);
    }

    /// <summary>리스너를 해제한다.</summary>
    private void OnDisable()
    {
        secretToggle.onValueChanged.RemoveListener(HandleToggleChanged);
        passwordInput.onSubmit.RemoveListener(RaiseSubmitted);
    }

    /// <summary>입력창을 펼친다. 이미 열려 있으면 아무것도 하지 않는다.</summary>
    public void Open()
    {
        if (secretToggle.isOn) return;

        secretToggle.isOn = true; // onValueChanged 를 타고 연출·이벤트가 함께 일어난다
    }

    /// <summary>입력창을 접는다. 이미 닫혀 있으면 아무것도 하지 않는다.</summary>
    public void Close()
    {
        if (!secretToggle.isOn) return;

        secretToggle.isOn = false;
    }

    /// <summary>연출 없이 즉시 초기 상태로 되돌린다. ToggleChanged 를 발행하지 않는다.</summary>
    public void ResetField()
    {
        passwordInput.text = "";
        passwordInput.DeactivateInputField();
        secretToggle.SetIsOnWithoutNotify(false);
        slideTween.Rewind(); // OnRewind 가 입력창을 비활성화한다
    }

    /// <summary>비밀번호가 틀렸음을 알린다. 입력창을 흔들고 비운 뒤 다시 포커스를 준다.</summary>
    public void ShakeAndClear()
    {
        passwordInput.text = "";

        // HLG 가 위치를 다시 잡기 전에 원위치를 붙잡아 두고, 흔들기가 끝나면 그 자리로 되돌린다
        Vector2 homePosition = inputRect.anchoredPosition;

        shakeTween?.Kill();
        shakeTween = inputRect.DOShakeAnchorPos(shakeDuration, new Vector2(shakeStrength, 0f), 20, 0f)
            .SetLink(gameObject)
            .SetUpdate(true)
            .OnComplete(() => inputRect.anchoredPosition = homePosition);

        passwordInput.ActivateInputField();
    }

    /// <summary>토글 상태에 따라 입력창을 펼치거나 접고, 상태 변경을 올린다.</summary>
    /// <param name="isOn">열림 여부</param>
    private void HandleToggleChanged(bool isOn)
    {
        if (isOn)
        {
            passwordInput.gameObject.SetActive(true);
            slideTween.PlayForward();
            passwordInput.ActivateInputField(); // 열자마자 바로 타이핑할 수 있게
        }
        else
        {
            passwordInput.text = "";
            passwordInput.DeactivateInputField();
            slideTween.PlayBackwards();
        }

        ToggleChanged?.Invoke(isOn);
    }

    /// <summary>입력창 Enter 를 확정 의도로 올린다.</summary>
    /// <param name="_">입력된 문자열. 값은 Password 로 읽으므로 쓰지 않는다</param>
    private void RaiseSubmitted(string _)
    {
        Submitted?.Invoke();
    }
}
