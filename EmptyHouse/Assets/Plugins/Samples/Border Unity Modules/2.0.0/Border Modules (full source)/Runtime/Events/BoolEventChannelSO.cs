using UnityEngine;
using UnityEngine.Events;

namespace Border.Events
{
    /// <summary>Event channel carrying a <see cref="bool"/> payload.</summary>
    [CreateAssetMenu(fileName = "BoolEventChannelSO", menuName = "Border/Events/Bool")]
    public class BoolEventChannelSO : ScriptableObject
    {
        public UnityAction<bool> OnEventRaised = delegate { };

        public void RaiseEvent(bool value)
        {
            OnEventRaised?.Invoke(value);
        }
    }
}
