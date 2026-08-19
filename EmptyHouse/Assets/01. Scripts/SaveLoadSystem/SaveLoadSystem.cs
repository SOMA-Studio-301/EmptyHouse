using UnityEngine;
using Border.Core;

/// <summary>
/// 세이브 데이터의 단일 소유자이자 영속 계층. ProfileSave 와 RunSave 를 각각 별도 파일로 읽고 쓴다.
/// 두 파일을 분리하는 이유는 "데이터 삭제"가 런 진행만 날리고 설정(볼륨·해상도·언어)은 보존해야 하기 때문이다.
/// 값 자체는 에셋에 직렬화되지 않는다 — 디스크가 원본이고 이 SO 는 런타임 캐시다.
///
/// 로드는 "누가 어느 씬에서 부른다"가 아니라 첫 접근 시 자동으로 1회 일어난다.
/// 씬이 Menu/Lobby/Game 으로 나뉘고 에디터에선 아무 씬이나 바로 플레이하므로,
/// 특정 씬 컴포넌트에 로드를 맡기면 그 컴포넌트를 빠뜨린 씬에서 조용히 깨진다.
/// SO 는 씬 로드로 죽지 않으므로 캐시는 플레이 세션 내내 유지된다.
/// </summary>
[CreateAssetMenu(fileName = "SO_SaveLoadSystem", menuName = "EmptyHouse/Save/Save Load System")]
public class SaveLoadSystem : ScriptableObject
{
    private const string ProfileFileName = "profile.save";
    private const string RunFileName = "run.save";

    private ProfileSave profile = new ProfileSave();
    private RunSave run = new RunSave();

    private bool isProfileLoaded;
    private bool isRunLoaded;

    public ProfileSave Profile // 설정 등 기기 단위 영속 데이터. 첫 접근 시 디스크에서 자동 로드된다
    {
        get
        {
            EnsureProfileLoaded();
            return profile;
        }
    }

    public RunSave Run // 현재 런 진행 데이터. 데이터 삭제의 대상이다. 첫 접근 시 디스크에서 자동 로드된다
    {
        get
        {
            EnsureRunLoaded();
            return run;
        }
    }

    /// <summary>
    /// 플레이 진입·도메인 리로드마다 호출된다. 캐시를 무효화해 세션당 정확히 한 번만 디스크를 읽게 한다.
    /// </summary>
    private void OnEnable()
    {
        isProfileLoaded = false;
        isRunLoaded = false;
    }

    /// <summary>아직 로드하지 않았다면 프로필을 디스크에서 읽는다.</summary>
    private void EnsureProfileLoaded()
    {
        if (isProfileLoaded)
        {
            return;
        }

        LoadProfile();
    }

    /// <summary>아직 로드하지 않았다면 런 데이터를 디스크에서 읽는다.</summary>
    private void EnsureRunLoaded()
    {
        if (isRunLoaded)
        {
            return;
        }

        LoadRun();
    }

    /// <summary>프로필을 디스크에서 읽어 캐시에 덮어쓴다. 파일이 없으면 기본값을 유지한다.</summary>
    public void LoadProfile()
    {
        isProfileLoaded = true;

        if (!FileManager.LoadFromFile(ProfileFileName, out string json))
        {
            Log.D("[SaveLoadSystem] 프로필 파일 없음 — 기본값으로 시작한다.");
            return;
        }

        JsonUtility.FromJsonOverwrite(json, profile);
        Log.D($"[SaveLoadSystem] 프로필 로드 <- {json}");
    }

    /// <summary>현재 프로필 캐시를 디스크에 쓴다.</summary>
    public void SaveProfile()
    {
        EnsureProfileLoaded(); // 로드 전에 저장하면 기본값이 디스크를 덮어쓴다

        string json = JsonUtility.ToJson(profile);
        FileManager.WriteToFile(ProfileFileName, json);
        Log.D($"[SaveLoadSystem] 프로필 저장 -> {json}");
    }

    /// <summary>프로필을 기본값으로 되돌리고 즉시 디스크에 쓴다. 런 데이터(RunSave)는 건드리지 않는다.</summary>
    public void ResetProfile()
    {
        profile = new ProfileSave(); // 기본값의 단일 소스는 ProfileSave 의 필드 초기화식이다 — 여기서 항목별로 다시 대입하면 설정 추가 때 빠뜨린다
        isProfileLoaded = true;      // 방금 기본값으로 채웠으므로 디스크를 다시 읽을 필요가 없다

        SaveProfile();
        Log.D("[SaveLoadSystem] 프로필을 기본값으로 초기화 — 런 데이터는 보존된다.");
    }

    /// <summary>런 데이터를 디스크에서 읽어 캐시에 덮어쓴다. 파일이 없으면 기본값을 유지한다.</summary>
    public void LoadRun()
    {
        isRunLoaded = true;

        if (!FileManager.LoadFromFile(RunFileName, out string json))
        {
            Log.D("[SaveLoadSystem] 런 파일 없음 — 새 런으로 시작한다.");
            return;
        }

        JsonUtility.FromJsonOverwrite(json, run);
        Log.D($"[SaveLoadSystem] 런 로드 <- {json}");
    }

    /// <summary>현재 런 캐시를 디스크에 쓴다.</summary>
    public void SaveRun()
    {
        EnsureRunLoaded();

        string json = JsonUtility.ToJson(run);
        FileManager.WriteToFile(RunFileName, json);
        Log.D($"[SaveLoadSystem] 런 저장 -> {json}");
    }

    /// <summary>런 파일을 지우고 캐시를 초기화한다. 설정(ProfileSave)은 건드리지 않는다.</summary>
    public void DeleteRun()
    {
        FileManager.Delete(RunFileName);
        run = new RunSave();
        isRunLoaded = true; // 방금 비웠으므로 다시 읽을 필요가 없다
        Log.D("[SaveLoadSystem] 런 데이터 삭제 — 프로필(설정)은 보존된다.");
    }
}
