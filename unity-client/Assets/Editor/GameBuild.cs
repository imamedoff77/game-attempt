using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Command-line build entry points.
/// Usage: Unity -batchmode -nographics -quit -projectPath unity-client -buildTarget Android -executeMethod GameBuild.BuildAndroid
/// </summary>
public static class GameBuild
{
    private const string BootstrapScene = "Assets/Scenes/Bootstrap.unity";
    private const string ApkPath = "Builds/Android/GameAttempt.apk";

    public static void ConfigureAndroid()
    {
        PlayerSettings.companyName = "imamedoff";
        PlayerSettings.productName = "GameAttempt";
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.imamedoff.gameattempt");

        // 64-bit ARM needs IL2CPP (Mono only ships 32-bit ARM). Google Play requires 64-bit anyway.
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
        PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;

        // RTS is played in landscape.
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
        PlayerSettings.allowedAutorotateToPortrait = false;
        PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
        PlayerSettings.allowedAutorotateToLandscapeLeft = true;
        PlayerSettings.allowedAutorotateToLandscapeRight = true;

        EditorUserBuildSettings.buildAppBundle = false;
    }

    public static void CreateBootstrapScene()
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var cameraGo = new GameObject("Main Camera") { tag = "MainCamera" };
        var cam = cameraGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.12f, 0.15f, 0.2f);
        cameraGo.AddComponent<AudioListener>();
        cameraGo.transform.position = new Vector3(0f, 6f, -8f);
        cameraGo.transform.LookAt(Vector3.zero);

        var lightGo = new GameObject("Directional Light");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.localScale = new Vector3(2f, 1f, 2f);

        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "SpinningCube";
        cube.transform.position = new Vector3(0f, 1f, 0f);
        cube.AddComponent<Spinner>();

        new GameObject("Hud").AddComponent<FpsHud>();

        Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(scene, BootstrapScene);
        AssetDatabase.Refresh();
    }

    public static void BuildAndroid()
    {
        try
        {
            ConfigureAndroid();
            CreateBootstrapScene();

            Directory.CreateDirectory(Path.GetDirectoryName(ApkPath));
            var options = new BuildPlayerOptions
            {
                scenes = new[] { BootstrapScene },
                locationPathName = ApkPath,
                target = BuildTarget.Android,
                options = BuildOptions.None,
            };

            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            Debug.Log($"BUILD_RESULT: {summary.result} size={summary.totalSize} errors={summary.totalErrors} time={summary.totalTime}");
            EditorApplication.Exit(summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
        }
        catch (Exception e)
        {
            Debug.LogError("BUILD_EXCEPTION: " + e);
            EditorApplication.Exit(2);
        }
    }
}
