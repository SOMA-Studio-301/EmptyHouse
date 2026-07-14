/// <summary>
/// 게임이 소유하는 사운드 식별자. AudioRegistrySO 가 Cue/Configuration 을 조회하는 키다.
/// Border 패키지에도 동명 enum 이 있으나 그것은 샘플 값뿐이고 패키지 소유라 확장할 수 없다 — 이쪽이 실제로 쓰이는 목록이다.
/// 번호대로 용도를 나눈다: SFX 100~, Ambient 500~, BGM 1000~.
/// 어느 믹서 버스로 나갈지는 이 enum 이 아니라 짝지어진 AudioConfigurationSO 의 OutputAudioMixerGroup 이 정한다.
/// </summary>
public enum AudioId
{
    None = 0,

    // SFX (100~)
    Sfx_Test_Beep = 100,

    // Ambient (500~)
    Amb_Test_Room = 500,

    // BGM (1000~)
    Bgm_Test_Tone = 1000,
}
