using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// 범용 로딩 표시 위젯. 기본 문구 뒤에 점을 순환시켜 진행 중임을 알린다.
/// 자기 자신과 자식만 참조하므로 어느 화면에든 그대로 얹어 쓸 수 있다.
/// </summary>
public class UILoadingIndicator : MonoBehaviour
{
    [Header("View")]
    [SerializeField] private TMP_Text messageText;  // 문구 출력 대상
    [SerializeField] private RectTransform spinner; // 선택. 회전시킬 아이콘

    [Header("Settings")]
    [SerializeField] private string baseMessage = "Loading"; // 점 앞에 붙는 고정 문구
    [SerializeField] private int maxDotCount = 3;            // 점 최대 개수
    [SerializeField] private float dotInterval = 0.35f;      // 점 하나가 늘어나는 간격(초)
    [SerializeField] private float spinnerSpeed = -180f;     // 아이콘 초당 회전 각도

    private readonly StringBuilder builder = new StringBuilder(32);
    private float nextDotTime;
    private int dotCount;

    /// <summary>로딩 표시를 켠다.</summary>
    public void Show()
    {
        gameObject.SetActive(true);
    }

    /// <summary>로딩 표시를 끈다.</summary>
    public void Hide()
    {
        gameObject.SetActive(false);
    }

    /// <summary>점 애니메이션 상태를 초기화하고 첫 프레임을 그린다.</summary>
    private void OnEnable()
    {
        dotCount = 0;
        nextDotTime = Time.unscaledTime + dotInterval;
        ApplyMessage();
    }

    /// <summary>점 개수를 순환시키고 아이콘을 회전시킨다. 일시정지 영향을 받지 않도록 unscaled 시간을 쓴다.</summary>
    private void Update()
    {
        if (spinner != null)
        {
            spinner.Rotate(0f, 0f, spinnerSpeed * Time.unscaledDeltaTime);
        }

        if (Time.unscaledTime < nextDotTime) return;

        nextDotTime = Time.unscaledTime + dotInterval;
        dotCount = (dotCount + 1) % (maxDotCount + 1);
        ApplyMessage();
    }

    /// <summary>기본 문구 + 현재 점 개수를 텍스트에 반영한다.</summary>
    private void ApplyMessage()
    {
        builder.Length = 0;
        builder.Append(baseMessage);
        builder.Append('.', dotCount);

        messageText.SetText(builder);
    }
}
