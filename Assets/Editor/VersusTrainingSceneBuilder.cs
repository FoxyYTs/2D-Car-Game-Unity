using UnityEditor;
using UnityEngine;

// Builds the versus training scene: the versus map, the two starting points of the game and CarA turned into
// the template of the dueling agents.
public static class VersusTrainingSceneBuilder
{
    private const string VERSUS_TRAINING_SCENE = "Assets/Scenes/VersusTrainingScene.unity";

    [MenuItem("Car Game/Create Versus Training Scene")]
    public static void Build()
    {
        if (!ArenaBuilder.TryOpen(VERSUS_TRAINING_SCENE, out var scene, out int agentLayer))
            return;

        var carA = GameObject.Find("CarA");
        var carB = GameObject.Find("CarB");
        var leftStart = StartingPoint("LeftStart", carA.transform);
        var rightStart = StartingPoint("RightStart", carB.transform);

        Object.DestroyImmediate(carB);
        var template = ArenaBuilder.PrepareTemplate(carA, agentLayer).AddComponent<DuelAgent>();

        var manager = new GameObject("VersusTrainingManager").AddComponent<VersusTrainingManager>();
        manager.AgentTemplate = template;
        manager.LeftStart = leftStart;
        manager.RightStart = rightStart;

        ArenaBuilder.Save(scene, VERSUS_TRAINING_SCENE);

        Selection.activeGameObject = manager.gameObject;
        Debug.Log($"{VERSUS_TRAINING_SCENE} created. Press Play to train.");
    }

    private static Transform StartingPoint(string name, Transform car)
    {
        var point = new GameObject(name).transform;
        point.SetPositionAndRotation(car.position, car.rotation);
        return point;
    }
}
