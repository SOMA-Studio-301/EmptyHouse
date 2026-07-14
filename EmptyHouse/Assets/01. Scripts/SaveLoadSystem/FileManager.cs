using System;
using System.IO;
using System.Text;
using UnityEngine;
using Border.Core;

/// <summary>
/// persistentDataPath 기준 텍스트 파일 입출력 유틸리티.
/// 인코딩은 BOM 없는 UTF-8 로 고정한다 — 한글 저장값이 깨지지 않게 읽기·쓰기 양쪽에 명시한다.
/// </summary>
public static class FileManager
{
    private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

    /// <summary>파일명으로 persistentDataPath 하위 전체 경로를 만든다.</summary>
    /// <param name="fileName">파일명.</param>
    /// <returns>전체 경로.</returns>
    private static string GetFullPath(string fileName)
    {
        return Path.Combine(Application.persistentDataPath, fileName);
    }

    /// <summary>문자열을 파일에 UTF-8 로 쓴다.</summary>
    /// <param name="fileName">파일명.</param>
    /// <param name="contents">쓸 내용.</param>
    /// <returns>성공 여부.</returns>
    public static bool WriteToFile(string fileName, string contents)
    {
        string fullPath = GetFullPath(fileName);
        try
        {
            File.WriteAllText(fullPath, contents, Utf8NoBom);
            return true;
        }
        catch (Exception e)
        {
            Log.E($"[FileManager] 저장 실패. 경로: {fullPath}, exception: {e.Message}");
            return false;
        }
    }

    /// <summary>파일을 UTF-8 로 읽는다. 파일이 없으면 실패로 취급한다(에러 로그 없음).</summary>
    /// <param name="fileName">파일명.</param>
    /// <param name="result">읽은 내용. 실패 시 빈 문자열.</param>
    /// <returns>성공 여부.</returns>
    public static bool LoadFromFile(string fileName, out string result)
    {
        string fullPath = GetFullPath(fileName);
        if (!File.Exists(fullPath))
        {
            result = string.Empty;
            return false;
        }

        try
        {
            result = File.ReadAllText(fullPath, Utf8NoBom);
            return true;
        }
        catch (Exception e)
        {
            Log.E($"[FileManager] 로드 실패. 경로: {fullPath}, exception: {e.Message}");
            result = string.Empty;
            return false;
        }
    }

    /// <summary>파일을 삭제한다. 이미 없으면 성공으로 취급한다.</summary>
    /// <param name="fileName">파일명.</param>
    /// <returns>성공 여부.</returns>
    public static bool Delete(string fileName)
    {
        string fullPath = GetFullPath(fileName);
        try
        {
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }

            return true;
        }
        catch (Exception e)
        {
            Log.E($"[FileManager] 삭제 실패. 경로: {fullPath}, exception: {e.Message}");
            return false;
        }
    }
}
