using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Border.Core;
using Border.Events;
using Border.UI;
using Border.Localization;

namespace Border.Settings
{
    public class UISettingsDropdown : MonoBehaviour
    {
        [SerializeField] private TMP_Dropdown dropdown;
        [SerializeField] private float itemHeight = 20f;
        [SerializeField] private float itemPadding = 23f;
        [SerializeField] private float maxHeight = 150f;

        public UnityAction<int> ValueChanged;

        private Coroutine adjustCoroutine;
        private Coroutine trackingCoroutine;

        private void Awake()
        {
            dropdown.onValueChanged.AddListener(DropDownValueChanged);
            RegisterDropdownOpenEvents();
        }

        /// <summary>
        /// 드롭다운 GameObject에 EventTrigger를 추가하여 마우스 PointerClick과 키보드 Submit(Enter) 모두에서
        /// 리스트 높이 보정 코루틴을 실행하도록 등록한다.
        /// </summary>
        private void RegisterDropdownOpenEvents()
        {
            EventTrigger trigger = dropdown.gameObject.GetComponent<EventTrigger>();
            if (trigger == null)
                trigger = dropdown.gameObject.AddComponent<EventTrigger>();

            AddOpenTrigger(trigger, EventTriggerType.PointerClick);
            AddOpenTrigger(trigger, EventTriggerType.Submit);
        }

        /// <summary>
        /// 지정한 EventTriggerType에 대해 리스트 높이 보정 코루틴을 실행하는 엔트리를 EventTrigger에 추가한다.
        /// </summary>
        /// <param name="trigger">엔트리를 추가할 EventTrigger</param>
        /// <param name="eventType">등록할 이벤트 타입</param>
        private void AddOpenTrigger(EventTrigger trigger, EventTriggerType eventType)
        {
            EventTrigger.Entry entry = new EventTrigger.Entry();
            entry.eventID = eventType;
            entry.callback.AddListener(_ => StartAdjustDropdownList());
            trigger.triggers.Add(entry);
        }

        /// <summary>
        /// 리스트 보정 코루틴이 이미 실행 중이면 무시하고, 아니면 새로 시작한다.
        /// 마우스 클릭과 키보드 Submit이 동일 프레임에 발생해도 중복 실행되지 않도록 가드한다.
        /// </summary>
        private void StartAdjustDropdownList()
        {
            if (adjustCoroutine != null)
            {
                return;
            }

            adjustCoroutine = StartCoroutine(AdjustDropdownListHeight());
        }

        private void DropDownValueChanged(int value)
        {
            ValueChanged?.Invoke(value);
        }

        public void SetValue(int value, bool notify = true)
        {
            if (notify)
            {
                dropdown.value = value;
                return;
            }

            dropdown.SetValueWithoutNotify(value);
        }

        public float GetValue()
        {
            return dropdown.value;
        }

        public void ClearOptions()
        {
            dropdown.ClearOptions();   
        }

        /// <summary>
        /// 드롭다운에 옵션을 추가한 뒤, 아이템 수에 맞게 Template 높이를 자동 조정한다.
        /// </summary>
        public void AddOptions(List<string> option)
        {
            dropdown.AddOptions(option);
        }

        /// <summary>
        /// 1프레임 대기 후 복제된 Dropdown List의 RectTransform 높이를 아이템 수에 맞게 조정하고,
        /// 선택된 아이템이 Viewport 밖으로 나가면 자동 스크롤하는 추적 코루틴을 시작한다.
        /// 키보드 Submit 경로에서는 리스트 생성이 프레임 하나로 끝나지 않는 경우가 있어 최대 2프레임까지 탐색한다.
        /// </summary>
        private IEnumerator AdjustDropdownListHeight()
        {
            Transform dropdownList = null;
            for (int i = 0; i < 2 && dropdownList == null; i++)
            {
                yield return null;
                dropdownList = dropdown.transform.Find("Dropdown List");
            }

            if (dropdownList == null)
            {
                Log.D("[UISettingsDropdown] Dropdown List를 찾을 수 없습니다.");
                adjustCoroutine = null;
                yield break;
            }

            RectTransform rect = dropdownList.GetComponent<RectTransform>();
            float targetHeight = dropdown.options.Count * itemHeight + itemPadding;
            float finalHeight = Mathf.Min(targetHeight, maxHeight);
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, finalHeight);

