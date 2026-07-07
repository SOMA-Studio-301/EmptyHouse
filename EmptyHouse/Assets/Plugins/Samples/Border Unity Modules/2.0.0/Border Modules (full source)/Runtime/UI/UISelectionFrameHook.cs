using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Border.UI
{
    /// <summary>
    /// <see cref="Selectable"/>에 부착되어 EventSystem의 Select/Deselect 이벤트를 가로채
    /// 지정된 9-slice 선택 프레임 GameObject의 활성 상태를 토글한다.
    /// 모든 입력 디바이스(마우스/키보드/게임패드)는 EventSystem을 통해 동일 인터페이스로 도달하므로
    /// 본 컴포넌트 1개로 입력 디바이스 무관 동작을 보장한다.
    /// ColorTint 트랜지션 색상은 에디터에서 "Reset ColorBlock To White" 메뉴로 1회 베이킹해 둔다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Selectable))]
    public class UISelectionFrameHook : MonoBehaviour,
        ISelectHandler,
        IDeselectHandler
    {
        [Tooltip("선택 상태에서 활성화될 9-slice 프레임 루트 GameObject.")]
        [SerializeField] private GameObject selectionFrame;

        /// <summary>
        /// 프레임 초기 상태를 비활성으로 설정한다.
        /// </summary>
        private void Awake()
        {
            if (selectionFrame != null)
            {
                selectionFrame.SetActive(false);
            }
        }

        /// <summary>
        /// Selectable이 비활성/파괴될 때 프레임이 켜진 상태로 남는 것을 방지한다.
        /// </summary>
        private void OnDisable()
        {
            if (selectionFrame != null)
            {
                selectionFrame.SetActive(false);
            }
        }

        /// <summary>
        /// EventSystem이 본 Selectable을 선택했을 때 호출되어 프레임을 활성화한다.
        /// </summary>
        /// <param name="eventData">BaseEventData</param>
        public void OnSelect(BaseEventData eventData)
        {
            if (selectionFrame != null)
            {
                selectionFrame.SetActive(true);
            }
        }

        /// <summary>
        /// EventSystem이 본 Selectable의 선택을 해제했을 때 호출되어 프레임을 비활성화한다.
        /// </summary>
        /// <param name="eventData">BaseEventData</param>
        public void OnDeselect(BaseEventData eventData)
        {
            if (selectionFrame != null)
            {
                selectionFrame.SetActive(false);
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// 에디터에서 1회 실행하여 본 Selectable의 ColorBlock(normal/highlighted/pressed/selected)을
        /// 흰색으로 베이킹한다. disabledColor는 디자이너 의도를 보존하기 위해 변경하지 않는다.
        /// 컴포넌트 인스펙터에서 우클릭 → "Reset ColorBlock To White" 로 호출한다.
        /// </summary>
        [ContextMenu("Reset ColorBlock To White")]
        private void ResetColorBlockToWhite()
        {
            if (!TryGetComponent<Selectable>(out var selectable))
            {
                return;
            }

            ColorBlock colors = selectable.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = Color.white;
            colors.pressedColor = Color.white;
            colors.selectedColor = Color.white;
            selectable.colors = colors;

            EditorUtility.SetDirty(selectable);
        }
#endif
    }
}
