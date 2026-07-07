using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Border.Core;
using Border.Events;
using Border.UI;
using Border.Localization;

namespace Border.Settings
{
    public class UISettingsSlider : MonoBehaviour
    {
        [SerializeField] private Slider slider;
        public Slider Slider => slider;
        // [SerializeField] private TextMeshProUGUI text;

        public UnityAction<float> ValueChanged;

        private void Awake()
        {
            slider.onValueChanged.AddListener(SliderValueChanged);
        }

        private void SliderValueChanged(float value)
        {
            ValueChanged?.Invoke(value);
        }

        public void SetSlider(float value)
        {
            slider.value = value;
        }

        public float GetValue()
        {
            return slider.value;
        }
    }

}
