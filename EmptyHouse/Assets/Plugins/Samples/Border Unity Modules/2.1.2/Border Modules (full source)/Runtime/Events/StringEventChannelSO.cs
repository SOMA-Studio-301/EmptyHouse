using UnityEngine;
using UnityEngine.Events;

namespace Border.Events
{
    /// <summary>Event channel carrying a <see cref="string"/> payload.</summary>
    [CreateAssetMenu(fileName = "StringEventChannelSO", menuName = "Border/Events/String")]
    public class StringEventChannelSO : ScriptableObject
    {
        public UnityAction<string> OnEventRaised = delegate { };

        public void RaiseEvent(string value)
        {
            OnEventRaised?.Invoke(value);
        }
    }
}
