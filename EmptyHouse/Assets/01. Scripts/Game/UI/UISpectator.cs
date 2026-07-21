using System.Collections.Generic;
using Border.Core;
using TMPro;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 관전 화면 HUD(조작상호작용UI.md 3-8-1). 켜고 끄는 일은 <see cref="UIHud"/> 소유이고, 이 컴포넌트는 켜져 있는 동안의 내용만 책임진다.
/// 사망자 목록(본인 포함)을 그리고, 현재 관전 대상의 닉네임을 표시하며, 좌우 버튼을 대상 순환 요청으로 번역한다.
/// 카메라·대상 선정은 폰의 <see cref="PlayerSpectatorController"/> 몫이라 <see cref="SpectatorEventChannelSO"/> 로만 오간다 —
/// 버튼 클릭은 ←/→ 키 입력과 같은 CycleTarget 경로로 합류한다.
/// 신원(닉네임·스팀 ID)은 폰에 실려 복제되는 <see cref="PlayerIdentity"/> 에서 읽는다. 게임 씬에는 UGS 로비 스냅샷이 복제되지 않기 때문이다.
/// </summary>
public class UISpectator : MonoBehaviour
{
    [Header("Spectator Channel")]
    [SerializeField] private SpectatorEventChannelSO spectator; // 대상 변경 수신 · 순환 요청 발행

    [Header("Dead User List")]
    [SerializeField] private UISpectatorUser userPrefab; // 사망자 행 프리팹. 사망자 수만큼 생성한다
    [SerializeField] private Transform userListRoot;     // 생성한 행을 붙일 부모(UserList). 레이아웃 그룹이 정렬을 맡는다

    [Header("Current Target")]
    [SerializeField] private TMP_Text targetNameText;   // 현재 관전 중인 생존 플레이어 닉네임
    [SerializeField] private UIGenericButton leftButton;  // 이전 대상
    [SerializeField] private UIGenericButton rightButton; // 다음 대상

    private readonly List<PlayerDeathHandler> subscribedDeaths = new List<PlayerDeathHandler>(); // 사망 상태 구독 중인 플레이어들. 해제 짝을 맞추려 구독 시점 목록을 그대로 들고 있는다

    /// <summary>채널·버튼·로스터 구독을 걸고 사망자 목록을 처음 그린다. 패널이 켜지는 시점 = 관전 진입 시점이다.</summary>
    private void OnEnable()
    {
        spectator.OnTargetChanged += HandleTargetChanged;
        leftButton.Clicked += RequestPrevious;
        rightButton.Clicked += RequestNext;

        SubscribeDeathStates();
        RebuildDeadList();
    }

    /// <summary>구독을 해제하고 만들어 둔 행을 정리한다. OnEnable 과 짝을 맞춘다.</summary>
    private void OnDisable()
    {
        spectator.OnTargetChanged -= HandleTargetChanged;
        leftButton.Clicked -= RequestPrevious;
        rightButton.Clicked -= RequestNext;

        UnsubscribeDeathStates();
        ClearDeadList();
    }

    /// <summary>이전 대상 순환을 요청한다. ←키(InputReader.PreviousEvent)와 같은 경로로 합류한다.</summary>
    private void RequestPrevious()
    {
        spectator.RaiseCycleRequested(-1);
    }

    /// <summary>다음 대상 순환을 요청한다. →키(InputReader.NextEvent)와 같은 경로로 합류한다.</summary>
    private void RequestNext()
    {
        spectator.RaiseCycleRequested(1);
    }

    /// <summary>관전 대상 변경을 받아 닉네임 라벨을 갱신한다. 대상 폰을 못 찾으면 라벨을 비운다(despawn 경합 방어).</summary>
    /// <param name="clientId">새 관전 대상의 clientId.</param>
    private void HandleTargetChanged(ulong clientId)
    {
        Log.D($"[UISpectator] HandleTargetChanged {clientId}");

        PlayerIdentity identity = FindIdentity(clientId);
        targetNameText.text = identity != null ? identity.PlayerName.Value.ToString() : string.Empty;
    }

    /// <summary>전 플레이어의 복제 사망 상태를 구독한다. 누가 죽든 목록이 즉시 따라가게 하는 유일한 신호원이다.</summary>
    private void SubscribeDeathStates()
    {
        foreach (NetworkObject player in NetworkManager.Singleton.SpawnManager.PlayerObjects)
        {
            PlayerDeathHandler deathHandler = player.GetComponent<PlayerDeathHandler>();
            deathHandler.IsDead.OnValueChanged += HandleDeathStateChanged;
            subscribedDeaths.Add(deathHandler);
        }
    }

    /// <summary>구독해 둔 사망 상태를 전부 해제한다. 폰이 먼저 despawn 됐어도 참조는 살아 있으므로 목록 기준으로 뗀다.</summary>
    private void UnsubscribeDeathStates()
    {
        foreach (PlayerDeathHandler deathHandler in subscribedDeaths)
        {
            if (deathHandler == null) continue;
            deathHandler.IsDead.OnValueChanged -= HandleDeathStateChanged;
        }

        subscribedDeaths.Clear();
    }

    /// <summary>누군가의 사망 상태가 바뀌면 목록을 다시 그린다. 인원이 최대 몇 명이라 부분 갱신 없이 통째로 재구성한다.</summary>
    /// <param name="previous">이전 상태(미사용).</param>
    /// <param name="current">새 상태(미사용).</param>
    private void HandleDeathStateChanged(bool previous, bool current)
    {
        RebuildDeadList();
    }

    /// <summary>사망한 플레이어(본인 포함)를 행 프리팹 생성으로 다시 그린다.</summary>
    private void RebuildDeadList()
    {
        ClearDeadList();

        foreach (NetworkObject player in NetworkManager.Singleton.SpawnManager.PlayerObjects)
        {
            if (!player.GetComponent<PlayerDeathHandler>().IsDead.Value) continue;

            PlayerIdentity identity = player.GetComponent<PlayerIdentity>();
            UISpectatorUser row = Instantiate(userPrefab, userListRoot);
            row.SetPlayerInfo(identity.PlayerName.Value.ToString(), identity.SteamId.Value.ToString());
        }
    }

    /// <summary>
    /// UserList 아래를 전부 비운다. 생성분만 골라 지우지 않고 자식을 통째로 버리는 이유는,
    /// 프리팹에 편집용 더미 행이 남아 있어도 실제 목록과 섞이지 않게 하기 위해서다.
    /// 행이 소유한 아바타 텍스처는 각 행의 OnDestroy 가 정리한다.
    /// </summary>
    private void ClearDeadList()
    {
        for (int i = userListRoot.childCount - 1; i >= 0; i--)
        {
            Destroy(userListRoot.GetChild(i).gameObject);
        }
    }

    /// <summary>clientId 로 해당 플레이어의 신원 컴포넌트를 찾는다.</summary>
    /// <param name="clientId">찾을 클라이언트 ID.</param>
    /// <returns>찾은 신원 컴포넌트. 해당 폰이 없으면 null.</returns>
    private PlayerIdentity FindIdentity(ulong clientId)
    {
        foreach (NetworkObject player in NetworkManager.Singleton.SpawnManager.PlayerObjects)
        {
            if (player.OwnerClientId == clientId) return player.GetComponent<PlayerIdentity>();
        }

        return null;
    }
}
