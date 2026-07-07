using UnityEngine;
using UnityEngine.Events;

namespace Border.Events
{
    /// <summary>
    /// Screen fade in/out event channel. A fade controller subscribes and tweens a fullscreen
    /// Image's color/alpha; any system (scene transition, dialog, gimmick) requests fades through
    /// this channel without referencing the controller. The channel itself has no tween/render
    /// dependency — that lives in the listener.
    /// </summary>
    [CreateAssetMenu(fileName = "FadeChannelSO", menuName = "Border/Events/UI/Fade Channel")]
    public class FadeChannelSO : ScriptableObject
    {
        /// <summary>Payload: (fadeIn, duration, targetColor).</summary>
        public UnityAction<bool, float, Color> OnEventRaised = delegate { };

        /// <summary>Fade the screen into gameplay (opaque → clear).</summary>
        public void FadeIn(float duration) => Fade(true, duration, Color.clear);

        /// <summary>Fade the screen out (clear → black).</summary>
        public void FadeOut(float duration) => Fade(false, duration, Color.black);

        /// <summary>General fade. Subscribers receive (fadeIn, duration, color).</summary>
        public void Fade(bool fadeIn, float duration, Color color) => OnEventRaised?.Invoke(fadeIn, duration, color);
    }
}
