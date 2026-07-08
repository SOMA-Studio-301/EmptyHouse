using System;
using System.IO;
using UnityEngine;
using Border.Core;

namespace Border.SaveLoad
{
    public class FileManager
    {
        public static bool WriteToFile(string fileName, string fileContents)
        {
            var fullPath = Path.Combine(Application.persistentDataPath, fileName);
            try
            {
                File.WriteAllText(fullPath, fileContents);
                return true;
            }
            catch (Exception e)
            {
                Log.E($"데이터 저장 실패. 경로: {fullPath}, exception: {e.Message}");
                return false;
            }
        }

        public static bool LoadFromFile(string fileName, out string result)
        {
            var fullPath = Path.Combine(Application.persistentDataPath, fileName);
            // 만약 Save file이 존재하지 않으면, False return
            if (!File.Exists(fullPath))
            {
                result = "";
                return false;
            }

            try
            {
                result = File.ReadAllText(fullPath);
                return true;
            }
            catch (Exception e)
            {
                Log.E($"데이터 로드 실패. 경로: {fullPath}, exception: {e.Message}");
                result = "";
                return false;
            }
        }
    }

}
