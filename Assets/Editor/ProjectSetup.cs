using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.iOS;

public static class ProjectSetup
{
    const string InputTestScenePath = "Assets/Scenes/InputTest.unity";

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

    [MenuItem("KoreaGame/Create Input Test Scene")]
    public static void CreateInputTestScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var camera = new GameObject("Main Camera", typeof(Camera)).GetComponent<Camera>();
        camera.tag = "MainCamera";
        camera.orthographic = true;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color32(0x1E, 0x1E, 0x28, 0xFF);

        new GameObject("InputTest", typeof(InputTestGame));

        EditorSceneManager.SaveScene(scene, InputTestScenePath);

        // Put the test scene first so a device build boots straight into it.
        var scenes = EditorBuildSettings.scenes.Where(s => s.path != InputTestScenePath).ToList();
        scenes.Insert(0, new EditorBuildSettingsScene(InputTestScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();

        Debug.Log($"KoreaGame: created {InputTestScenePath} and set it as the first build scene.");
    }
}
