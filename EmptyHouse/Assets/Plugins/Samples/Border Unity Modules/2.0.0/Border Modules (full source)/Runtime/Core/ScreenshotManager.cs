using System.IO;
using UnityEngine;

namespace Border.Core
{
    /// <summary>
    /// Development helper: press a hotkey to capture the current Game View at its native
    /// resolution and write a timestamped PNG to <see cref="Application.persistentDataPath"/>.
    /// </summary>
    /// <remarks>
    /// Uses the legacy <see cref="Input"/> polling API, so it requires "Active Input Handling"
    /// to be set to "Both" or "Input Manager (Old)". Editor/development use only.
    /// </remarks>
    public class ScreenshotManager : MonoBehaviour
    {
        [Tooltip("Key that triggers a screenshot capture.")]
        [SerializeField] private KeyCode captureKey = KeyCode.F12;

        private void Update()
        {
            if (!Input.GetKeyDown(captureKey)) return;

            // Timestamped name avoids overwriting previous captures.
            string fileName = "Screenshot_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png";
            string fullPath = Path.Combine(Application.persistentDataPath, fileName);
            ScreenCapture.CaptureScreenshot(fullPath);
            Log.D($"Screenshot saved: {fullPath}");
        }
    }
}
