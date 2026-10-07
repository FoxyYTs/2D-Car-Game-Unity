using UnityEditor;
using UnityEngine;

// Builds the checkpoint training scene from the versus scene: same map, CarA turned into the agent template.
public static class TrainingSceneBuilder
{
    private const string TRAINING_SCENE = "Assets/Scenes/TrainingScene.unity";
    private const float CHECKPOINT_RADIUS = 2.5f;

    // Rough loop around the mountain to start from; move the checkpoints in the editor to shape the real circuit.
    private static readonly Vector2[] PLACEHOLDER_CHECKPOINTS =
    {
        new(-10, 7), new(1, 9), new(13, 7), new(13, -4), new(1, -6), new(-10, -5)
    };

    [MenuItem("Car Game/Create Training Scene")]
    public static void Build()
    {
        if (!ArenaBuilder.TryOpen(TRAINING_SCENE, out var scene, out int agentLayer))
            return;

        Object.DestroyImmediate(GameObject.Find("CarB"));
        var template = ArenaBuilder.PrepareTemplate(GameObject.Find("CarA"), agentLayer).AddComponent<CarAgent>();
        var circuit = BuildPlaceholderCircuit();

        var manager = new GameObject("TrainingManager").AddComponent<TrainingManager>();
        manager.AgentTemplate = template;
        manager.Circuit = circuit;

        ArenaBuilder.Save(scene, TRAINING_SCENE);

        Selection.activeGameObject = circuit.gameObject;
        Debug.Log($"{TRAINING_SCENE} created. Arrange the children of 'Checkpoints' (hierarchy order = circuit order) and press Play.");
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
}
