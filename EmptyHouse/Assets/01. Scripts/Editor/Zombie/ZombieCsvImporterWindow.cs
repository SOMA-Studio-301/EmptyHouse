using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

public class ZombieCsvImporterWindow : EditorWindow
{
    private string csvPath;
    private string outputFolder = "Assets/03. ScriptableObjects/Zombies";

    [MenuItem("Tools/EmptyHouse/Import Zombie CSV")]
    public static void Open()
    {
        GetWindow<ZombieCsvImporterWindow>("Zombie CSV Importer");
    }

    private void OnEnable()
    {
        csvPath = GetDefaultCsvPath();
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Zombie CSV Importer", EditorStyles.boldLabel);
        csvPath = EditorGUILayout.TextField("CSV Path", csvPath);
        outputFolder = EditorGUILayout.TextField("Output Folder", outputFolder);

        if (GUILayout.Button("Import / Update Assets"))
        {
            ImportCsv(csvPath, outputFolder);
        }
    }

    private static string GetDefaultCsvPath()
    {
        string assetsPath = Application.dataPath;
        string projectRoot = Path.GetFullPath(Path.Combine(assetsPath, "..", ".."));
        return Path.Combine(projectRoot, "Docs", "Spec", "Data", "zombie.csv");
    }

    private static void ImportCsv(string path, string targetFolder)
    {
        if (!File.Exists(path))
        {
            Debug.LogError($"Zombie CSV not found: {path}");
            return;
        }

        if (!AssetDatabase.IsValidFolder(targetFolder))
        {
            CreateFolderRecursive(targetFolder);
        }

        string[] lines = File.ReadAllLines(path);
        Dictionary<string, ZombieDataSO> assetsByType = new Dictionary<string, ZombieDataSO>(StringComparer.OrdinalIgnoreCase)
        {
            { "Walker", LoadOrCreateAsset(targetFolder, "SO_Zombie_Walker") },
            { "Listener", LoadOrCreateAsset(targetFolder, "SO_Zombie_Listener") },
            { "Watcher", LoadOrCreateAsset(targetFolder, "SO_Zombie_Watcher") }
        };

        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;

            string[] columns = SplitCsvLine(line);
            if (columns.Length < 6) continue;

            string category = columns[0].Trim();
            string id = columns[1].Trim();
            string value = columns[3].Trim();

            ApplyRow(assetsByType, category, id, value);
        }

        foreach (var asset in assetsByType.Values)
        {
            EditorUtility.SetDirty(asset);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Zombie CSV import completed.");
    }

