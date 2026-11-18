using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Runeveil.Editor
{
    public static class RuneveilBuild
    {
        public const string Scene = "Assets/Scenes/Runeveil.unity";
        public const string Package = "com.runeveil.golemduel";
        public const string Apk = "Builds/Runeveil/Runeveil.apk";

        [MenuItem("Runeveil/Build Glasses APK")]
        public static void BuildApk()
        {
            if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(Scene))
                throw new InvalidOperationException("Missing Runeveil scene: " + Scene);
            if (PlayerSettings.GetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android) != Package)
                throw new InvalidOperationException("Check the Runeveil Android application identifier.");

            Directory.CreateDirectory(Path.GetDirectoryName(Apk));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { Scene },
                locationPathName = Apk,
                target = BuildTarget.Android,
                options = BuildOptions.Development
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException($"Runeveil APK build {report.summary.result} with {report.summary.totalErrors} errors");
            Debug.Log($"[RuneveilBuild] {Path.GetFullPath(Apk)} ({report.summary.totalSize} bytes)");
        }
    }
}
