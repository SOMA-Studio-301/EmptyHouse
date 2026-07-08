using UnityEngine;
using UnityEngine.Events;

namespace Border.Events
{
    /// <summary>Event channel carrying an <see cref="int"/> payload.</summary>
    [CreateAssetMenu(fileName = "IntEventChannelSO", menuName = "Border/Events/Int")]
    public class IntEventChannelSO : ScriptableObject
    {
        public UnityAction<int> OnEventRaised = delegate { };

        public void RaiseEvent(int value)
        {
            OnEventRaised?.Invoke(value);
        }
    }
}
