using UnityEditor;
using UnityEngine;
using System.Linq;

/// <summary>
/// Build APK dari command line:
/// Unity -batchmode -quit -projectPath <path> -buildTarget Android -executeMethod BuildAndroid.Build
/// </summary>
public static class BuildAndroid
{
    public static void Build()
    {
        var scenes = EditorBuildSettings.scenes
            .Where(s => s.enabled)
            .Select(s => s.path)
            .ToArray();
        if (scenes.Length == 0)
            scenes = new[] { "Assets/Scenes/Main.unity" };

        var opts = new BuildPlayerOptions
        {
            scenes           = scenes,
            locationPathName = "Builds/ConceptMapAR.apk",
            target           = BuildTarget.Android,
            options          = BuildOptions.None,
        };

        var report = BuildPipeline.BuildPlayer(opts);
        Debug.Log($"[BuildAndroid] Result: {report.summary.result}, " +
                  $"size: {report.summary.totalSize / (1024 * 1024)} MB, " +
                  $"errors: {report.summary.totalErrors}");

        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            EditorApplication.Exit(1);
    }
}
