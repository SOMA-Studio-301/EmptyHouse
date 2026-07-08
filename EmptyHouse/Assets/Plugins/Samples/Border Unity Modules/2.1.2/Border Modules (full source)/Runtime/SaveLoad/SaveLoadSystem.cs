using System;
using UnityEngine;
using Border.Core;

namespace Border.SaveLoad
{
    [CreateAssetMenu(fileName = "SaveLoadSystem", menuName = "Save Load System")]
    public class SaveLoadSystem : ScriptableObject
    {
        public Save SaveData = new Save();
        public string SaveFileName = "save.game";

        private void Awake()
        {
            if (SaveData == null)
                SaveData = new Save();
        }

        public void SaveDataToDisk()
        {
            // 디스크에 저장
            if (FileManager.WriteToFile(SaveFileName, SaveData.ToJson()))
            {
                //Debug.Log("데이터 저장에 성공했습니다.");
            }
            else
            {
                Log.D("데이터 저장에... 실패했습니다...");
            }
        }

        public bool LoadSaveDataFromDisk()
        {
            if (FileManager.LoadFromFile(SaveFileName, out string json))
            {
                SaveData.LoadFromJson(json);
                return true;
            }

            return false;
        }

        public void SetNewGameData()
        {
            FileManager.WriteToFile(SaveFileName, "");
            SaveDataToDisk();
        }
    }

}
