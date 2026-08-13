using Border.Core;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 결과창 개인 칭호 방송 채널(게임 전용). 서버→클라 전달은 ServerGameManager 의 ClientRpc 가 맡고,
/// 각 클라의 RPC 수신부가 이 채널로 로컬 방송한다 — SO 채널은 네트워크를 타지 않는다.
/// ClientRpc 도착과 결과창 표시(NetworkVariable 경로)의 순서가 보장되지 않으므로 마지막 페이로드를 캐시한다:
/// UIResult 는 표시 시점에 CurrentTitles 를 읽고, 늦은 도착은 구독으로 갱신한다. 게임 전용 타입이라 패키지가 아닌 게임 코드에 둔다.
/// </summary>
[CreateAssetMenu(fileName = "PlayerTitlesEventChannelSO", menuName = "Events/PlayerTitles")]
public class PlayerTitlesEventChannelSO : ScriptableObject
{
    public UnityAction<PlayerTitle[]> OnEventRaised = delegate { };

    /// <summary>
    /// 마지막으로 방송된 칭호 배열. 방송 전이면 null — 결과창은 폴백 키로 표시한다.
    /// 자동 프로퍼티라 직렬화되지 않는 런타임 전용 값이다.
    /// </summary>
    public PlayerTitle[] CurrentTitles { get; private set; }

    /// <summary>SO 활성화 시 캐시를 비운다. 에디터에서 SO 는 플레이 세션 사이에 살아남으므로 이전 세션 값이 남지 않게 한다.</summary>
    private void OnEnable()
    {
        // TODO(impl): CurrentTitles 초기화.
        Log.D("[PlayerTitlesEventChannelSO] OnEnable");
    }

    /// <summary>칭호 배열을 캐시하고 모든 구독자에게 방송한다(각 클라의 RPC 수신부에서 호출).</summary>
    /// <param name="titles">플레이어별 칭호. 로스터 전원 분.</param>
    public void RaiseEvent(PlayerTitle[] titles)
    {
        // TODO(impl): CurrentTitles 캐시 갱신 후 OnEventRaised 방송.
        Log.D($"[PlayerTitlesEventChannelSO] RaiseEvent {titles.Length}명");
    }
}
