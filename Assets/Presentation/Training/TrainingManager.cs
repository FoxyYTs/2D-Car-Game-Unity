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

    private const int OUTPUTS = 2;

    [Header("Scene")]
    [Tooltip("Inactive car that every agent is cloned from; its pose is the starting line.")]
    public CarAgent AgentTemplate;
    public CheckpointCircuit Circuit;

    [Header("Population")]
    [Min(1)] public int PopulationSize = 30;
    [Min(1)] public int HiddenNeurons = 8;
    [Tooltip("0 picks a different seed on every run.")]
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

    public int Generation { get; private set; }
    public float BestFitness { get; private set; }
    public float LastGenerationBest { get; private set; }

    private CarAgent[] agents;
    private GeneticAlgorithm genetics;
    private float[][] population;
    private int[] layers;
    private float elapsed;

    private string SavePath => Path.Combine(Application.persistentDataPath, "best-brain.json");

    void Start()
    {
        if (AgentTemplate == null || Circuit == null || Circuit.Count == 0)
        {
            Debug.LogError("TrainingManager needs an agent template and a circuit with at least one checkpoint.");
            enabled = false;
            return;
        }

        AgentTemplate.gameObject.SetActive(false);
        layers = new[] { AgentTemplate.InputCount, HiddenNeurons, OUTPUTS };

        var random = Seed == 0 ? new System.Random() : new System.Random(Seed);
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
        Debug.Log($"Generation {Generation}: best {LastGenerationBest:F2}, average {fitness.Average():F2}");

        if (LastGenerationBest > BestFitness)
        {
            BestFitness = LastGenerationBest;
            Save(population[best], LastGenerationBest);
        }

        population = genetics.NextGeneration(population, fitness);
        Generation++;
        StartGeneration();
    }

    private void Save(float[] genome, float fitness)
    {
        var brain = new SavedBrain
        {
            Generation = Generation,
            Fitness = fitness,
            Layers = layers,
            Genome = genome
        };
        File.WriteAllText(SavePath, JsonUtility.ToJson(brain, true));
    }

    void OnGUI()
    {
        if (agents is null)
            return;

        GUILayout.BeginArea(new Rect(10, 10, 260, 170), GUI.skin.box);
        GUILayout.Label($"Generation {Generation}");
        GUILayout.Label($"Alive {agents.Count(agent => agent.Alive)} / {agents.Length}");
        GUILayout.Label($"Time {elapsed:F1} / {GenerationSeconds:F0} s");
        GUILayout.Label($"Best of last generation {LastGenerationBest:F2}");
        GUILayout.Label($"Best ever {BestFitness:F2}");
        GUILayout.Label($"Speed x{SimulationSpeed:F1}");
        SimulationSpeed = GUILayout.HorizontalSlider(SimulationSpeed, 1, 10);
        GUILayout.EndArea();
    }

    [Serializable]
    public class SavedBrain
    {
        public int Generation;
        public float Fitness;
        public int[] Layers;
        public float[] Genome;
    }
}
