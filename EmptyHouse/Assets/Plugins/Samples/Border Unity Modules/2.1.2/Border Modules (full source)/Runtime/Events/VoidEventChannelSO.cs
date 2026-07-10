using UnityEngine;
using UnityEngine.Events;

namespace Border.Events
{
    /// <summary>Parameterless event channel. Create as an asset and reference it to broadcast a signal.</summary>
    [CreateAssetMenu(fileName = "VoidEventChannelSO", menuName = "Events/Void")]
    public class VoidEventChannelSO : ScriptableObject
    {
        public UnityAction OnEventRaised = delegate { };

        public void RaiseEvent()
        {
            OnEventRaised?.Invoke();
        }
    }
}
