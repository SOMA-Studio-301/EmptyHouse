using System;
using System.Collections.Generic;
using UnityEngine;

namespace EmptyHouse.EditorTools
{
    /// <summary>
    /// HUD 아이콘 베이크 파라미터(에디터 전용).
    /// 대상 목록은 ItemDataSO 전수 스캔이 기본이고, ItemDataSO 가 없는 슬롯 아이템(무전기)만
    /// ExtraTargets 로 추가한다 — 대상 하드코딩 금지 원칙의 근거 에셋.
    /// </summary>
    [CreateAssetMenu(fileName = "SO_IconRenderSettings", menuName = "Game/Editor/Icon Render Settings")]
    public class IconRenderSettingsSO : ScriptableObject
    {
        /// <summary> 프리팹별 렌더 종횡비 오버라이드. 기름통 2칸 확정(M4) 시 2:1 대응용 </summary>
        [Serializable]
        public class AspectOverride
        {
            public GameObject Prefab; // 대상 픽업 프리팹
            public float Aspect = 1f; // 가로/세로 비. 2칸 아이템이면 2
        }

        /// <summary> 프리팹별 아이콘 방향 오버라이드. 카메라·조명은 불변(통일 유지) — 방향은 오브젝트 회전으로 만든다 </summary>
        [Serializable]
        public class RotationOverride
        {
            public GameObject Prefab; // 대상 픽업 프리팹
            public Vector3 Euler; // 인스턴스에 적용할 월드 오일러 회전. IconRender 씬에서 맞춘 Transform Rotation 값을 그대로 입력
        }

        [Header("리그")]
        public GameObject RigPrefab; // 카메라+조명 리그 프리팹. 베이크 시 임시 인스턴스화 후 파기

        [Header("출력")]
        public string OutputFolder = "Assets/04. Arts/UI/Icons"; // PNG 출력 폴더
        public string FileNamePrefix = "hud_icon_"; // 파일명 접두. 뒤에 프리팹 이름(소문자)이 붙는다
        public int RenderSize = 1024; // 슈퍼샘플 렌더 세로 해상도
        public int FinalSize = 256; // 최종 PNG 세로 해상도 (HUD 아이콘 영역 80px@1080p 기준 여유 2배 이상)
        public bool AssignIcons = true; // 베이크 후 ItemDataSO.Icon 자동 할당 여부

        [Header("프레이밍")]
        [Range(0.1f, 1f)] public float Padding = 0.85f; // 프레임 점유율. 1이면 꽉 참

        [Header("결정론")]
        public Color AmbientColor = new Color(0.12f, 0.12f, 0.14f); // 베이크 중 강제할 평면 앰비언트 — 씬과 무관하게 같은 입력 = 같은 픽셀

        [Header("추가 대상")]
        public List<GameObject> ExtraTargets = new List<GameObject>(); // ItemDataSO 스캔에 안 잡히는 슬롯 아이템 프리팹(무전기 등)
        public List<AspectOverride> AspectOverrides = new List<AspectOverride>(); // 종횡비 예외 목록. 비면 전부 1:1
        public List<RotationOverride> RotationOverrides = new List<RotationOverride>(); // 방향 예외 목록. 비면 전부 무회전
    }
}
