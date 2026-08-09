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
    Sfx_Door_Close = 100,
    Sfx_Door_Open = 101,
    Sfx_Flash_OnOff = 102,
    Sfx_Foot_Walk_Hard = 103,
    Sfx_Item_Drop = 104,
    Sfx_Item_Pickup = 105,
    Sfx_Player_Heartbeat = 106,
    Sfx_Power_Activate = 107,
    Sfx_Power_Interaction = 108,
    Sfx_Ui_Click = 109,
    Sfx_Ui_GameStart = 110,
    Sfx_Ui_SlotSelect = 111,
    Sfx_Ui_TabClose = 112,
    Sfx_Ui_TabOpen = 113,
    Sfx_Wardrobe_Close = 114,
    Sfx_Wardrobe_Open = 115,
    Sfx_Zombie_Chase0 = 116,
    Sfx_Zombie_Chase1 = 117,
    Sfx_Zombie_Chase2 = 118,
    Sfx_Zombie_Idle = 119,
    Sfx_Zombie_Roar = 120,

    // Ambient (500~)
    Amb_1F_Wind = 500,
    Amb_2F_Beep = 501,
    Amb_B1_Drone = 502,

    // BGM (1000~)
    Bgm_Alert_Sting = 1000,
    Bgm_Lobby_0 = 1001,
    Bgm_Lobby_1 = 1002,
}
