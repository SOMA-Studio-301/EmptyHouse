namespace Border.Audio
{
    /// <summary>
    /// AudioRegistrySO가 Cue/Configuration을 조회하는 키.
    /// 게임별로 필요한 항목을 여기에 추가해 사용한다. (예시 값만 제공 — 실제 매핑은 프로젝트에서 정의)
    /// </summary>
    public enum AudioId
    {
        None = 0,

        // SFX (100~)
        Sfx_Sample = 100,

        // BGM (1000~)
        Bgm_Sample = 1000,
    }
}
