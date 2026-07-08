using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Border.Core;
using Border.Events;
using Border.UI;
using Border.Localization;

namespace Border.Settings
{
    public static class SettingsGraphicsUtility
    {
        public const int FullScreenModeIndex = 0;
        public const int WindowedModeIndex = 1;
        public const int BorderlessWindowModeIndex = 2;

        private const int MinResolution = 1920;
        private const int MinRefreshRate = 30;

        public static List<Resolution> GetResolutionsList()
        {
            List<Resolution> resolutions = Screen.resolutions
                .Where(resolution =>
                    resolution.width >= MinResolution &&
                    Mathf.RoundToInt((float)resolution.refreshRateRatio.value) >= MinRefreshRate)
                .Distinct()
                .Reverse()
                .ToList();

            if (resolutions.Count == 0)
            {
                resolutions.Add(Screen.currentResolution);
            }

            return resolutions;
        }

        public static int GetValidatedResolutionIndex(IReadOnlyList<Resolution> resolutions, int resolutionIndex)
        {
            if (resolutions == null || resolutions.Count == 0)
            {
                return 0;
            }

            return Mathf.Clamp(resolutionIndex, 0, resolutions.Count - 1);
        }

        public static int GetValidatedWindowModeIndex(int modeIndex)
        {
            if (modeIndex < FullScreenModeIndex || modeIndex > BorderlessWindowModeIndex)
            {
                return BorderlessWindowModeIndex;
            }

            return modeIndex;
        }

        public static FullScreenMode GetFullScreenMode(int modeIndex)
        {
            switch (GetValidatedWindowModeIndex(modeIndex))
            {
                case FullScreenModeIndex:
                    return FullScreenMode.ExclusiveFullScreen;
                case BorderlessWindowModeIndex:
                    return FullScreenMode.FullScreenWindow;
                case WindowedModeIndex:
                default:
                    return FullScreenMode.Windowed;
            }
        }

        public static Resolution ApplyGraphicsSettings(int resolutionIndex, int windowModeIndex)
        {
            List<Resolution> resolutions = GetResolutionsList();
            int validatedResolutionIndex = GetValidatedResolutionIndex(resolutions, resolutionIndex);
            Resolution resolution = resolutions[validatedResolutionIndex];

            Screen.SetResolution(
                resolution.width,
                resolution.height,
                GetFullScreenMode(windowModeIndex));

            return resolution;
        }
    }

}