            ScrollRect scrollRect = dropdownList.GetComponentInChildren<ScrollRect>();
            if (scrollRect != null)
            {
                if (trackingCoroutine != null)
                {
                    StopCoroutine(trackingCoroutine);
                }
                trackingCoroutine = StartCoroutine(TrackSelectedItemForScroll(dropdownList, scrollRect));
            }

            adjustCoroutine = null;
        }

        /// <summary>
        /// 드롭다운 리스트가 열려있는 동안 현재 선택된 GameObject를 감시한다.
        /// 선택이 변경될 때만 Viewport 가시 범위를 검사해 필요 시 스크롤을 조정한다.
        /// 드롭다운 리스트가 사라지면 코루틴을 자동 종료한다.
        /// </summary>
        /// <param name="dropdownList">드롭다운 리스트 루트 Transform</param>
        /// <param name="scrollRect">드롭다운 내부 ScrollRect</param>
        private IEnumerator TrackSelectedItemForScroll(Transform dropdownList, ScrollRect scrollRect)
        {
            GameObject lastSelected = null;

            while (dropdownList != null && dropdownList.gameObject.activeInHierarchy)
            {
                GameObject current = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;

                if (current != null && current != lastSelected && scrollRect.content != null && current.transform.IsChildOf(scrollRect.content))
                {
                    lastSelected = current;
                    RectTransform targetRect = current.transform as RectTransform;
                    if (targetRect != null)
                    {
                        EnsureSelectedVisible(scrollRect, targetRect);
                    }
                }

                yield return null;
            }

            trackingCoroutine = null;
        }

        /// <summary>
        /// 선택된 아이템이 Viewport 경계를 벗어나면 content.anchoredPosition을 조정하여 시야 안으로 들어오게 한다.
        /// 이미 가시 범위 안에 있으면 아무 동작도 하지 않는다.
        /// </summary>
        /// <param name="scrollRect">대상 ScrollRect</param>
        /// <param name="target">선택된 아이템 RectTransform</param>
        private void EnsureSelectedVisible(ScrollRect scrollRect, RectTransform target)
        {
            RectTransform content = scrollRect.content;
            RectTransform viewport = scrollRect.viewport;
            if (content == null || viewport == null || content.parent == null)
            {
                return;
            }

            Canvas.ForceUpdateCanvases();

            Vector3[] viewCorners = new Vector3[4];
            Vector3[] targetCorners = new Vector3[4];
            viewport.GetWorldCorners(viewCorners);
            target.GetWorldCorners(targetCorners);

            float viewTopY = viewCorners[1].y;
            float viewBottomY = viewCorners[0].y;
            float targetTopY = targetCorners[1].y;
            float targetBottomY = targetCorners[0].y;

            if (targetTopY <= viewTopY && targetBottomY >= viewBottomY)
            {
                return;
            }

            float worldDeltaY;
            if (targetTopY > viewTopY)
            {
                worldDeltaY = targetTopY - viewTopY;
            }
            else
            {
                worldDeltaY = targetBottomY - viewBottomY;
            }

            Vector3 worldStart = content.position;
            Vector3 worldEnd = worldStart;
            worldEnd.y -= worldDeltaY;

            Vector3 localStart = content.parent.InverseTransformPoint(worldStart);
            Vector3 localEnd = content.parent.InverseTransformPoint(worldEnd);

            Vector2 pos = content.anchoredPosition;
            pos.y += (localEnd.y - localStart.y);
            content.anchoredPosition = pos;
        }

        public void RefreshShownValue()
        {
            dropdown.RefreshShownValue();
        }
    }

}
