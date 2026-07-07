using UnityEngine;
using Border.Core;

namespace Border.SaveLoad
{
    public class Save
    {
        public string ToJson()
        {
            return JsonUtility.ToJson(this);
        }

        public void LoadFromJson(string json)
        {
            JsonUtility.FromJsonOverwrite(json, this);
        }
    }

}
