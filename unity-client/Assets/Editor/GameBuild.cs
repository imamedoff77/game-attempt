using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Command-line entry points. Everything the project needs (render pipeline, materials, scene, Android
/// settings) is created from code, so it can be rebuilt from scratch without opening the editor.
///
///   Unity -batchmode -nographics -quit -projectPath unity-client -executeMethod GameBuild.Prepare
///   Unity -batchmode -nographics -quit -projectPath unity-client -buildTarget Android -executeMethod GameBuild.BuildAndroid
/// </summary>
public static class GameBuild
{
    private const string ScenePath = "Assets/Scenes/Bootstrap.unity";
    private const string ApkPath = "Builds/Android/GameAttempt.apk";

    public static void Prepare()
    {
        try
        {
            SetupRenderPipeline();
            ConfigureAndroid();
            Material[] mats = CreateMaterials();
            CreateScene(mats);
            AssetDatabase.SaveAssets();
            Debug.Log("PREPARE_RESULT: OK");
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogError("PREPARE_EXCEPTION: " + e);
            EditorApplication.Exit(2);
        }
    }

    public static void BuildAndroid()
    {
        try
        {
            SetupRenderPipeline();
            ConfigureAndroid();
            Material[] mats = CreateMaterials();
            CreateScene(mats);

            Directory.CreateDirectory(Path.GetDirectoryName(ApkPath));
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = ApkPath,
                target = BuildTarget.Android,
                options = BuildOptions.None,
            };

            BuildSummary summary = BuildPipeline.BuildPlayer(options).summary;
            Debug.Log($"BUILD_RESULT: {summary.result} size={summary.totalSize} errors={summary.totalErrors} time={summary.totalTime}");
            EditorApplication.Exit(summary.result == BuildResult.Succeeded ? 0 : 1);
        }
        catch (Exception e)
        {
            Debug.LogError("BUILD_EXCEPTION: " + e);
            EditorApplication.Exit(2);
        }
    }

    private static void ConfigureAndroid()
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

    private static void SetupRenderPipeline()
    {
        const string rendererPath = "Assets/Settings/Mobile_Renderer.asset";
        const string assetPath = "Assets/Settings/Mobile_RP.asset";
        Directory.CreateDirectory("Assets/Settings");

        var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
        if (rendererData == null)
        {
            rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(rendererData, rendererPath);
        }

        var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(assetPath);
        if (pipeline == null)
        {
            pipeline = UniversalRenderPipelineAsset.Create(rendererData);
            AssetDatabase.CreateAsset(pipeline, assetPath);
        }

        // Mobile-friendly: no HDR, no MSAA, short shadow distance.
        pipeline.supportsHDR = false;
        pipeline.msaaSampleCount = 1;
        pipeline.shadowDistance = 40f;
        EditorUtility.SetDirty(pipeline);

        GraphicsSettings.defaultRenderPipeline = pipeline;
        for (int i = 0; i < QualitySettings.names.Length; i++)
        {
            QualitySettings.SetQualityLevel(i, false);
            QualitySettings.renderPipeline = pipeline;
        }
        AssetDatabase.SaveAssets();
    }

    /// <returns>ground, unit, accent, ring, marker</returns>
    private static Material[] CreateMaterials()
    {
        Directory.CreateDirectory("Assets/Materials");
        return new[]
        {
            Mat("Ground", new Color(1f, 1f, 1f)),
            Mat("Unit", new Color(0.20f, 0.45f, 0.85f)),
            Mat("Accent", new Color(0.12f, 0.12f, 0.14f)),
            Mat("SelectionRing", new Color(0.35f, 1f, 0.45f)),
            Mat("TapMarker", new Color(1f, 0.9f, 0.3f)),
        };
    }

    private static Material Mat(string name, Color color)
    {
        string path = $"Assets/Materials/{name}.mat";
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) throw new Exception("URP Lit shader not found");

        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.shader = shader;
        mat.color = color;
        mat.SetFloat("_Smoothness", 0.1f);
        mat.SetFloat("_Metallic", 0f);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    private static void CreateScene(Material[] mats)
    {
        Directory.CreateDirectory("Assets/Scenes");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.55f, 0.57f, 0.62f);

        var cameraGo = new GameObject("Main Camera") { tag = "MainCamera" };
        var cam = cameraGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.08f, 0.10f, 0.14f);
        cam.nearClipPlane = 0.3f;
        cam.farClipPlane = 200f;
        cameraGo.AddComponent<AudioListener>();
        cameraGo.AddComponent<RtsCamera>();

        var lightGo = new GameObject("Directional Light");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 0.9f;
        light.shadows = LightShadows.None;
        lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        var game = new GameObject("Game");
        var demo = game.AddComponent<DemoGame>();
        game.AddComponent<DemoHud>();

        var so = new SerializedObject(demo);
        so.FindProperty("groundMaterial").objectReferenceValue = mats[0];
        so.FindProperty("unitMaterial").objectReferenceValue = mats[1];
        so.FindProperty("accentMaterial").objectReferenceValue = mats[2];
        so.FindProperty("ringMaterial").objectReferenceValue = mats[3];
        so.FindProperty("markerMaterial").objectReferenceValue = mats[4];
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.SaveScene(scene, ScenePath);
    }
}
