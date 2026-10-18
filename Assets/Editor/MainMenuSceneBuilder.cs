using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Creates the main menu scene, puts every game scene in the build, and makes Play start from the menu.
[InitializeOnLoad]
public static class MainMenuSceneBuilder
{
    private const string MENU_SCENE = "Assets/Scenes/" + GameSession.MENU_SCENE + ".unity";
    private const string PLAY_FROM_MENU_ITEM = "Car Game/Play From Main Menu";
    private const string PLAY_FROM_MENU_PREF = "CarGame.PlayFromMainMenu";
    private static readonly string[] GAME_SCENES =
    {
        "Assets/Scenes/SampleScene.unity", "Assets/Scenes/TrainingScene.unity", "Assets/Scenes/VersusTrainingScene.unity"
    };
    private static readonly Color BACKGROUND = new(0.118f, 0.118f, 0.18f);

    // The start scene is not saved with the project: apply the preference on every editor load.
    static MainMenuSceneBuilder()
    {
        EditorApplication.delayCall += ApplyPlayFromMenu;
    }

    [MenuItem("Car Game/Create Main Menu Scene")]
    public static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(MENU_SCENE) != null &&
            !EditorUtility.DisplayDialog("Main menu", $"{MENU_SCENE} already exists. Replace it?", "Replace", "Cancel"))
            return;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camera = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
        camera.orthographic = true;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = BACKGROUND;
        new GameObject(nameof(MainMenu)).AddComponent<MainMenu>();
        EditorSceneManager.SaveScene(scene, MENU_SCENE);

        EditorBuildSettings.scenes = new[] { MENU_SCENE }.Concat(GAME_SCENES)
                                                         .Where(path => AssetDatabase.LoadAssetAtPath<SceneAsset>(path) != null)
                                                         .Select(path => new EditorBuildSettingsScene(path, true))
                                                         .ToArray();

        // Otherwise the build list stays in memory until the project is saved.
        AssetDatabase.SaveAssets();

        EditorPrefs.SetBool(PLAY_FROM_MENU_PREF, true);
        ApplyPlayFromMenu();
        Debug.Log($"{MENU_SCENE} created and added to the build with every game scene. Play now starts from the menu " +
                  $"({PLAY_FROM_MENU_ITEM} turns it off).");
    }

    [MenuItem(PLAY_FROM_MENU_ITEM)]
    private static void TogglePlayFromMenu()
    {
        EditorPrefs.SetBool(PLAY_FROM_MENU_PREF, !EditorPrefs.GetBool(PLAY_FROM_MENU_PREF, true));
        ApplyPlayFromMenu();
    }

    [MenuItem(PLAY_FROM_MENU_ITEM, true)]
    private static bool TogglePlayFromMenuValidate()
    {
        Menu.SetChecked(PLAY_FROM_MENU_ITEM, EditorPrefs.GetBool(PLAY_FROM_MENU_PREF, true));
        return AssetDatabase.LoadAssetAtPath<SceneAsset>(MENU_SCENE) != null;
    }

    private static void ApplyPlayFromMenu()
    {
        var menu = AssetDatabase.LoadAssetAtPath<SceneAsset>(MENU_SCENE);
        EditorSceneManager.playModeStartScene = menu != null && EditorPrefs.GetBool(PLAY_FROM_MENU_PREF, true) ? menu : null;
    }
}
