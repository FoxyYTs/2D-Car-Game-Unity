using Assets.Logic.AI;
using EngineAbstractor;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

// Lets a trained brain drive this car in a versus match, aiming at the opponent.
// Enabled: the AI drives. Disabled (also during play): the keyboard drives again.
[RequireComponent(typeof(CarWritter), typeof(CarSensors))]
public class AIDriver : MonoBehaviour, IInputSource
{
    [Tooltip("File in Application.persistentDataPath saved by the TrainingManager. Empty: the most recent brain-seed*.json.")]
    public string BrainFile = "";
    [Tooltip("Must be the value the brain was trained with.")]
    [Min(1)] public float TargetDistanceScale = 50;

    public bool Driving { get; private set; }

    private CarWritter writer;
    private CarPilot pilot;
    private Transform opponent;
    private bool started;

    void Start()
    {
        writer = GetComponent<CarWritter>();
        opponent = FindObjectsByType<CarWritter>(FindObjectsSortMode.None)
                       .FirstOrDefault(car => car != writer)?.transform;
        pilot = LoadPilot();
        started = true;

        TakeControl();

        if (FindAnyObjectByType<VersusExhibition>() == null)
            new GameObject(nameof(VersusExhibition)).AddComponent<VersusExhibition>();
    }

    void OnEnable()
    {
        if (started)
            TakeControl();
    }

    void OnDisable()
    {
        if (started)
            ReleaseControl();
    }

    // Same constant step the brain was trained with.
    void FixedUpdate()
    {
        if (Driving)
            writer.Step(Time.fixedDeltaTime);
    }

    public IReadOnlyList<InputValue> Read() => pilot.Drive(opponent.position);

    private void TakeControl()
    {
        if (pilot == null || opponent == null)
            return;

        writer.ExternalStep = true;
        writer.Location.InputSource = this;
        Driving = true;
    }

    private void ReleaseControl()
    {
        writer.ExternalStep = false;
        writer.Location.InputSource = new KeyboardInputSource(writer.Id);
        Driving = false;
    }

    private string BrainPath()
    {
        if (!string.IsNullOrEmpty(BrainFile))
            return Path.Combine(Application.persistentDataPath, BrainFile);

        return new DirectoryInfo(Application.persistentDataPath)
                   .GetFiles($"{TrainingManager.BRAIN_FILE_PREFIX}*.json")
                   .OrderByDescending(file => file.LastWriteTimeUtc)
                   .FirstOrDefault()?.FullName;
    }

    private CarPilot LoadPilot()
    {
        string path = BrainPath();
        if (path is null || !File.Exists(path))
        {
            Debug.LogError($"{name}: there is no brain in {Application.persistentDataPath}. Train one in TrainingScene first.");
            return null;
        }
        string file = Path.GetFileName(path);

        var saved = JsonUtility.FromJson<TrainingManager.SavedBrain>(File.ReadAllText(path));
        var sensors = GetComponent<CarSensors>();
        int inputs = CarPilot.InputCount(sensors);
        var layers = saved.Layers ?? new int[0];

        if (layers.Length < 2 || layers[0] != inputs || layers[layers.Length - 1] != CarPilot.OUTPUTS)
        {
            Debug.LogError($"{name}: {file} has layers [{string.Join(", ", layers)}], " +
                           $"but this car needs {inputs} inputs and {CarPilot.OUTPUTS} outputs.");
            return null;
        }

        var brain = new NeuralNetwork(layers);
        brain.SetParameters(saved.Genome);
        Debug.Log($"{name} is driven by {file} (generation {saved.Generation}, circuit {saved.Circuit}, fitness {saved.Fitness:F2})");

        return new CarPilot(writer, sensors, brain, TargetDistanceScale);
    }
}
