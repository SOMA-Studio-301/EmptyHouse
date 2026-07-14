using UnityEngine;

/// <summary> 인벤토리 슬롯에 들어가는 아이템의 종류 </summary>
public enum ItemKind
{
    Key, // 열쇠_NN. 손에 쥐고 자물쇠_NN에 E — 개방 시 소멸(3-6)
    Throwable, // 투척 오브젝트. 좌클릭 투척 — 착지 소음으로 좀비 유인, 투척 즉시 소멸(4-3)
    Vaccine, // 백신. 최종 목표 수집물 — 버스 적재 시 인벤에서 빠짐
    Scrap, // 스크랩. 환금용 — 버스 정산 시 인벤에서 빠짐
    Oil // 기름. 버스 연료 — 홀드 적재로 회수(3-5)
}

/// <summary>
/// 아이템 종류별 정적 데이터
/// 종류당 에셋 1개를 만들고, 픽업 프리팹이 이를 참조
/// </summary>
[CreateAssetMenu(fileName = "SO_Item_", menuName = "Game/Data/Item Data")]
public class ItemDataSO : ScriptableObject
{
    [Header("표시")]
    [Tooltip("아이템 표시 이름")]
    public string ItemName;

    [Tooltip("HUD 인벤 3칸 표시용 아이콘(5장)")]
    public Sprite Icon;

    [Header("분류")]
    [Tooltip("아이템 종류. 사용 분기(투척·개방·적재)의 기준")]
    public ItemKind Kind;

    [Header("회수 (3-5)")]
    [Tooltip("활성 프롬프트 행위명 — \"회수\" / \"줍기\" / \"적재\"")]
    public string PickupVerb = "회수";

    [Tooltip("회수 실행 시 발행할 소음(dB) ⚪")]
    public float PickupNoiseDb = 12f;

    [Header("월드 복원")]
    [Tooltip("G 버리기 · 열쇠 소지자 사망 드롭 시 스폰할 픽업 프리팹")]
    public GameObject WorldPrefab;

    [Header("투척 (Throwable 전용)")]
    [Tooltip("좌클릭 투척 시 스폰할 비행체(ThrownProjectile) 프리팹. 다른 종류는 비워 둔다")]
    public GameObject ProjectilePrefab;
}
