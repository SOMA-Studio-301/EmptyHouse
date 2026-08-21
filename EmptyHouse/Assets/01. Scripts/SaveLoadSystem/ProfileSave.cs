using System;

/// <summary>
/// 기기 단위로 영속되는 프로필 데이터. 설정값(오디오·그래픽·언어)과 최초 실행 플래그를 담는다.
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
    public int WindowModeIndex = GraphicsSettingsUtility.BorderlessWindowModeIndex;

    public string LanguageCode = "ko";

    public float MouseSensitivity = 1f; // 시선 감도 배율. 플레이어의 기본 lookSensitivity 에 곱해진다(설정값은 배율일 뿐 절대값이 아니다)

    public bool IsFirstPlay = true; // 최초 실행 여부. SplashRouter 가 분기 직후 false 로 저장한다 — 튜토리얼 자동 진입은 최초 1회뿐(D39 예외 — 런 진행도가 아니라 타이틀 설정값. 튜토리얼.md 1-3)
}
