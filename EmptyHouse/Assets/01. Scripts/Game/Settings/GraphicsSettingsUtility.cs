using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 그래픽 설정(해상도·창 모드)의 목록 생성·검증·적용을 담당하는 게임 소유 유틸리티.
///
/// Border.Settings.SettingsGraphicsUtility 를 포크한 버전이다. 포크한 이유는 해상도 정책이 다르기 때문이다:
/// 패키지판은 주사율까지 구분해 같은 해상도를 Hz 별로 여러 항목으로 나열하지만,
/// 이 게임은 해상도를 (가로 x 세로) 단위로만 노출하고 주사율은 항상 그 해상도의 최댓값을 강제한다.
///
/// ResolutionIndex 규약(프로필에 저장되는 인덱스)이 목록 생성 함수에 묶여 있으므로,
/// 게임 코드는 반드시 이 유틸리티만 사용해야 한다 — 패키지판과 섞어 쓰면 인덱스가 어긋난다.
/// </summary>
public static class GraphicsSettingsUtility
{
    public const int FullScreenModeIndex = 0;        // 창 모드 인덱스 — 전체화면
    public const int WindowedModeIndex = 1;          // 창 모드 인덱스 — 창모드
    public const int BorderlessWindowModeIndex = 2;  // 창 모드 인덱스 — 테두리없는 창

    private const int MinResolution = 1920;  // 목록에 넣을 최소 가로 해상도
    private const int MinRefreshRate = 30;   // 목록에 넣을 최소 주사율(Hz). 주사율이 유효할 때만 적용된다

    /// <summary>선택 가능한 해상도 목록을 (가로 x 세로) 단위로 중복 제거해 내림차순으로 만든다. 같은 해상도가 여러 주사율로 존재하면 최대 주사율 항목만 남긴다.</summary>
    /// <returns>내림차순 정렬된 해상도 목록. 조건을 만족하는 항목이 없으면 현재 해상도 하나만 담는다.</returns>
    public static List<Resolution> GetResolutionsList()
    {
        // 주사율 하한은 그룹핑으로 최대 주사율을 고른 뒤에 본다 — 저주사율 변종 때문에 해상도 자체가 사라지면 안 된다.
        List<Resolution> resolutions = Screen.resolutions
            .Where(resolution => resolution.width >= MinResolution)
            .GroupBy(resolution => (resolution.width, resolution.height))
            .Select(group => group.OrderByDescending(resolution => resolution.refreshRateRatio.value).First())
            .Where(IsRefreshRateAllowed)
            .OrderByDescending(resolution => resolution.width)
            .ThenByDescending(resolution => resolution.height)
            .ToList();

        if (resolutions.Count == 0)
        {
            resolutions.Add(Screen.currentResolution);
        }

        return resolutions;
    }

    /// <summary>주사율 값이 쓸 수 있는 값인지 판단한다. 에디터의 Screen.resolutions 는 주사율이 0/0 으로 와 value 가 NaN 이다.</summary>
    /// <param name="resolution">검사할 해상도.</param>
    /// <returns>유효하면 true.</returns>
    private static bool HasValidRefreshRate(Resolution resolution)
    {
        double refreshRate = resolution.refreshRateRatio.value;
        return !double.IsNaN(refreshRate) && refreshRate > 0d;
    }

    /// <summary>주사율 하한을 통과하는지 판단한다. 주사율이 무효하면 판단할 근거가 없으므로 거르지 않는다 — 거르면 에디터에서 목록이 통째로 비어버린다.</summary>
    /// <param name="resolution">검사할 해상도.</param>
    /// <returns>목록에 넣어도 되면 true.</returns>
    private static bool IsRefreshRateAllowed(Resolution resolution)
    {
        return !HasValidRefreshRate(resolution) || resolution.refreshRateRatio.value >= MinRefreshRate;
    }

    /// <summary>해상도 인덱스를 목록 범위 안으로 보정한다.</summary>
    /// <param name="resolutions">기준이 되는 해상도 목록.</param>
    /// <param name="resolutionIndex">보정할 인덱스.</param>
    /// <returns>목록 범위로 clamp 된 인덱스. 목록이 비었으면 0.</returns>
    public static int GetValidatedResolutionIndex(IReadOnlyList<Resolution> resolutions, int resolutionIndex)
    {
        if (resolutions == null || resolutions.Count == 0)
        {
            return 0;
        }

        return Mathf.Clamp(resolutionIndex, 0, resolutions.Count - 1);
    }

    /// <summary>창 모드 인덱스를 유효 범위 안으로 보정한다.</summary>
    /// <param name="modeIndex">보정할 창 모드 인덱스.</param>
    /// <returns>0~2 범위의 인덱스. 범위를 벗어나면 테두리없는 창.</returns>
    public static int GetValidatedWindowModeIndex(int modeIndex)
    {
        if (modeIndex < FullScreenModeIndex || modeIndex > BorderlessWindowModeIndex)
        {
            return BorderlessWindowModeIndex;
        }

        return modeIndex;
    }

    /// <summary>창 모드 인덱스를 Unity 의 FullScreenMode 로 변환한다.</summary>
    /// <param name="modeIndex">창 모드 인덱스.</param>
    /// <returns>대응하는 FullScreenMode.</returns>
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

    /// <summary>해상도·창 모드를 화면에 적용한다. 주사율은 목록이 이미 최댓값으로 골라둔 값을 그대로 요청하되, 값이 무효하면 명시하지 않고 플랫폼에 맡긴다.</summary>
    /// <param name="resolutionIndex">해상도 목록 인덱스.</param>
    /// <param name="windowModeIndex">창 모드 인덱스.</param>
    /// <returns>실제로 적용된 해상도.</returns>
    public static Resolution ApplyGraphicsSettings(int resolutionIndex, int windowModeIndex)
    {
        List<Resolution> resolutions = GetResolutionsList();
        int validatedResolutionIndex = GetValidatedResolutionIndex(resolutions, resolutionIndex);
        Resolution resolution = resolutions[validatedResolutionIndex];
        FullScreenMode fullScreenMode = GetFullScreenMode(windowModeIndex);

        if (HasValidRefreshRate(resolution))
        {
            Screen.SetResolution(resolution.width, resolution.height, fullScreenMode, resolution.refreshRateRatio);
        }
        else
        {
            // 0/0 을 그대로 넘기면 플랫폼이 주사율을 못 정한다. 무효할 땐 아예 빼고 기본값을 쓰게 두는 편이 안전하다.
            Screen.SetResolution(resolution.width, resolution.height, fullScreenMode);
        }

        return resolution;
    }
}
