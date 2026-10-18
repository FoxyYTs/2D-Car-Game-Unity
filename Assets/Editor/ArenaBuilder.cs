using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Shared by the training scene builders: the versus map without cockpit, UI or game rules, and a car
// stripped down into an inactive template for agents on their own layer.
internal static class ArenaBuilder
{
    private const string SOURCE_SCENE = "Assets/Scenes/SampleScene.unity";
    private const string AGENT_LAYER = "Agent";
    private const int FIRST_USER_LAYER = 8;

    // Asks to save open changes and to replace an existing scene; returns false if the user cancels.
    public static bool TryOpen(string targetScene, out Scene scene, out int agentLayer)
    {
        scene = default;
        agentLayer = -1;

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return false;

        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(targetScene) != null &&
            !EditorUtility.DisplayDialog("Training scene", $"{targetScene} already exists. Replace it?", "Replace", "Cancel"))
            return false;

        agentLayer = EnsureLayer(AGENT_LAYER);
        if (agentLayer < 0)
        {
            EditorUtility.DisplayDialog("Training scene", "There is no free user layer for the agents.", "OK");
            return false;
        }
        // Agents share the arena: they must not crash into each other.
        Physics2D.IgnoreLayerCollision(agentLayer, agentLayer, true);

        scene = EditorSceneManager.OpenScene(SOURCE_SCENE, OpenSceneMode.Single);

        DestroyRoot(scene, "Canvas");
        DestroyRoot(scene, "EventSystem");
        StripVersusComponents(GameObject.Find("Main Camera"));
        MakeObstaclesStatic(GameObject.Find("Obstacles"));
        return true;
    }

    public static void Save(Scene scene, string targetScene)
    {
        EditorSceneManager.SaveScene(scene, targetScene);
        AssetDatabase.SaveAssets();
    }

    // Leaves the car driven by nobody, without sound, with sensors and on the agent layer; the caller adds the agent.
    public static GameObject PrepareTemplate(GameObject car, int layer)
    {
        Object.DestroyImmediate(car.GetComponent<CarKeyboardReader>());
        Object.DestroyImmediate(car.GetComponent<AIDriver>());

        var writer = car.GetComponent<CarWritter>();
        writer.engineSound = null;
        writer.ExternalStep = true;
        foreach (var audio in car.GetComponents<AudioSource>())
            Object.DestroyImmediate(audio);

        if (car.GetComponent<CarSensors>() == null)
            car.AddComponent<CarSensors>();

        car.name = "AgentTemplate";
        foreach (var child in car.GetComponentsInChildren<Transform>(true))
            child.gameObject.layer = layer;
        car.SetActive(false);

        return car;
    }

    // The game controller ends the match on the first crash; training has its own manager.
    private static void StripVersusComponents(GameObject camera)
    {
        Object.DestroyImmediate(camera.GetComponent<GameController>());
        Object.DestroyImmediate(camera.GetComponent<GameWritter>());
        foreach (var audio in camera.GetComponents<AudioSource>())
            Object.DestroyImmediate(audio);
    }

    // Some obstacles have dynamic bodies; hundreds of crashes per generation would slowly push them around.
    private static void MakeObstaclesStatic(GameObject obstacles)
    {
        foreach (var body in obstacles.GetComponentsInChildren<Rigidbody2D>(true))
            body.bodyType = RigidbodyType2D.Static;
    }

    private static void DestroyRoot(Scene scene, string name)
    {
        foreach (var root in scene.GetRootGameObjects())
            if (root.name == name)
                Object.DestroyImmediate(root);
    }

    private static int EnsureLayer(string name)
    {
        int existing = LayerMask.NameToLayer(name);
        if (existing >= 0)
            return existing;

        var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layers = tagManager.FindProperty("layers");

        for (int i = FIRST_USER_LAYER; i < layers.arraySize; i++)
        {
            var layer = layers.GetArrayElementAtIndex(i);
            if (string.IsNullOrEmpty(layer.stringValue))
            {
                layer.stringValue = name;
                tagManager.ApplyModifiedPropertiesWithoutUndo();
                return i;
            }
        }

        return -1;
    }
}
