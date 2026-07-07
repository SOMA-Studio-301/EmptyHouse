using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Border.UI
{
    /// <summary>
    /// Selectable의 상태(Normal/Highlighted/Selected) 변화에 맞춰 추가 Graphic 목록의 색상을 동기화한다.
    /// 기본 m_TargetGraphic은 그대로 두고, Background/Fill 등의 보조 그래픽도 같은 색상 트랜지션을 적용한다.
    /// </summary>
    [RequireComponent(typeof(Selectable))]
    public class SelectableExtraGraphics : MonoBehaviour,
        ISelectHandler, IDeselectHandler,
        IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] private Graphic[] extraGraphics;

        private Selectable _selectable;

        /// <summary>
        /// 같은 GameObject의 Selectable 참조를 캐시한다.
        /// </summary>
        private void Awake()
        {
            _selectable = GetComponent<Selectable>();
        }

        /// <summary>
        /// EventSystem이 이 Selectable을 선택했을 때 추가 그래픽에 SelectedColor를 적용한다.
        /// </summary>
        public void OnSelect(BaseEventData eventData)
        {
            Apply(_selectable.colors.selectedColor);
        }

        /// <summary>
        /// EventSystem이 이 Selectable의 선택을 해제했을 때 추가 그래픽을 NormalColor로 되돌린다.
        /// </summary>
        public void OnDeselect(BaseEventData eventData)
        {
            Apply(_selectable.colors.normalColor);
        }

        /// <summary>
        /// 마우스 포인터가 진입했을 때 추가 그래픽에 HighlightedColor를 적용한다.
        /// </summary>
        public void OnPointerEnter(PointerEventData eventData)
        {
            Apply(_selectable.colors.highlightedColor);
        }

        /// <summary>
        /// 마우스 포인터가 빠져나갔을 때 현재 선택 여부에 따라 SelectedColor 또는 NormalColor를 복원한다.
        /// </summary>
        public void OnPointerExit(PointerEventData eventData)
        {
            bool isCurrentSelected = EventSystem.current != null
                && EventSystem.current.currentSelectedGameObject == gameObject;

            Apply(isCurrentSelected ? _selectable.colors.selectedColor : _selectable.colors.normalColor);
        }

        /// <summary>
        /// 등록된 추가 그래픽 전체에 색상 페이드를 적용한다.
        /// </summary>
        /// <param name="color">적용할 대상 색상</param>
        private void Apply(Color color)
        {
            if (extraGraphics == null)
            {
                return;
            }

            float duration = _selectable.colors.fadeDuration;
            for (int i = 0; i < extraGraphics.Length; i++)
            {
                Graphic graphic = extraGraphics[i];
                if (graphic != null)
                {
                    graphic.CrossFadeColor(color, duration, true, true);
                }
            }
        }
    }
}
