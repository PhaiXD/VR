using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class BuildHelper
{
    [MenuItem("UR3e VR/Build Windows Player (Quest PCVR)")]
    public static void BuildWindows()
    {
        string[] scenes = new string[] { "Assets/Scenes/SampleScene.unity" };
        string buildPath = "C:/Users/dhamm/Downloads/VR.exe";

        BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = buildPath,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        };

        Debug.Log("[BuildHelper] Starting Windows Standalone build to: " + buildPath);
        BuildReport report = BuildPipeline.BuildPlayer(buildPlayerOptions);
        BuildSummary summary = report.summary;

        if (summary.result == BuildResult.Succeeded)
        {
            Debug.Log($"[BuildHelper] Build succeeded: {summary.totalSize / 1024 / 1024} MB in {summary.totalTime.TotalSeconds:F1} seconds");
            EditorUtility.RevealInFinder(buildPath);
        }
        else if (summary.result == BuildResult.Failed)
        {
            Debug.LogError($"[BuildHelper] Build failed with {summary.totalErrors} errors!");
        }
    }
}
