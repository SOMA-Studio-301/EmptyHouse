using System;

/// <summary>
/// 한 번의 플레이(런) 진행 데이터. 설정과 달리 "데이터 삭제"의 대상이다 — <see cref="SaveLoadSystem.DeleteRun"/> 이 이 파일만 지운다.
/// 필드는 런 저장 항목(인벤토리·오일·해금 등)이 설계로 확정되면 채운다.
/// JsonUtility 직렬화 대상이므로 필드는 public 이어야 한다.
/// </summary>
[Serializable]
public class RunSave
{
}
