using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Border.Core;
using Border.Localization;

namespace Border.UI
{
    /// <summary>
    /// 공통 UI 버튼의 클릭 이벤트와 라벨/스프라이트 갱신을 담당한다.
    /// 클릭 사운드는 UISelectableSoundHook이 일괄 처리하므로 본 클래스는 사운드 분기를 가지지 않는다.
    /// </summary>
    public class UIGenericButton : MonoBehaviour
    {
        [SerializeField] private UILocalizeText buttonLocalizeText;
        [SerializeField] private Button button;

        public UnityAction Clicked;

        /// <summary>
        /// 누락된 참조를 자동 보정하여 버튼 라벨 로컬라이징 컴포넌트를 확보한다.
        /// </summary>
        private void Awake()
        {
            button ??= GetComponent<Button>();
            EnsureLocalizeTextReference();
        }

        /// <summary>
        /// 버튼 클릭 이벤트를 외부 구독자에게 전달한다.
        /// </summary>
        public void Click()
        {
            Clicked?.Invoke();
        }

        /// <summary>
        /// 버튼 라벨에 사용할 로컬라이징 키를 설정한다.
        /// </summary>
        /// <param name="localizationKey">적용할 로컬라이징 키</param>
        public void SetButton(string localizationKey)
        {
            EnsureLocalizeTextReference();

            if (buttonLocalizeText == null)
            {
                return;
            }

            buttonLocalizeText.SetKey(localizationKey);
        }

        /// <summary>
        /// 버튼 이미지 스프라이트를 교체한다.
        /// </summary>
        /// <param name="sprite">적용할 스프라이트</param>
        public void SetSprite(Sprite sprite)
        {
            if (button != null)
                button.image.sprite = sprite;
        }

        /// <summary>
        /// 버튼 하위 텍스트에서 UILocalizeText를 찾고 없으면 자동으로 생성한다.
        /// </summary>
        private void EnsureLocalizeTextReference()
        {
            if (buttonLocalizeText != null)
            {
                return;
            }

            buttonLocalizeText = GetComponentInChildren<UILocalizeText>(true);
            if (buttonLocalizeText != null)
            {
                return;
            }

            TMP_Text tmpText = GetComponentInChildren<TMP_Text>(true);
            if (tmpText == null)
            {
                return;
            }

            buttonLocalizeText = tmpText.GetComponent<UILocalizeText>();
            if (buttonLocalizeText == null)
            {
                buttonLocalizeText = tmpText.gameObject.AddComponent<UILocalizeText>();
            }
        }
    }

}