    private static void ApplyRow(Dictionary<string, ZombieDataSO> assetsByType, string category, string id, string value)
    {
        switch (id)
        {
            case "fov_walker":
                assetsByType["Walker"].VisionAngle = ParseFloat(value);
                break;
            case "fov_listener":
                assetsByType["Listener"].VisionAngle = ParseFloat(value);
                break;
            case "fov_watcher":
                assetsByType["Watcher"].VisionAngle = ParseFloat(value);
                break;
            case "range_walker":
                assetsByType["Walker"].VisionDistance = ParseFloat(value);
                break;
            case "range_listener":
                assetsByType["Listener"].VisionDistance = ParseFloat(value);
                break;
            case "range_watcher":
                assetsByType["Watcher"].VisionDistance = ParseFloat(value);
                break;
            case "hear_detect_walker":
                assetsByType["Walker"].HearDetectDb = ParseFloat(value);
                break;
            case "hear_detect_listener":
                assetsByType["Listener"].HearDetectDb = ParseFloat(value);
                break;
            case "hear_detect_watcher":
                assetsByType["Watcher"].HearDetectDb = ParseFloat(value);
                break;
            case "th_alert":
                foreach (ZombieDataSO asset in assetsByType.Values) asset.ThAlert = ParseFloat(value);
                break;
            case "th_investigate":
                foreach (ZombieDataSO asset in assetsByType.Values) asset.ThInvestigate = ParseFloat(value);
                break;
            case "vis_gain_base":
                foreach (ZombieDataSO asset in assetsByType.Values) asset.VisGainBase = ParseFloat(value);
                break;
            case "vis_instant_range":
                foreach (ZombieDataSO asset in assetsByType.Values) asset.VisInstantRange = ParseFloat(value);
                break;
            case "vis_dist_near":
                foreach (ZombieDataSO asset in assetsByType.Values) asset.VisDistNear = ParseFloat(value);
                break;
            case "vis_dist_far":
                foreach (ZombieDataSO asset in assetsByType.Values) asset.VisDistFar = ParseFloat(value);
                break;
            case "vis_front":
                foreach (ZombieDataSO asset in assetsByType.Values) asset.VisFront = ParseFloat(value);
                break;
            case "vis_light_bright":
                foreach (ZombieDataSO asset in assetsByType.Values) asset.VisLightBright = ParseFloat(value);
                break;
            case "vis_light_dark":
                foreach (ZombieDataSO asset in assetsByType.Values) asset.VisLightDark = ParseFloat(value);
                break;
            case "vis_light_flashlight":
                foreach (ZombieDataSO asset in assetsByType.Values) asset.VisLightFlashlight = ParseFloat(value);
                break;
            case "vis_pose_walk":
                foreach (ZombieDataSO asset in assetsByType.Values) asset.VisPoseWalk = ParseFloat(value);
                break;
            case "vis_pose_crouch":
                foreach (ZombieDataSO asset in assetsByType.Values) asset.VisPoseCrouch = ParseFloat(value);
                break;
            case "vis_pose_idle":
                foreach (ZombieDataSO asset in assetsByType.Values) asset.VisPoseIdle = ParseFloat(value);
                break;
            case "hear_floor":
                foreach (ZombieDataSO asset in assetsByType.Values) asset.HearFloor = ParseFloat(value);
                break;
            case "cool_rate":
                foreach (ZombieDataSO asset in assetsByType.Values) asset.CoolRate = ParseFloat(value);
                break;
            case "sync_radius":
                foreach (ZombieDataSO asset in assetsByType.Values) asset.SyncRadius = ParseFloat(value);
                break;
            case "watcher_blind_recovery":
                assetsByType["Watcher"].WatcherBlindRecoverySeconds = ParseFloat(value);
                break;
            case "zombie_wander":
                foreach (ZombieDataSO asset in assetsByType.Values) asset.WanderSpeed = ParseFloat(value);
                break;
            case "zombie_alert":
                foreach (ZombieDataSO asset in assetsByType.Values) asset.AlertSpeed = ParseFloat(value);
                break;
            case "zombie_investigate_base":
                foreach (ZombieDataSO asset in assetsByType.Values) asset.InvestigateBaseSpeed = ParseFloat(value);
                break;
            case "zombie_investigate_cap":
                foreach (ZombieDataSO asset in assetsByType.Values) asset.InvestigateCapSpeed = ParseFloat(value);
                break;
            case "zombie_investigate_k":
                foreach (ZombieDataSO asset in assetsByType.Values) asset.InvestigateDbToSpeed = ParseFloat(value);
                break;
            case "zombie_chase":
                foreach (ZombieDataSO asset in assetsByType.Values) asset.ChaseSpeed = ParseFloat(value);
                break;
            case "zombie_subside":
                foreach (ZombieDataSO asset in assetsByType.Values) asset.SubsideSpeed = ParseFloat(value);
                break;
            case "alert_motion":
                foreach (ZombieDataSO asset in assetsByType.Values) asset.AlertMotionSeconds = ParseFloat(value);
                break;
            case "chase_to_investigate":
                foreach (ZombieDataSO asset in assetsByType.Values) asset.ChaseToInvestigateSeconds = ParseFloat(value);
                break;
            case "attack_lock":
                foreach (ZombieDataSO asset in assetsByType.Values) asset.AttackLockSeconds = ParseFloat(value);
                break;
            case "investigate_to_wander":
                foreach (ZombieDataSO asset in assetsByType.Values) asset.InvestigateToWanderSeconds = ParseFloat(value);
                break;
            case "suspicion_grace":
                foreach (ZombieDataSO asset in assetsByType.Values) asset.SuspicionGraceSeconds = ParseFloat(value);
                break;
        }

        if (category == "sense")
        {
            if (id == "fov_walker") assetsByType["Walker"].ZombieType = ZombieType.Walker;
            if (id == "fov_listener") assetsByType["Listener"].ZombieType = ZombieType.Listener;
            if (id == "fov_watcher") assetsByType["Watcher"].ZombieType = ZombieType.Watcher;
        }
    }

    private static ZombieDataSO LoadOrCreateAsset(string folder, string assetName)
    {
        string path = $"{folder}/{assetName}.asset";
        ZombieDataSO asset = AssetDatabase.LoadAssetAtPath<ZombieDataSO>(path);
        if (asset != null) return asset;

        asset = ScriptableObject.CreateInstance<ZombieDataSO>();
        AssetDatabase.CreateAsset(asset, path);
        return asset;
    }

    private static float ParseFloat(string value)
    {
        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result))
        {
            return result;
        }

        return 0f;
    }

    private static string[] SplitCsvLine(string line)
    {
        List<string> columns = new List<string>();
        bool inQuotes = false;
        System.Text.StringBuilder current = new System.Text.StringBuilder();

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (c == ',' && !inQuotes)
            {
                columns.Add(current.ToString());
                current.Length = 0;
                continue;
            }

            current.Append(c);
        }

        columns.Add(current.ToString());
        return columns.ToArray();
    }

    private static void CreateFolderRecursive(string folderPath)
    {
        string[] parts = folderPath.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, parts[i]);
            }
            current = next;
        }
    }
}