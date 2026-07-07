using System;
using Border.Core;
using Border.Events;

namespace Border.Localization
{
    /// <summary>
    /// 로컬라이즈 문자열 조회와 언어 변경 통지를 제공하는 추상화.
    /// UI 컴포넌트는 구체 <see cref="LocalizationManager"/>(또는 게임의 서비스 로케이터) 대신
    /// 이 인터페이스에 의존하므로, 특정 프로젝트의 Managers 시스템에 묶이지 않는다.
    /// </summary>
    public interface ILocalizationProvider
    {
        /// <summary>키에 해당하는 현재 언어 번역을 반환한다. 없으면 폴백 또는 키 자체를 반환한다.</summary>
        string Get(string key);

        /// <summary>언어가 변경될 때 발생한다.</summary>
        event Action OnLanguageChanged;

        /// <summary>현재 활성 언어 코드이다.</summary>
        string CurrentLanguageCode { get; }
    }

}
