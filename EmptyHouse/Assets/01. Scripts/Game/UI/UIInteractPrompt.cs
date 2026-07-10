using Border.Core;
using TMPro;
using UnityEngine;

/// <summary>
/// 조준선 아래 상호작용 프롬프트를 그리는 HUD 위젯 (조작상호작용UI.md 3-3).
/// Screen Space - Overlay 캔버스에 놓이되, 캔버스는 플레이어 프리팹의 자식이라 인스펙터 직접 참조가 가능하다
/// (비소유자 인스턴스는 PlayerController 가 캔버스째 끈다).
/// 이 클래스는 <see cref="InteractPromptInfo"/> 를 그대로 그릴 뿐 대상 타입으로 분기하지 않는다(3-2).
/// 신규 기믹이 추가돼도 이 파일은 수정될 이유가 없다.
/// </summary>
public class UIInteractPrompt : MonoBehaviour
{
    [Header("Input")]
    [SerializeField] private InputReader inputReader;

    [Header("Widgets")]
    [SerializeField] private GameObject promptRoot;
    [SerializeField] private TMP_Text promptLabel;

    [Header("Colors")]
    [SerializeField] private Color activeColor = Color.white;
    [SerializeField] private Color inactiveColor = Color.gray;

    /// <summary>
    /// 프롬프트 정보를 화면에 반영한다. PlayerInteractor 가 매 프레임 호출한다.
    /// Hidden 이면 루트를 끄고, Inactive 면 회색 사유 문구만(입력키 미표기), Active 면 `[키] 행위명` 을 그린다(3-3).
    /// </summary>
    /// <param name="info">이번 프레임의 프롬프트 정보.</param>
    public void Render(InteractPromptInfo info)
    {
        // TODO(impl): Hidden → promptRoot.SetActive(false) 후 return.
        // TODO(impl): promptRoot.SetActive(true).
        // TODO(impl): Inactive → promptLabel.color = inactiveColor, text = info.InactiveReason (입력키 절대 붙이지 않는다).
        // TODO(impl): Active → promptLabel.color = activeColor, text = BuildActiveText(info).
        Log.D($"[UIInteractPrompt] Render {info.State}");
    }

    /// <summary>
    /// 활성 프롬프트 문자열을 만든다. 입력키는 하드코딩하지 않고 현재 바인딩을 런타임 조회한다(3-3 ⚠️).
    /// Tap 이면 `[E] 회수`, Hold 면 `[E 홀드] 적재` 형태다.
    /// </summary>
    /// <param name="info">활성 상태의 프롬프트 정보.</param>
    /// <returns>화면에 그릴 프롬프트 문자열.</returns>
    private string BuildActiveText(InteractPromptInfo info)
    {
        // TODO(impl): key = inputReader.GetInteractBindingDisplayString().
        // TODO(impl): info.InputMethod == Hold ? $"[{key} 홀드] {info.ActionName}" : $"[{key}] {info.ActionName}".
        Log.D($"[UIInteractPrompt] BuildActiveText {info.ActionName}");
        return default;
    }
}
