using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.iOS;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

public static class ProjectSetup
{
    const string InputTestScenePath = "Assets/Scenes/InputTest.unity";
    const string KeoCoScenePath = "Assets/Scenes/KeoCo.unity";
    const string MenuScenePath = "Assets/Scenes/Menu.unity";
    const string PodiumScenePath = "Assets/Scenes/Podium.unity";

    const string FontsFolder = "Assets/UI/Fonts";
    const string PanelSettingsPath = "Assets/UI/MadangPanelSettings.asset";
    const string ThemePath = "Assets/UI/MadangTheme.tss";
    const string MenuUxmlPath = "Assets/UI/Menu/Menu.uxml";
    const string KeoCoUxmlPath = "Assets/UI/KeoCo/KeoCoHud.uxml";
    const string KeoCoSeatUxmlPath = "Assets/UI/KeoCo/KeoCoSeat.uxml";
    const string PodiumUxmlPath = "Assets/UI/Podium/Podium.uxml";

    // Phone lies flat on a table: lock landscape so a bump never flips the screen,
    // and keep system edge gestures from stealing taps on the corner buttons.
    [MenuItem("KoreaGame/Apply Mobile Player Settings")]
    public static void ApplyMobilePlayerSettings()
    {
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
        PlayerSettings.Android.requestedVisibleInsets = AndroidWindowInsetsType.None; // hide status and navigation bars
        PlayerSettings.iOS.hideHomeButton = true;
        PlayerSettings.iOS.deferSystemGesturesMode = SystemGestureDeferMode.All;
        AssetDatabase.SaveAssets();
        Debug.Log("KoreaGame: mobile player settings applied (Landscape Left, fullscreen, deferred edge gestures).");
    }

    // Mali covers Latin and Vietnamese; Jua is its fallback for Hangul. Assets that already exist are kept.
    [MenuItem("KoreaGame/Create UI Assets")]
    public static void CreateUiAssets()
    {
        var jua = CreateFontAsset("Jua-Regular.ttf", "JuaSDF.asset");
        var mali = CreateFontAsset("Mali-Bold.ttf", "MaliBoldSDF.asset");
        mali.fallbackFontAssetTable = new List<FontAsset> { jua };
        EditorUtility.SetDirty(mali);

        if (AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath) == null)
        {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(1920, 1080);
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = 0.5f;
            settings.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            AssetDatabase.CreateAsset(settings, PanelSettingsPath);
        }

        AssetDatabase.SaveAssets();
        Debug.Log("KoreaGame: UI assets ready.");
    }

    [MenuItem("KoreaGame/Create Input Test Scene")]
    public static void CreateInputTestScene() => CreateGameScene<InputTestGame>(InputTestScenePath);

    // Menu ends up as the first build scene, so a device build boots into it.
    [MenuItem("KoreaGame/Create UI Scenes")]
    public static void CreateUiScenes()
    {
        CreateGameScene<PodiumScreen>(PodiumScenePath, PodiumUxmlPath);
        CreateGameScene<KeoCoGame>(KeoCoScenePath, KeoCoUxmlPath, game =>
        {
            var serialized = new SerializedObject(game);
            serialized.FindProperty("seatTemplate").objectReferenceValue = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(KeoCoSeatUxmlPath);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        });
        CreateGameScene<MainMenu>(MenuScenePath, MenuUxmlPath, _ => new GameObject(nameof(GameManager), typeof(GameManager)));
    }

    // A scene with a camera and the given component, set as the first build scene.
    static void CreateGameScene<T>(string path, string uxmlPath = null, Action<T> configure = null) where T : MonoBehaviour
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var camera = new GameObject("Main Camera", typeof(Camera)).GetComponent<Camera>();
        camera.tag = "MainCamera";
        camera.orthographic = true;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color32(0x1E, 0x1E, 0x28, 0xFF);

        var component = new GameObject(typeof(T).Name, typeof(T)).GetComponent<T>();
        if (uxmlPath != null)
        {
            var panel = component.GetComponent<PanelRenderer>();
            panel.panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            panel.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxmlPath);
        }
        configure?.Invoke(component);

        EditorSceneManager.SaveScene(scene, path);

        var scenes = EditorBuildSettings.scenes.Where(s => s.path != path).ToList();
        scenes.Insert(0, new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = scenes.ToArray();

        Debug.Log($"KoreaGame: created {path} and set it as the first build scene.");
    }

    static FontAsset CreateFontAsset(string fontFile, string assetFile)
    {
        string assetPath = $"{FontsFolder}/{assetFile}";
        var existing = AssetDatabase.LoadAssetAtPath<FontAsset>(assetPath);
        if (existing != null) return existing;

        var font = AssetDatabase.LoadAssetAtPath<Font>($"{FontsFolder}/{fontFile}");
        var asset = FontAsset.CreateFontAsset(font);
        AssetDatabase.CreateAsset(asset, assetPath);

        // Dynamic font assets need their atlas and material saved inside the asset.
        asset.material.name = $"{asset.name} Material";
        AssetDatabase.AddObjectToAsset(asset.material, asset);
        asset.atlasTextures[0].name = $"{asset.name} Atlas";
        AssetDatabase.AddObjectToAsset(asset.atlasTextures[0], asset);
        return asset;
    }
}
