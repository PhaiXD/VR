using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class BuildHelper
{
    [MenuItem("UR3e VR/Build Windows Player (Quest PCVR)")]
    public static void BuildWindows()
    {
        // 1. Close any running instance of VR.exe to avoid Windows file-lock errors
        var runningProcesses = System.Diagnostics.Process.GetProcessesByName("VR");
        if (runningProcesses.Length > 0)
        {
            bool close = EditorUtility.DisplayDialog(
                "VR.exe is Running",
                "VR.exe is currently running. It must be closed before rebuilding.\nClose it automatically and continue?",
                "Close and Rebuild",
                "Cancel"
            );
            if (!close)
            {
                Debug.LogWarning("[BuildHelper] Build cancelled by user because VR.exe is currently running.");
                return;
            }

            foreach (var p in runningProcesses)
            {
                try
                {
                    p.Kill();
                    p.WaitForExit(3000);
                }
                catch { }
            }
        }

        // 2. CRITICAL: Save all open scenes and assets so that latest UI and Inspector edits are included!
        Debug.Log("[BuildHelper] Saving open scenes and assets before building...");
        EditorSceneManager.SaveOpenScenes();
        AssetDatabase.SaveAssets();

        // 3. Collect scenes: prefer enabled build scenes or open scene
        List<string> scenesList = new List<string>();
        foreach (var scene in EditorBuildSettings.scenes)
        {
            if (scene.enabled && !string.IsNullOrEmpty(scene.path))
            {
                scenesList.Add(scene.path);
            }
        }

        if (scenesList.Count == 0)
        {
            string activeScenePath = EditorSceneManager.GetActiveScene().path;
            if (!string.IsNullOrEmpty(activeScenePath))
            {
                scenesList.Add(activeScenePath);
            }
            else
            {
                scenesList.Add("Assets/Scenes/SampleScene.unity");
            }
        }

        string buildPath = "C:/Users/dhamm/Downloads/VR.exe";

        BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions
        {
            scenes = scenesList.ToArray(),
            locationPathName = buildPath,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        };

        Debug.Log("[BuildHelper] Starting Windows Standalone build to: " + buildPath);
        foreach (var s in scenesList)
        {
            Debug.Log("   [Build Scene] " + s);
        }

        BuildReport report = BuildPipeline.BuildPlayer(buildPlayerOptions);
        BuildSummary summary = report.summary;

        if (summary.result == BuildResult.Succeeded)
        {
            string sizeMB = (summary.totalSize / 1024 / 1024).ToString();
            string timeSec = summary.totalTime.TotalSeconds.ToString("F1");
            string msg = "Build succeeded!\nSize: " + sizeMB + " MB\nTime: " + timeSec + "s\nLocation: " + buildPath;
            Debug.Log("[BuildHelper] " + msg);
            EditorUtility.DisplayDialog("UR3e VR Build Succeeded!", msg, "OK");
            EditorUtility.RevealInFinder(buildPath);
        }
        else if (summary.result == BuildResult.Failed)
        {
            string msg = "Build failed with " + summary.totalErrors + " errors!\nCheck Unity Console for details.";
            Debug.LogError("[BuildHelper] " + msg);
            EditorUtility.DisplayDialog("UR3e VR Build Failed", msg, "OK");
        }
    }
}
