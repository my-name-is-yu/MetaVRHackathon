using System;
using System.IO;
using Runeveil.Contracts;
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
        public const string ReleaseApk = "Builds/Runeveil/Save-Muse-0.2.3-release.apk";

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

        [MenuItem("Runeveil/Build Release APK")]
        public static void BuildReleaseApk()
        {
            string keystorePath = Environment.GetEnvironmentVariable("RUNEVEIL_ANDROID_KEYSTORE_PATH");
            string keyAlias = Environment.GetEnvironmentVariable("RUNEVEIL_ANDROID_KEY_ALIAS");
            string keystorePassword = Environment.GetEnvironmentVariable("RUNEVEIL_ANDROID_KEYSTORE_PASSWORD");
            string keyPassword = Environment.GetEnvironmentVariable("RUNEVEIL_ANDROID_KEY_PASSWORD");
            if (!ReleaseSigningPolicy.HasCompleteSigningConfiguration(keystorePath, keyAlias, keystorePassword, keyPassword))
                throw new InvalidOperationException("Set RUNEVEIL_ANDROID_KEYSTORE_PATH, RUNEVEIL_ANDROID_KEY_ALIAS, " +
                    "RUNEVEIL_ANDROID_KEYSTORE_PASSWORD, and RUNEVEIL_ANDROID_KEY_PASSWORD before building a release.");
            if (!File.Exists(keystorePath))
                throw new InvalidOperationException("RUNEVEIL_ANDROID_KEYSTORE_PATH must point to an existing file.");

            if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(Scene))
                throw new InvalidOperationException("Missing Runeveil scene: " + Scene);
            if (PlayerSettings.GetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android) != Package)
                throw new InvalidOperationException("Check the Runeveil Android application identifier.");

            Directory.CreateDirectory(Path.GetDirectoryName(ReleaseApk));
            bool previousUseCustomKeystore = PlayerSettings.Android.useCustomKeystore;
            string previousKeystoreName = PlayerSettings.Android.keystoreName;
            string previousKeyAlias = PlayerSettings.Android.keyaliasName;
            string previousKeystorePassword = PlayerSettings.Android.keystorePass;
            string previousKeyPassword = PlayerSettings.Android.keyaliasPass;

            BuildReport report;
            try
            {
                PlayerSettings.Android.useCustomKeystore = true;
                PlayerSettings.Android.keystoreName = keystorePath;
                PlayerSettings.Android.keyaliasName = keyAlias;
                PlayerSettings.Android.keystorePass = keystorePassword;
                PlayerSettings.Android.keyaliasPass = keyPassword;

                report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { Scene },
                    locationPathName = ReleaseApk,
                    target = BuildTarget.Android,
                    options = BuildOptions.None
                });
            }
            finally
            {
                PlayerSettings.Android.keystoreName = previousKeystoreName;
                PlayerSettings.Android.keyaliasName = previousKeyAlias;
                PlayerSettings.Android.keystorePass = previousKeystorePassword;
                PlayerSettings.Android.keyaliasPass = previousKeyPassword;
                PlayerSettings.Android.useCustomKeystore = previousUseCustomKeystore;
            }

            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException($"Runeveil release APK build {report.summary.result} with {report.summary.totalErrors} errors");
            Debug.Log($"[RuneveilBuild] {Path.GetFullPath(ReleaseApk)} ({report.summary.totalSize} bytes)");
        }
    }
}
