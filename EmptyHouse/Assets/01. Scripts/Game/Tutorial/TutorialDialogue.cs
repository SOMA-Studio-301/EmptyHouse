using System;
using System.Text.RegularExpressions;
using Border.Core;
using Border.Localization;
using TMPro;
using UnityEngine;

/// <summary>
/// 튜토리얼 전용 비차단 대화창 (튜토리얼.md 3장).
/// 게임을 멈추지 않고(3-1), 자동 넘김 없이 플레이어 입력으로만 다음 대사로 진행한다(3-3).
/// 대사 원고는 로컬라이즈 시트가 원천이며 여기에는 키만 둔다 — 문자열 하드코딩 금지.
/// UILocalizeText 를 쓰지 않는 이유: 대사 본문의 키 표기({key:액션} 토큰)를 런타임 바인딩으로 치환하는
/// 단계가 필요해서다(3-3 · AC "키 표기가 리바인딩을 따라 바뀐다"). 조회·치환·표시를 이 컴포넌트가 직접 한다.
/// </summary>
public sealed class TutorialDialogue : MonoBehaviour
{
    /// <summary>단계 하나가 재생할 대사 키 목록. 키 규칙 = tut_&lt;stage&gt;_&lt;nn&gt;, 실패는 tut_&lt;stage&gt;_fail_&lt;nn&gt; (7장).</summary>
    [Serializable]
    private sealed class StageSequence
    {
        public TutorialStageKind Stage;                  // 이 시퀀스가 속한 단계
        [LocalizeKey] public string[] LineKeys;          // 재생 순서대로의 대사 키. 가변 길이 대사 목록이라 배열을 쓴다(§4 의 고정 옵션 셋 금지와 무관)
        [LocalizeKey] public string[] FailLineKeys;      // 실패 안내 대사 키(4-4-4 리스폰 직후 재생). 실패 개념이 있는 단계(S2_3)만 채운다 — 빈 배열 허용
    }

    [Header("Input")]
    [SerializeField] private InputReader inputReader; // 다음 대사 진행 입력 + 대사 본문의 키 표기 조회(GetBindingDisplayString). ⚠ 진행 키 미확정(핸드오프 미결) — E·좌클릭과 겹치면 안 된다

    [Header("View")]
    [SerializeField] private GameObject panelRoot; // 대화창 표시 루트. 재생 시작 시 켜고 종료 시 끈다 — 비재생 중에는 화면에 없다
    [SerializeField] private TMP_Text lineText; // 현재 대사 표시. 시트 원문을 그대로 꽂지 말 것 — 반드시 ResolveLine 치환 결과만 SetText 한다

    [Header("Sequences")]
    [SerializeField] private StageSequence[] sequences; // 단계별 대사 시퀀스. 원고 등재는 /localize-sheet

    public event Action<TutorialStageKind> SequenceCompleted; // 시퀀스의 마지막 대사가 넘겨졌을 때 발행(정규·실패 공통). TutorialDirector 가 구독한다

    public bool IsPlaying { get; private set; } // 시퀀스 재생 중 여부

    private static readonly Regex keyTokenRegex = new Regex(@"\{key:([^}]+)\}", RegexOptions.Compiled); // 대사 본문의 {key:액션명} 토큰 패턴. 치환 결과는 캐시하지 않는다 — 표시 시점 재조회라야 리바인드가 반영된다

    private TutorialStageKind currentStage; // 재생 중인 시퀀스의 단계
    private string[] activeKeys; // 재생 중인 키 배열(정규 LineKeys 또는 실패 FailLineKeys)
    private int lineIndex; // activeKeys 에서 현재 표시 중인 대사 인덱스
    private ILocalizationProvider subscribedLocalization; // 구독 시점의 프로바이더 캐시 — 씬 언로드 때 LocalizeManager 가 먼저 꺼지며 Current 가 null 이 되므로, 해제는 캐시로 한다

    /// <summary>진행 입력과 언어 변경(LocalizationManager.Current.OnLanguageChanged) 구독을 건다.</summary>
    private void OnEnable()
    {
        Log.D("[TutorialDialogue] OnEnable");

        // 진행 키 기획 미확정(핸드오프 미결) — 잠정 R(RadioEquip). 튜토리얼에 무전기가 없어 부작용 0. 확정 시 이 한 줄만 재배선.
        inputReader.RadioEquipEvent += HandleAdvanceRequested;
        subscribedLocalization = LocalizationManager.Current;
        subscribedLocalization.OnLanguageChanged += HandleLanguageChanged;
    }

    /// <summary>OnEnable 과 짝을 맞춰 진행 입력·언어 변경 구독을 해제한다.</summary>
    private void OnDisable()
    {
        Log.D("[TutorialDialogue] OnDisable");

        inputReader.RadioEquipEvent -= HandleAdvanceRequested;
        // 언로드 순서 레이스 가드 — 프로바이더가 먼저 파괴된 프레임에는 해제할 대상 자체가 없다(배선 누락이 아니라 테어다운 순서 문제).
        if (subscribedLocalization != null)
        {
            subscribedLocalization.OnLanguageChanged -= HandleLanguageChanged;
            subscribedLocalization = null;
        }
    }

