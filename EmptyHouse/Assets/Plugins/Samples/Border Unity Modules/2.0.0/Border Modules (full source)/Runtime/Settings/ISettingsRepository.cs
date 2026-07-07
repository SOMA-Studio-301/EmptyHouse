using Border.Core;
using Border.Events;
using Border.UI;
using Border.Localization;

namespace Border.Settings
{
    /// <summary>
    /// 설정값의 영속(저장/로드)을 추상화한다.
    /// Settings 모듈은 게임의 구체 저장 시스템(SaveLoadSystem/ProfileSave) 대신 이 인터페이스에 의존한다.
    /// 저장 포맷과 필드 매핑을 아는 구현체는 소비 게임(또는 별도 save-load 모듈)이 제공한다.
    /// </summary>
    public interface ISettingsRepository
    {
        /// <summary>저장소에 보관된 값으로 <paramref name="target"/> 를 채운다.</summary>
        void Load(SettingsSO target);

        /// <summary><paramref name="source"/> 의 현재 값을 저장소에 반영하고 디스크에 영속화한다.</summary>
        void Save(SettingsSO source);
    }

}
