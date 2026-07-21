using Border.Core;
using Steamworks;
using Unity.Collections;
using Unity.Netcode;

/// <summary>
/// 플레이어의 표시 신원(스팀 닉네임·스팀 ID)을 전 클라에 복제하는 컴포넌트.
/// 게임 씬에는 UGS 로비 스냅샷과 NGO clientId 를 이어줄 키가 없어, 신원을 폰에 실어 복제한다 —
/// 관전 UI 가 사망자 목록과 현재 관전 대상을 지목하려면 clientId 단위 신원이 필요하기 때문이다.
/// 스팀은 클라 로컬 API 라 소유자만 읽을 수 있으므로, 소유자가 스폰 직후 서버에 제출하고 서버가 써서 복제한다(서버 권위).
/// </summary>
public class PlayerIdentity : NetworkBehaviour
{
    private const int MaxNameLength = 30; // FixedString128Bytes(125바이트) 한도 방어용 문자 수 상한. 한글 3바이트·이모지 4바이트 기준 보수적 값

    private readonly NetworkVariable<FixedString128Bytes> playerName = new NetworkVariable<FixedString128Bytes>(); // 표시 닉네임. 서버가 쓰고 전 클라가 읽는다
    private readonly NetworkVariable<FixedString128Bytes> steamId = new NetworkVariable<FixedString128Bytes>();    // 아바타 조회용 스팀 ID. 서버가 쓰고 전 클라가 읽는다

    public NetworkVariable<FixedString128Bytes> PlayerName => playerName; // 복제 닉네임 — 관전 UI 표시용
    public NetworkVariable<FixedString128Bytes> SteamId => steamId;       // 복제 스팀 ID — 아바타 로드용

    /// <summary>소유자에 한해 로컬 스팀 신원을 읽어 서버에 제출한다. 나머지 클라는 복제만 받는다.</summary>
    public override void OnNetworkSpawn()
    {
        if (!IsOwner) return;

        SubmitIdentityServerRpc(ReadLocalName(), ReadLocalSteamId());
    }

    /// <summary>소유자가 제출한 신원을 서버가 복제 변수에 기록한다.</summary>
    /// <param name="name">표시 닉네임.</param>
    /// <param name="steam">스팀 ID 문자열. 스팀 미초기화면 빈 문자열.</param>
    [ServerRpc]
    private void SubmitIdentityServerRpc(FixedString128Bytes name, FixedString128Bytes steam)
    {
        Log.D($"[PlayerIdentity] SubmitIdentityServerRpc {OwnerClientId} name={name}");

        playerName.Value = name;
        steamId.Value = steam;
    }

    /// <summary>로컬 스팀 닉네임을 읽는다. 스팀 미초기화면 clientId 기반 대체 이름을 쓴다.</summary>
    /// <returns>길이가 잘린 표시 닉네임.</returns>
    private FixedString128Bytes ReadLocalName()
    {
        string name = SteamManager.Initialized ? SteamFriends.GetPersonaName() : $"Player {OwnerClientId}";
        if (name.Length > MaxNameLength) name = name.Substring(0, MaxNameLength);

        return new FixedString128Bytes(name);
    }

    /// <summary>로컬 스팀 ID 문자열을 읽는다. 스팀 미초기화면 빈 문자열이라 아바타는 표시되지 않는다.</summary>
    /// <returns>스팀 ID 문자열.</returns>
    private FixedString128Bytes ReadLocalSteamId()
    {
        return new FixedString128Bytes(SteamManager.Initialized ? SteamUser.GetSteamID().ToString() : string.Empty);
    }
}
