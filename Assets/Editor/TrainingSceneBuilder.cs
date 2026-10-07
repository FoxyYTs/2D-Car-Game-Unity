using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Builds the AI training scene from the versus scene: same map, no cockpit or UI, CarA turned into the agent template.
public static class TrainingSceneBuilder
{
    private const string SOURCE_SCENE = "Assets/Scenes/SampleScene.unity";
    private const string TRAINING_SCENE = "Assets/Scenes/TrainingScene.unity";
    private const string AGENT_LAYER = "Agent";
    private const int FIRST_USER_LAYER = 8;
    private const float CHECKPOINT_RADIUS = 2.5f;

    // Rough loop around the mountain to start from; move the checkpoints in the editor to shape the real circuit.
    private static readonly Vector2[] PLACEHOLDER_CHECKPOINTS =
    {
        new(-10, 7), new(1, 9), new(13, 7), new(13, -4), new(1, -6), new(-10, -5)
    };

    [MenuItem("Car Game/Create Training Scene")]
    public static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(TRAINING_SCENE) != null &&
            !EditorUtility.DisplayDialog("Training scene", $"{TRAINING_SCENE} already exists. Replace it?", "Replace", "Cancel"))
            return;

        int agentLayer = EnsureLayer(AGENT_LAYER);
        if (agentLayer < 0)
        {
            EditorUtility.DisplayDialog("Training scene", "There is no free user layer for the agents.", "OK");
            return;
        }
        // Agents share the track: they must not crash into each other.
        Physics2D.IgnoreLayerCollision(agentLayer, agentLayer, true);

        var scene = EditorSceneManager.OpenScene(SOURCE_SCENE, OpenSceneMode.Single);

        DestroyRoot(scene, "Canvas");
        DestroyRoot(scene, "EventSystem");
        Object.DestroyImmediate(GameObject.Find("CarB"));
        StripVersusComponents(GameObject.Find("Main Camera"));
        MakeObstaclesStatic(GameObject.Find("Obstacles"));

        var template = BuildAgentTemplate(GameObject.Find("CarA"), agentLayer);
        var circuit = BuildPlaceholderCircuit();

        var manager = new GameObject("TrainingManager").AddComponent<TrainingManager>();
        manager.AgentTemplate = template;
        manager.Circuit = circuit;

        EditorSceneManager.SaveScene(scene, TRAINING_SCENE);
        AssetDatabase.SaveAssets();

        Selection.activeGameObject = circuit.gameObject;
        Debug.Log($"{TRAINING_SCENE} created. Arrange the children of 'Checkpoints' (hierarchy order = circuit order) and press Play.");
    }

    private static CarAgent BuildAgentTemplate(GameObject car, int layer)
    {
        Object.DestroyImmediate(car.GetComponent<CarKeyboardReader>());

        var writer = car.GetComponent<CarWritter>();
        writer.engineSound = null;
        writer.ExternalStep = true;
        foreach (var audio in car.GetComponents<AudioSource>())
            Object.DestroyImmediate(audio);

        car.AddComponent<CarSensors>();
        var agent = car.AddComponent<CarAgent>();

        car.name = "AgentTemplate";
        foreach (var child in car.GetComponentsInChildren<Transform>(true))
            child.gameObject.layer = layer;
        car.SetActive(false);

        return agent;
    }

    private static CheckpointCircuit BuildPlaceholderCircuit()
    {
        var circuit = new GameObject("Checkpoints").AddComponent<CheckpointCircuit>();

        for (int i = 0; i < PLACEHOLDER_CHECKPOINTS.Length; i++)
        {
            var checkpoint = new GameObject($"Checkpoint {i}");
            checkpoint.transform.SetParent(circuit.transform);
            checkpoint.transform.position = PLACEHOLDER_CHECKPOINTS[i];

            var trigger = checkpoint.AddComponent<CircleCollider2D>();
            trigger.isTrigger = true;
            trigger.radius = CHECKPOINT_RADIUS;
            checkpoint.AddComponent<Checkpoint>();
        }

        return circuit;
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