    /// <summary>해당 단계의 정규 시퀀스(LineKeys)를 첫 대사부터 재생한다. 시퀀스가 비어 있으면 즉시 SequenceCompleted 를 발행한다.</summary>
    /// <param name="stage">재생할 단계.</param>
    public void Play(TutorialStageKind stage)
    {
        Log.D($"[TutorialDialogue] Play {stage}");
        PlaySequence(stage, useFailKeys: false);
    }

    /// <summary>해당 단계의 실패 안내 시퀀스(FailLineKeys)를 재생한다. S2_3 리스폰 직후 TutorialDirector 가 호출한다(4-4-4).</summary>
    /// <param name="stage">실패한 단계.</param>
    public void PlayFail(TutorialStageKind stage)
    {
        Log.D($"[TutorialDialogue] PlayFail {stage}");
        PlaySequence(stage, useFailKeys: true);
    }

    /// <summary>정규·실패 공통 재생 진입 — 단계의 시퀀스를 선형 탐색해 첫 대사부터 표시한다. 미등재·빈 시퀀스는 창을 띄우지 않고 즉시 완료를 발행한다.</summary>
    /// <param name="stage">재생할 단계.</param>
    /// <param name="useFailKeys">true 면 FailLineKeys, false 면 LineKeys 를 재생한다.</param>
    private void PlaySequence(TutorialStageKind stage, bool useFailKeys)
    {
        StageSequence sequence = null;
        for (int i = 0; i < sequences.Length; i++)
        {
            if (sequences[i].Stage == stage)
            {
                sequence = sequences[i];
                break;
            }
        }

        string[] keys = sequence == null ? null : (useFailKeys ? sequence.FailLineKeys : sequence.LineKeys);
        if (keys == null || keys.Length == 0)
        {
            // 미등재 시퀀스가 단계 진행을 막지 않도록 창 없이 즉시 완료만 알린다.
            Log.D($"[TutorialDialogue] {stage} 시퀀스 없음/비어 있음(fail={useFailKeys}) — 즉시 완료 발행");
            SequenceCompleted?.Invoke(stage);
            return;
        }

        // 단계 전진이 재생 중 대사를 대체한다 — 비차단이라 놓친 대사는 되돌리지 않는다(3-3).
        currentStage = stage;
        activeKeys = keys;
        lineIndex = 0;
        IsPlaying = true;
        panelRoot.SetActive(true);
        ShowCurrentLine();
    }

    /// <summary>진행 입력 처리 — 다음 대사를 표시하고, 마지막 대사였다면 창을 닫고 SequenceCompleted 를 발행한다.</summary>
    private void HandleAdvanceRequested()
    {
        Log.D("[TutorialDialogue] HandleAdvanceRequested");
        if (!IsPlaying) return;

        lineIndex++;
        if (lineIndex < activeKeys.Length)
        {
            ShowCurrentLine();
            return;
        }

        // 마지막 대사를 넘겼다 — 상태를 먼저 정리하고 완료를 발행한다(구독자가 곧바로 다음 Play 를 호출해도 안전).
        TutorialStageKind completedStage = currentStage;
        panelRoot.SetActive(false);
        IsPlaying = false;
        activeKeys = null;
        SequenceCompleted?.Invoke(completedStage);
    }

    /// <summary>현재 인덱스의 대사 키를 치환해 lineText 에 표시한다.</summary>
    private void ShowCurrentLine()
    {
        lineText.SetText(ResolveLine(activeKeys[lineIndex]));
    }

    /// <summary>
    /// 대사 키를 표시 문자열로 변환한다 — LocalizationManager.Current.Get 조회 후, 본문의
    /// {key:액션명} 토큰을 inputReader.GetBindingDisplayString(액션명) 결과로 치환한다(3-3 런타임 바인딩 조회).
    /// </summary>
    /// <param name="lineKey">대사 로컬라이즈 키.</param>
    /// <returns>키 표기가 치환된 표시 문자열.</returns>
    private string ResolveLine(string lineKey)
    {
        Log.D($"[TutorialDialogue] ResolveLine {lineKey}");

        string line = LocalizationManager.Current.Get(lineKey);
        // 표시 시점마다 바인딩을 재조회한다 — 리바인드·스킴 전환이 자동 반영된다(캐시 금지).
        return keyTokenRegex.Replace(line, match => inputReader.GetBindingDisplayString(match.Groups[1].Value));
    }

    /// <summary>언어 변경 처리 — 표시 중인 대사를 현재 언어로 다시 치환해 갱신한다.</summary>
    private void HandleLanguageChanged()
    {
        Log.D("[TutorialDialogue] HandleLanguageChanged");
        if (!IsPlaying) return;

        ShowCurrentLine();
    }
}
