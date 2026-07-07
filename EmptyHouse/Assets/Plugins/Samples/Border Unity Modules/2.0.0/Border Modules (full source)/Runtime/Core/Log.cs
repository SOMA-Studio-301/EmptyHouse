using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Border.Core
{
    /// <summary>
    /// Thin logging facade. All calls are marked <see cref="ConditionalAttribute"/> on
    /// <c>UNITY_EDITOR</c>, so every log — including warnings and errors — is stripped from
    /// player builds at compile time (the argument expressions are not even evaluated).
    /// Use this instead of <see cref="UnityEngine.Debug"/> for development-only diagnostics.
    /// </summary>
    public static class Log
    {
        [Conditional("UNITY_EDITOR")]
        public static void D(object message) => Debug.Log(message);

        [Conditional("UNITY_EDITOR")]
        public static void D(object message, Object context) => Debug.Log(message, context);

        [Conditional("UNITY_EDITOR")]
        public static void W(object message) => Debug.LogWarning(message);

        [Conditional("UNITY_EDITOR")]
        public static void W(object message, Object context) => Debug.LogWarning(message, context);

        [Conditional("UNITY_EDITOR")]
        public static void E(object message) => Debug.LogError(message);

        [Conditional("UNITY_EDITOR")]
        public static void E(object message, Object context) => Debug.LogError(message, context);
    }
}
