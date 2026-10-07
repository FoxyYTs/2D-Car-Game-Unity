using Assets.Logic.AI;
using System;
using System.IO;
using System.Linq;
using UnityEngine;

// Runs generations of CarAgents on a circuit and evolves their brains with a genetic algorithm.
public class TrainingManager : MonoBehaviour
{
    public enum SelectionMethod
    {
        Tournament,
        Roulette
    }

    [Header("Scene")]
    [Tooltip("Inactive car that every agent is cloned from; its pose is the starting line.")]
    public CarAgent AgentTemplate;
    public CheckpointCircuit Circuit;

    [Header("Population")]
    [Min(1)] public int PopulationSize = 30;
    [Min(1)] public int HiddenNeurons = 8;
    [Tooltip("0 picks a different seed on every run; the one used is logged, so a run can be repeated.")]
    public int Seed = 0;

    [Header("Evolution")]
    public SelectionMethod Selection = SelectionMethod.Tournament;
    [Min(1)] public int TournamentSize = 3;
    [Min(0)] public int EliteCount = 2;
    [Range(0, 1)] public float MutationRate = 0.1f;
    [Min(0)] public float MutationSigma = 0.3f;

    [Header("Generation")]
    [Min(1)] public float GenerationSeconds = 60;
    [Range(1, 10)] public float SimulationSpeed = 1;

    [Header("Circuit")]
    [Tooltip("Rearrange the circuit into a new random ring every this many generations, so agents must follow the " +
             "checkpoints instead of memorizing a route. 0 keeps the circuit of the scene.")]
    [Min(0)] public int CircuitChangeEvery = 10;

    public int Generation { get; private set; }
    public int CircuitNumber { get; private set; }
    // Fitness on different circuits is not comparable, so the record restarts with every circuit.
    public float BestOnCircuit { get; private set; }
    public float LastGenerationBest { get; private set; }

    private CarAgent[] agents;
    private GeneticAlgorithm genetics;
    private System.Random random;
    private float[][] population;
    private int[] layers;
    private float elapsed;
    // Fitness the elites had when they were selected; a deterministic simulation must reproduce it exactly.
    private float[] expectedEliteFitness = Array.Empty<float>();

    // Every run saves to its own file, so a new run, however short, never overwrites a trained brain.
    public const string BRAIN_FILE_PREFIX = "brain-seed";
    private int seed;
    private string SavePath => Path.Combine(Application.persistentDataPath, $"{BRAIN_FILE_PREFIX}{seed}.json");

    void Start()
    {
        if (AgentTemplate == null || Circuit == null || Circuit.Count == 0)
        {
            Debug.LogError("TrainingManager needs an agent template and a circuit with at least one checkpoint.");
            enabled = false;
            return;
        }

        AgentTemplate.gameObject.SetActive(false);
        layers = new[] { AgentTemplate.InputCount, HiddenNeurons, CarPilot.OUTPUTS };

        seed = Seed != 0 ? Seed : Math.Max(1, Environment.TickCount & int.MaxValue);
        random = new System.Random(seed);
        Debug.Log($"Training seed {seed} (set it as Seed to repeat this run); brains are saved to {SavePath}");
        ISelection selection = Selection == SelectionMethod.Roulette
                                   ? new RouletteSelection()
                                   : new TournamentSelection(TournamentSize);
        var settings = new GeneticSettings
        {
            PopulationSize = PopulationSize,
            EliteCount = Math.Min(EliteCount, PopulationSize),
            MutationRate = MutationRate,
            MutationSigma = MutationSigma
        };
        genetics = new GeneticAlgorithm(settings, selection, random);

        agents = Enumerable.Range(0, PopulationSize).Select(SpawnAgent).ToArray();
        population = genetics.CreatePopulation(NeuralNetwork.CountParameters(layers));

        Generation = 1;
        CircuitNumber = 1;
        LogCircuit();
        StartGeneration();
    }

    void Update()
    {
        Time.timeScale = SimulationSpeed;
    }

    // The manager is the only one advancing the simulation: every agent takes exactly one step, always in the
    // same order, and a new generation starts between complete steps. Otherwise the same genome could start one
    // step earlier or later depending on Unity's FixedUpdate order and on whether its agent had died.
    void FixedUpdate()
    {
        float deltaTime = Time.fixedDeltaTime;
        foreach (var agent in agents)
            agent.Tick(deltaTime);

        elapsed += deltaTime;

        if (elapsed >= GenerationSeconds || agents.All(agent => !agent.Alive))
            EndGeneration();
    }

    void OnDisable()
    {
        Time.timeScale = 1;
    }

    private CarAgent SpawnAgent(int index)
    {
        var agent = Instantiate(AgentTemplate, AgentTemplate.transform.parent);
        agent.name = $"Agent {index}";
        agent.gameObject.SetActive(true);
        agent.Setup(Circuit, new NeuralNetwork(layers));
        return agent;
    }

