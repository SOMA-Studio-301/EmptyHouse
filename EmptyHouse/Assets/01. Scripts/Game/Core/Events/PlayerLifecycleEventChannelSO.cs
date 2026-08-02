using Border.Core;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 플레이어 생명주기 신호 채널(게임 전용). 게임플레이·네트워크 레이어가 반드시 서버 프로세스에서 raise 하고,
/// ServerGameManager 가 서버에서만 구독해 로스터에 반영한다(세션루프.md 3장 · 신호 배선 A안).
/// 합류/사망/귀환/포기는 서버에서 발화돼야 로스터·NetworkVariable 판정이 성립한다 — 클라 로컬 raise 는 그 클라에만 터진다.
/// 로스터의 모든 변경이 이 채널 하나로 흐르므로, 게임플레이·스포너 어느 쪽도 ServerGameManager 를 직접 참조하지 않는다.
/// clientId 로 대상을 지정한다. 게임 전용 타입이라 패키지가 아닌 게임 코드에 둔다.
/// </summary>
[CreateAssetMenu(fileName = "PlayerLifecycleEventChannelSO", menuName = "Events/PlayerLifecycle")]
public class PlayerLifecycleEventChannelSO : ScriptableObject
{
    /// <summary>플레이어 세션 합류(스폰 완료 → 로스터 Active 등록). clientId 전달.</summary>
    public UnityAction<ulong> OnJoined = delegate { };

    /// <summary>플레이어 사망(원턴킬 → D19 드롭·D13 관전). clientId 전달.</summary>
    public UnityAction<ulong> OnDied = delegate { };

    /// <summary>플레이어 귀환(버스 복귀 → 개인 단위, D36). clientId 전달.</summary>
    public UnityAction<ulong> OnExtracted = delegate { };

    /// <summary>플레이어 개인 포기(게임 나가기 → 원칙 2, D19 준용 드롭). clientId 전달.</summary>
    public UnityAction<ulong> OnLeft = delegate { };

    /// <summary>세션 합류를 방송한다(서버에서 호출). 스포너가 PlayerObject 스폰 직후 발화한다.</summary>
    /// <param name="clientId">합류한 클라이언트 ID.</param>
    public void RaiseJoined(ulong clientId)
    {
        //Log.D($"[PlayerLifecycleEventChannelSO] RaiseJoined {clientId}");
        OnJoined.Invoke(clientId);
    }

    /// <summary>사망을 방송한다(서버에서 호출).</summary>
    /// <param name="clientId">사망한 클라이언트 ID.</param>
    public void RaiseDied(ulong clientId)
    {
        //Log.D($"[PlayerLifecycleEventChannelSO] RaiseDied {clientId}");
        OnDied.Invoke(clientId);
    }

    /// <summary>귀환을 방송한다(서버에서 호출).</summary>
    /// <param name="clientId">귀환한 클라이언트 ID.</param>
    public void RaiseExtracted(ulong clientId)
    {
        //Log.D($"[PlayerLifecycleEventChannelSO] RaiseExtracted {clientId}");
        OnExtracted.Invoke(clientId);
    }

    /// <summary>개인 포기를 방송한다(서버에서 호출).</summary>
    /// <param name="clientId">포기한 클라이언트 ID.</param>
    public void RaiseLeft(ulong clientId)
    {
        //Log.D($"[PlayerLifecycleEventChannelSO] RaiseLeft {clientId}");
        OnLeft.Invoke(clientId);
    }
}
