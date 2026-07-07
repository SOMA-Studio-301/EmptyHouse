using UnityEngine;
using UnityEngine.Events;
using Border.Core;
using Border.Events;
using Border.UI;
using Border.Localization;

namespace Border.Settings
{
    public class UISettingsCheck : MonoBehaviour
    {
        [SerializeField] private UIGenericButton checkButton;
        [SerializeField] private Sprite onSprite;
        [SerializeField] private Sprite offSprite;

        public UnityAction<bool> OnValueChanged;

        private bool _isOn;

        private void Awake()
        {
            UpdateVisual();
        }

        private void OnEnable()
        {
            checkButton.Clicked += Toggle;
        }

        private void OnDisable()
        {
            checkButton.Clicked -= Toggle;
        }

        /// <summary>
        /// 체크 박스 클릭 시 토글 액션 실행
        /// </summary>
        private void Toggle()
        {
            _isOn = !_isOn;
            UpdateVisual();
            OnValueChanged?.Invoke(_isOn);
        }

        /// <summary>
        /// 값을 설정하고 이에 따라 체크 표시 이미지를 변경합니다.
        /// </summary>
        /// <param name="isOn">체크 상태 값입니다.</param>
        /// <param name="notify">값 변경 이벤트를 호출할지 여부입니다.</param>
        public void SetValue(bool isOn, bool notify = false)
        {   
            _isOn = isOn;
            UpdateVisual();
            if (notify)
                OnValueChanged?.Invoke(_isOn);
        } 

        /// <summary>
        /// 체크 버튼 이미지 상태에 따라 변경
        /// </summary>
        private void UpdateVisual()
        {
            if (checkButton != null)
            {
                checkButton.SetSprite(_isOn ? onSprite : offSprite);
            }
        }

        /// <summary>
        /// 현재 체크 상태를 반환합니다.
        /// </summary>
        public bool IsOn => _isOn;
    }

}
