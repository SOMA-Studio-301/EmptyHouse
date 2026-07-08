using UnityEngine;
using UnityEngine.Events;

namespace Border.Events
{
    /// <summary>Event channel carrying a <see cref="float"/> payload.</summary>
    [CreateAssetMenu(fileName = "FloatEventChannelSO", menuName = "Border/Events/Float")]
    public class FloatEventChannelSO : ScriptableObject
    {
        public UnityAction<float> OnEventRaised = delegate { };

        public void RaiseEvent(float value)
        {
            OnEventRaised?.Invoke(value);
        }
    }
}
