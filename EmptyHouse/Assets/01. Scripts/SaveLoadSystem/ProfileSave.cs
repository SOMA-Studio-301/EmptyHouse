using System;
using Border.Settings;

/// <summary>
/// 기기 단위로 영속되는 프로필 데이터. 현재는 설정값(오디오·그래픽·언어)만 담는다.
/// 런 진행 데이터는 <see cref="RunSave"/> 가 별도 파일로 담당한다 — 데이터 삭제는 RunSave 만 지우므로 설정은 보존된다.
/// JsonUtility 직렬화 대상이므로 필드는 public 이어야 하며, 필드명이 곧 JSON 키가 된다.
/// </summary>
[Serializable]
public class ProfileSave
{
    public float MasterVolume = 1f;
    public float MusicVolume = 0.8f;
    public float SfxVolume = 1f;
    public float AmbientVolume = 1f;

    public int ResolutionIndex = 0;
    public int WindowModeIndex = SettingsGraphicsUtility.BorderlessWindowModeIndex;

    public string LanguageCode = "ko";
}