    private void StartGeneration()
    {
        elapsed = 0;
        for (int i = 0; i < agents.Length; i++)
            agents[i].Begin(population[i]);
    }

    private void EndGeneration()
    {
        var fitness = agents.Select(agent => agent.Fitness).ToArray();
        int best = Array.IndexOf(fitness, fitness.Max());

        LastGenerationBest = fitness[best];
        Debug.Log($"Generation {Generation} (circuit {CircuitNumber}): best {LastGenerationBest:F2}, " +
                  $"average {fitness.Average():F2}{EliteReplay(fitness)}{Outcomes(best)}");

        bool newRecord = LastGenerationBest > BestOnCircuit;
        if (newRecord)
            BestOnCircuit = LastGenerationBest;

        // With a changing circuit, scores on different circuits are not comparable: the brain worth keeping is the
        // champion that survived a whole circuit. With a fixed circuit, every record is a better brain.
        bool circuitEnds = CircuitChangeEvery > 0 && Generation % CircuitChangeEvery == 0;
        if (circuitEnds || (CircuitChangeEvery == 0 && newRecord))
            Save(population[best], LastGenerationBest);

        expectedEliteFitness = fitness.OrderByDescending(value => value).Take(genetics.Settings.EliteCount).ToArray();
        population = genetics.NextGeneration(population, fitness);
        Generation++;

        if (CircuitChangeEvery > 0 && (Generation - 1) % CircuitChangeEvery == 0)
            ChangeCircuit();

        StartGeneration();
    }

    private void ChangeCircuit()
    {
        Circuit.Rearrange(random);
        CircuitNumber++;
        BestOnCircuit = 0;
        // The elites were scored on the previous circuit: their fitness is not expected to repeat.
        expectedEliteFitness = Array.Empty<float>();
        LogCircuit();
    }

    private void LogCircuit()
    {
        string direction = Circuit.Clockwise ? "clockwise" : "counterclockwise";
        Debug.Log($"Circuit {CircuitNumber} ({direction}) starts at generation {Generation}:{Circuit.Describe()}");
    }

    // How the generation ended, and where the best agent stopped: tells crashes apart from agents stuck near a checkpoint.
    private string Outcomes(int best)
    {
        int crashed = agents.Count(agent => agent.Result == CarAgent.Outcome.Crashed);
        int stalled = agents.Count(agent => agent.Result == CarAgent.Outcome.Stalled);
        int running = agents.Count(agent => agent.Result == CarAgent.Outcome.Running);

        var champion = agents[best];
        Vector2 position = champion.transform.position;
        string ending = champion.Result switch
        {
            CarAgent.Outcome.Crashed => "crashed",
            CarAgent.Outcome.Stalled => "stalled",
            _ => "ran out of time"
        };

        return $"; crashed {crashed}, stalled {stalled}, out of time {running}; " +
               $"best {ending} at ({position.x:F1}, {position.y:F1}) heading to checkpoint {champion.NextCheckpointIndex}";
    }

    // The elites open the population (see GeneticAlgorithm.NextGeneration), so they are the first agents.
    private string EliteReplay(float[] fitness)
    {
        if (expectedEliteFitness.Length == 0)
            return "";

        var replays = expectedEliteFitness.Select((expected, i) => $"{expected:F2}->{fitness[i]:F2}");
        return $", elites {string.Join(" ", replays)}";
    }

    private void Save(float[] genome, float fitness)
    {
        var brain = new SavedBrain
        {
            Seed = seed,
            Generation = Generation,
            Circuit = CircuitNumber,
            Fitness = fitness,
            Layers = layers,
            Genome = genome
        };
        File.WriteAllText(SavePath, JsonUtility.ToJson(brain, true));
        Debug.Log($"Saved the champion of circuit {CircuitNumber} (fitness {fitness:F2}) to {SavePath}");
    }

    void OnGUI()
    {
        if (agents is null)
            return;

        GUILayout.BeginArea(new Rect(10, 10, 260, 195), GUI.skin.box);
        GUILayout.Label($"Generation {Generation}");
        GUILayout.Label(CircuitChangeEvery > 0
                            ? $"Circuit {CircuitNumber} (changes every {CircuitChangeEvery})"
                            : $"Circuit {CircuitNumber} (fixed)");
        GUILayout.Label($"Alive {agents.Count(agent => agent.Alive)} / {agents.Length}");
        GUILayout.Label($"Time {elapsed:F1} / {GenerationSeconds:F0} s");
        GUILayout.Label($"Best of last generation {LastGenerationBest:F2}");
        GUILayout.Label($"Best on this circuit {BestOnCircuit:F2}");
        GUILayout.Label($"Speed x{SimulationSpeed:F1}");
        SimulationSpeed = GUILayout.HorizontalSlider(SimulationSpeed, 1, 10);
        GUILayout.EndArea();
    }

    [Serializable]
    public class SavedBrain
    {
        public int Seed;
        public int Generation;
        public int Circuit;
        public float Fitness;
        public int[] Layers;
        public float[] Genome;
    }
}
