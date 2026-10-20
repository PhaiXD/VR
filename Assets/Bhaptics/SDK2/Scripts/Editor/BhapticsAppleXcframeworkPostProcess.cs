#if UNITY_IOS || UNITY_VISIONOS
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.iOS.Xcode;
using UnityEditor.iOS.Xcode.Extensions;
using Debug = UnityEngine.Debug;

namespace Bhaptics.SDK2.Editor
{
    // BhapticsPlugin.xcframework ships zipped (not as an imported Unity plugin) so the editor never
    // imports it and macOS-slice symlinks survive a Windows checkout. This unpacks it into the
    // generated Xcode project and wires it up, for iOS/visionOS builds only.
    public class BhapticsAppleXcframeworkPostProcess : IPostprocessBuildWithReport
    {
        private const string ZipFileName = "BhapticsPlugin.xcframework.zip";
        private const string FrameworkName = "BhapticsPlugin.xcframework";

        public int callbackOrder => 0;

        public void OnPostprocessBuild(BuildReport report)
        {
            var target = report.summary.platform;
            var isApple = target == BuildTarget.iOS;
#if UNITY_VISIONOS
            isApple |= target == BuildTarget.VisionOS;
#endif
            if (!isApple)
            {
                return;
            }

            var zipPath = FindZip();
            if (zipPath == null)
            {
                Debug.LogError($"[bHaptics] {ZipFileName} not found. BhapticsPlugin will not be linked into the build.");
                return;
            }

            var xcodeRoot = report.summary.outputPath;
            var frameworksDir = Path.Combine(xcodeRoot, "Frameworks");
            Directory.CreateDirectory(frameworksDir);

            var dest = Path.Combine(frameworksDir, FrameworkName);
            if (Directory.Exists(dest))
            {
                Directory.Delete(dest, true);
            }

            // unzip (CLI) preserves the macOS slice's symlinked bundle layout; .NET ZipFile would not.
            Unzip(zipPath, frameworksDir);

            var pbxPath = FindPbxProjectPath(xcodeRoot);
            if (pbxPath == null)
            {
                Debug.LogError($"[bHaptics] No .xcodeproj found under {xcodeRoot}; BhapticsPlugin not linked.");
                return;
            }

            var pbx = new PBXProject();
            pbx.ReadFromFile(pbxPath);

            var relPath = "Frameworks/" + FrameworkName;
            var fileGuid = pbx.AddFile(relPath, relPath, PBXSourceTree.Source);

            var frameworkTarget = pbx.GetUnityFrameworkTargetGuid();
            var appTarget = pbx.GetUnityMainTargetGuid();

            // Link into UnityFramework (IL2CPP resolves __Internal symbols there). Force it into the
            // frameworks (Link Binary With Libraries) phase explicitly — AddFileToBuild can silently
            // skip an .xcframework, leaving _BhapticsPlugin_* undefined at link time.
            pbx.AddFileToBuildSection(frameworkTarget, pbx.GetFrameworksBuildPhaseByTarget(frameworkTarget), fileGuid);
            // Embed & Sign into the app target so the dynamic framework ships and loads at runtime.
            pbx.AddFileToEmbedFrameworks(appTarget, fileGuid);

            foreach (var t in new[] { frameworkTarget, appTarget })
            {
                pbx.AddBuildProperty(t, "FRAMEWORK_SEARCH_PATHS", "$(inherited) $(PROJECT_DIR)/Frameworks");
                pbx.AddBuildProperty(t, "LD_RUNPATH_SEARCH_PATHS", "$(inherited) @executable_path/Frameworks @loader_path/Frameworks");
            }

            pbx.WriteToFile(pbxPath);
        }

        private static string FindZip()
        {
            foreach (var guid in AssetDatabase.FindAssets("BhapticsPlugin"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(ZipFileName))
                {
                    return Path.GetFullPath(path);
                }
            }

            return null;
        }

        // visionOS builds produce Unity-VisionOS.xcodeproj; iOS produces Unity-iPhone.xcodeproj.
        // PBXProject.GetPBXProjectPath hardcodes the iOS name, so locate the project by extension.
        private static string FindPbxProjectPath(string xcodeRoot)
        {
            foreach (var proj in Directory.GetDirectories(xcodeRoot, "*.xcodeproj"))
            {
                var path = Path.Combine(proj, "project.pbxproj");
                if (File.Exists(path))
                {
                    return path;
                }
            }

            return null;
        }

        private static void Unzip(string zip, string destDir)
        {
            var psi = new ProcessStartInfo("unzip", $"-o -q \"{zip}\" -d \"{destDir}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (var process = Process.Start(psi))
            {
                process.WaitForExit();
            }
        }
    }
}
#endif
