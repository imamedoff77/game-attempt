using System;
using System.IO;
using SimCore;
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
            ReportModelBounds();
            CreateScene();
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
            CreateScene();

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

    // ---------------------------------------------------------------- materials

    private static Material LitMaterial(string name, Color color, Texture texture = null)
    {
        Material mat = LoadOrCreate(name, "Universal Render Pipeline/Lit");
        mat.color = color;
        mat.SetFloat("_Smoothness", 0f);
        mat.SetFloat("_Metallic", 0f);
        mat.SetFloat("_SpecularHighlights", 0f);
        mat.SetFloat("_EnvironmentReflections", 0f);
        mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
        mat.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
        if (texture != null) { mat.SetTexture("_BaseMap", texture); mat.mainTexture = texture; }
        EditorUtility.SetDirty(mat);
        return mat;
    }

    private static Material UnlitMaterial(string name, Color color)
    {
        Material mat = LoadOrCreate(name, "Universal Render Pipeline/Unlit");
        mat.color = color;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    private static Material LoadOrCreate(string name, string shaderName)
    {
        Directory.CreateDirectory("Assets/Materials");
        string path = $"Assets/Materials/{name}.mat";
        Shader shader = Shader.Find(shaderName);
        if (shader == null) throw new Exception("Shader not found: " + shaderName);

        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.shader = shader;
        return mat;
    }

    // ---------------------------------------------------------------- scene

    private static GameObject Model(string name)
    {
        var go = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Models/{name}.fbx");
        if (go == null) throw new Exception("Model not found: " + name + " (run art/blender/generate_assets.py)");
        return go;
    }

    private static void CreateScene()
    {
        Directory.CreateDirectory("Assets/Scenes");

        var palette = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/palette.png");
        if (palette == null) throw new Exception("palette.png not found (run art/blender/generate_assets.py)");

        Material paletteMat = LitMaterial("Palette", Color.white, palette);
        Material groundMat = LitMaterial("Ground", Color.white);
        Material ringMat = UnlitMaterial("SelectionRing", new Color(0.35f, 1f, 0.45f));
        Material markerMat = UnlitMaterial("TapMarker", new Color(1f, 0.9f, 0.3f));

        Material overlayMat = LoadOrCreate("BuildOverlay", "Sprites/Default");
        overlayMat.color = Color.white;
        EditorUtility.SetDirty(overlayMat);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.62f, 0.62f, 0.66f);

        var cameraGo = new GameObject("Main Camera") { tag = "MainCamera" };
        var cam = cameraGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.55f, 0.47f, 0.35f);
        cam.nearClipPlane = 0.3f;
        cam.farClipPlane = 200f;
        cameraGo.AddComponent<AudioListener>();
        cameraGo.AddComponent<RtsCamera>();

        var lightGo = new GameObject("Directional Light");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 0.85f;
        light.color = new Color(1f, 0.96f, 0.88f);
        light.shadows = LightShadows.None;
        lightGo.transform.rotation = Quaternion.Euler(52f, -35f, 0f);

        var game = new GameObject("Game");
        var demo = game.AddComponent<DemoGame>();
        game.AddComponent<DemoHud>();

        var so = new SerializedObject(demo);
        so.FindProperty("paletteMaterial").objectReferenceValue = paletteMat;
        so.FindProperty("groundMaterial").objectReferenceValue = groundMat;
        so.FindProperty("overlayMaterial").objectReferenceValue = overlayMat;
        so.FindProperty("ringMaterial").objectReferenceValue = ringMat;
        so.FindProperty("markerMaterial").objectReferenceValue = markerMat;

        // Arrays are indexed by the enum value, so the order here must match SimCore.UnitKind / BuildingKind.
        SetObjects(so, "unitModels", new[]
        {
            Model("unit_rifleman"),  // UnitKind.Rifleman
            Model("unit_rocket"),    // UnitKind.RocketTrooper
            Model("unit_jeep"),      // UnitKind.Jeep
            Model("unit_tank"),      // UnitKind.Tank
        });
        SetObjects(so, "buildingModels", new[]
        {
            Model("bld_hq"),         // BuildingKind.Hq
            Model("bld_power"),      // BuildingKind.PowerPlant
            Model("bld_supply"),     // BuildingKind.SupplyCenter
            Model("bld_barracks"),   // BuildingKind.Barracks
            Model("bld_factory"),    // BuildingKind.VehicleFactory
        });
        SetObjects(so, "rockModels", new[] { Model("prop_rock_a"), Model("prop_rock_b"), Model("prop_rock_c") });
        so.FindProperty("palmModel").objectReferenceValue = Model("prop_palm");
        so.FindProperty("cactusModel").objectReferenceValue = Model("prop_cactus");
        so.FindProperty("bushModel").objectReferenceValue = Model("prop_bush");
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.SaveScene(scene, ScenePath);
    }

    private static void SetObjects(SerializedObject so, string property, UnityEngine.Object[] objects)
    {
        SerializedProperty prop = so.FindProperty(property);
        prop.arraySize = objects.Length;
        for (int i = 0; i < objects.Length; i++)
            prop.GetArrayElementAtIndex(i).objectReferenceValue = objects[i];
    }

    // ---------------------------------------------------------------- diagnostics

    /// <summary>Logs each model's size in Unity space, to confirm the Blender export kept scale and orientation.</summary>
    private static void ReportModelBounds()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { "Assets/Models" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Bounds b = default;
            bool first = true;
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
            {
                if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds);
            }
            Debug.Log($"MODEL_BOUNDS {Path.GetFileNameWithoutExtension(path)}: size=({b.size.x:0.00}, {b.size.y:0.00}, {b.size.z:0.00}) center=({b.center.x:0.00}, {b.center.y:0.00}, {b.center.z:0.00})");
        }
    }
}
