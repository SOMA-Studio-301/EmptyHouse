using Border.Localization;
using UnityEngine;

/// <summary> 인벤토리 슬롯에 들어가는 아이템의 종류 </summary>
public enum ItemKind
{
    Key, // 열쇠_NN. 손에 쥐고 자물쇠_NN에 E — 개방 시 소멸(3-6)
    Throwable, // 투척 오브젝트. 좌클릭 투척 — 착지 소음으로 좀비 유인, 투척 즉시 소멸(4-3)
    Vaccine, // 백신. 최종 목표 수집물 — 버스 적재 시 인벤에서 빠짐
    Scrap, // 스크랩. 환금용 — 버스 정산 시 인벤에서 빠짐
    Oil, // 기름. 버스 연료 — 홀드 적재로 회수(3-5)
    Radio // 무전기. 인벤 칸을 쓰지 않는 유일한 아이템 — 저장은 PlayerRadioSlot 전용 칸, 손에 들어야 송신(PTT) 가능. ※ 기존 SO 의 Kind 가 밀리지 않도록 반드시 끝에만 추가할 것
}

/// <summary> 아이템을 손에 들 때 쓰는 손의 개수 </summary>
public enum ItemHandUsage
{
    OneHand, // 한 손(왼손) 전담. 오른손 손전등과 공존한다 — 서로 간섭하지 않는다
    TwoHand // 양손 점유. 오른손을 빼앗으므로 드는 동안 손전등이 물리적으로 꺼진다
}

/// <summary>
/// 아이템 종류별 정적 데이터
/// 종류당 에셋 1개를 만들고, 픽업 프리팹이 이를 참조
/// </summary>
[CreateAssetMenu(fileName = "SO_Item_", menuName = "Game/Data/Item Data")]
public class ItemDataSO : ScriptableObject
{
    [Header("표시")]
    [Tooltip("아이템 표시 이름의 로컬라이즈 키")]
    [LocalizeKey] public string NameKey;

    [Tooltip("HUD 인벤 3칸 표시용 아이콘(5장)")]
    public Sprite Icon;

    [Header("분류")]
    [Tooltip("아이템 종류. 사용 분기(투척·개방·적재)의 기준")]
    public ItemKind Kind;

    [Header("회수 (3-5)")]
    [Tooltip("활성 프롬프트 행위명의 로컬라이즈 키 — INT_PROMPT_COLLECT / INT_PROMPT_PICKUP / INT_PROMPT_LOAD")]
    [LocalizeKey] public string PickupVerbKey = "INT_PROMPT_COLLECT";

    [Tooltip("회수 실행 시 발행할 소음(dB) ⚪")]
    public float PickupNoiseDb = 12f;

    [Header("손에 들기")]
    [Tooltip("드는 데 쓰는 손의 개수. TwoHand 는 손전등을 밀어낸다")]
    public ItemHandUsage HandUsage = ItemHandUsage.OneHand;

    [Tooltip("손 소켓에 붙일 표시 전용 모델(NetworkObject 아님 — 각 클라가 로컬 생성). " +
             "이 프리팹 루트의 Transform(위치·회전·스케일)이 곧 손 기준 그립 오프셋이다. 비워 두면 팔만 올라간다")]
    public GameObject HeldPrefab;

    [Header("월드 복원")]
    [Tooltip("G 버리기 · 열쇠 소지자 사망 드롭 시 스폰할 픽업 프리팹")]
    public GameObject WorldPrefab;

    [Header("투척 (Throwable 전용)")]
    [Tooltip("좌클릭 투척 시 스폰할 비행체(ThrownProjectile) 프리팹. 다른 종류는 비워 둔다")]
    public GameObject ProjectilePrefab;
}
