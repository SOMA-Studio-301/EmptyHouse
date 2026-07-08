using UnityEngine;
using UnityEngine.Events;

namespace Border.Events
{
    /// <summary>Event channel carrying a <see cref="Vector2"/> payload.</summary>
    [CreateAssetMenu(fileName = "Vector2EventChannelSO", menuName = "Border/Events/Vector2")]
    public class Vector2EventChannelSO : ScriptableObject
    {
        public UnityAction<Vector2> OnEventRaised = delegate { };

        public void RaiseEvent(Vector2 value)
        {
            OnEventRaised?.Invoke(value);
        }
    }
}
