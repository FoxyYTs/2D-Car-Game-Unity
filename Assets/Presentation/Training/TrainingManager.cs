using Assets.Logic.AI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

// Runs generations of CarAgents on a circuit and evolves their brains with a genetic algorithm.
public class TrainingManager : MonoBehaviour, IHudSource, IFitnessHistory
{
    public enum SelectionMethod
    {
        Tournament,
        Roulette
    }

    public enum CourseKind
    {
        // Checkpoints on a ring around the mountain.
        Ring,
        // Checkpoints anywhere on the map, in a random order.
        RandomPoints,
        // No checkpoints: drive through as many cells of the map as possible without crashing.
        Exploration,
        // Catch a runner driven by a trained brain along random points.
        Chase
    }

    private static readonly Color RUNNER_COLOR = new(1f, 0.55f, 0.15f);

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
    public CourseKind Course = CourseKind.Ring;
    [Tooltip("Exploration: side of the map cells; the fitness is the number of cells visited.")]
    [Min(0.5f)] public float ExplorationCellSize = 2.5f;

    [Header("Chase")]
    [Tooltip("File in Application.persistentDataPath that drives the runners. Empty: the most recent brain trained on " +
             "random points or by the curriculum.")]
    public string RunnerBrainFile = "";
    [Tooltip("Chasers start this far behind their runner.")]
    [Min(1)] public float ChaseStartDistance = 6;
    [Tooltip("Seconds the runners drive before the chasers start: with 3 s they were still caught about 2 s later.")]
    [Min(0)] public float RunnerHeadStart = 5;
    [Tooltip("Start the chasers from mutated copies of the runner's brain, which already drives to a target.")]
    public bool SeedChasersFromRunner = true;

    public int Generation { get; private set; }
    public int CircuitNumber { get; private set; }
    // Fitness on different circuits is not comparable, so the record restarts with every circuit.
    public float BestOnCircuit { get; private set; }
    public float LastGenerationBest { get; private set; }
    // Best and average fitness of every finished generation, for the fitness chart.
    public List<Vector2> History { get; } = new();
    // Index in History of the first generation of each new circuit: scores on different circuits are not comparable.
    public List<int> CircuitStarts { get; } = new();

    private CarAgent[] agents;
    // Chase only: runner i is chased by agent i.
    private CarAgent[] runners = Array.Empty<CarAgent>();
    private List<Vector2> freeCells;
    private float[] runnerGenome;
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
        if (Course == CourseKind.Exploration)
        {
            // The map never changes, so every record is a better brain (see EndGeneration).
            CircuitChangeEvery = 0;
            Circuit.gameObject.SetActive(false);
            freeCells = ExplorationTask.FreeCells(Circuit.ArenaMin, Circuit.ArenaMax, ExplorationCellSize, Circuit.ObstacleMask);
        }
        layers = new[] { AgentTemplate.InputCount, HiddenNeurons, CarPilot.OUTPUTS };
        if (Course == CourseKind.Chase && !LoadRunnerBrain())
        {
            enabled = false;
            return;
        }

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

        if (Course == CourseKind.Chase)
            runners = Enumerable.Range(0, PopulationSize).Select(SpawnRunner).ToArray();
        agents = Enumerable.Range(0, PopulationSize).Select(SpawnAgent).ToArray();
        population = Course == CourseKind.Chase && SeedChasersFromRunner
                         ? MutatedCopies(runnerGenome)
                         : genetics.CreatePopulation(NeuralNetwork.CountParameters(layers));

        Generation = 1;
        CircuitNumber = 1;
        // The scene's circuit is a ring: random points need their own course from the first generation.
        if (Scattered)
            Circuit.Scatter(random, AgentTemplate.transform.position);
        LogCircuit();
        StartGeneration();
    }

    void Update()
    {
        if (!PauseMenu.Paused)
            Time.timeScale = SimulationSpeed;
    }

    // The manager is the only one advancing the simulation: every agent takes exactly one step, always in the
    // same order, and a new generation starts between complete steps. Otherwise the same genome could start one
    // step earlier or later depending on Unity's FixedUpdate order and on whether its agent had died.
    void FixedUpdate()
    {
        float deltaTime = Time.fixedDeltaTime;
        foreach (var runner in runners)
            runner.Tick(deltaTime);
        bool chasersWait = Course == CourseKind.Chase && elapsed < RunnerHeadStart;
        if (!chasersWait)
            foreach (var agent in agents)
                agent.Tick(deltaTime);

        elapsed += deltaTime;
        if (Course == CourseKind.Chase && !chasersWait)
            ResolveChases();

        if (elapsed >= GenerationSeconds || agents.All(agent => !agent.Alive))
            EndGeneration();
    }

    void OnDisable()
    {
        Time.timeScale = 1;
    }

    private bool Scattered => Course is CourseKind.RandomPoints or CourseKind.Chase;

    private CarAgent SpawnAgent(int index)
    {
        var agent = Instantiate(AgentTemplate, AgentTemplate.transform.parent);
        agent.name = $"Agent {index}";
        // Setup takes the current pose as the agent's start.
        if (Course == CourseKind.Chase)
            agent.transform.position = ChaserStart();
        agent.gameObject.SetActive(true);
        agent.Setup(NewTask(index), new NeuralNetwork(layers));
        return agent;
    }

    private ITrainingTask NewTask(int index) => Course switch
    {
        CourseKind.Exploration => new ExplorationTask(Circuit.ArenaMin, Circuit.ArenaMax, ExplorationCellSize, freeCells),
        CourseKind.Chase => new ChaseTask(runners[index], Time.fixedDeltaTime, GenerationSeconds - RunnerHeadStart),
        _ => new CheckpointTask(Circuit)
    };

    // Behind the runner if there is room; otherwise beside or ahead of it.
    private Vector3 ChaserStart()
    {
        var start = AgentTemplate.transform;
        var obstacles = new ContactFilter2D { useTriggers = false };
        obstacles.SetLayerMask(Circuit.ObstacleMask);
        var hits = new Collider2D[1];

        foreach (var direction in new[] { -start.up, -start.right, start.right, start.up })
        {
            var position = start.position + direction * ChaseStartDistance;
            bool insideArena = position.x > Circuit.ArenaMin.x && position.x < Circuit.ArenaMax.x &&
                               position.y > Circuit.ArenaMin.y && position.y < Circuit.ArenaMax.y;
            if (insideArena && Physics2D.OverlapCircle(position, 1, obstacles, hits) == 0)
                return position;
        }

        Debug.LogWarning("No free spot around the starting line for the chasers: they start behind it anyway.");
        return start.position - start.up * ChaseStartDistance;
    }

    private CarAgent SpawnRunner(int index)
    {
        var runner = Instantiate(AgentTemplate, AgentTemplate.transform.parent);
        runner.name = $"Runner {index}";
        runner.GetComponent<SpriteRenderer>().color = RUNNER_COLOR;
        runner.IsRunner = true;
        runner.gameObject.SetActive(true);
        runner.Setup(new CheckpointTask(Circuit), new NeuralNetwork(layers));
        return runner;
    }

    private bool LoadRunnerBrain()
    {
        var entry = BrainLibrary.List().FirstOrDefault(brain =>
            string.IsNullOrEmpty(RunnerBrainFile)
                ? brain.Brain.Course is nameof(CourseKind.RandomPoints) or "Curriculum" && brain.Brain.Layers.SequenceEqual(layers)
                : brain.File == RunnerBrainFile);

        if (entry is null || !entry.Brain.Layers.SequenceEqual(layers))
        {
            Debug.LogError($"The chase needs a runner brain with layers [{string.Join(", ", layers)}]: train one on " +
                           "random points or with the curriculum first, or set RunnerBrainFile.");
            return false;
        }

        runnerGenome = entry.Brain.Genome;
        Debug.Log($"Runners are driven by {entry.File} (generation {entry.Brain.Generation}, fitness {entry.Brain.Fitness:F2})");
        return true;
    }

    // The runner's brain itself plus mutated copies of it.
    private float[][] MutatedCopies(float[] genome)
    {
        var copies = new float[PopulationSize][];
        for (int i = 0; i < copies.Length; i++)
        {
            copies[i] = genome.ToArray();
            if (i > 0)
                genetics.Mutate(copies[i]);
        }
        return copies;
    }

    // A chase ends when the chaser touches its runner or the runner is out (crashed or stuck).
    private void ResolveChases()
    {
        // Contacts are checked on the poses just set, not on the ones of the last physics step.
        Physics2D.SyncTransforms();

        for (int i = 0; i < agents.Length; i++)
        {
            var chaser = agents[i];
            var runner = runners[i];
            if (!chaser.Alive || chaser.Task is not ChaseTask chase)
                continue;

            if (!runner.Alive)
            {
                chase.End(ChaseTask.Ending.RunnerOut);
                chaser.Finish();
                continue;
            }

            var contact = ContactRules.Resolve(chaser, runner);
            if (contact == ContactRules.Contact.None)
                continue;

            chase.End(contact == ContactRules.Contact.FirstRams ? ChaseTask.Ending.Rammed : ChaseTask.Ending.Touched);
            chaser.Finish();
            runner.Finish();
        }
    }

    private void StartGeneration()
    {
        elapsed = 0;
        // Runners first: a chaser measures its starting distance to its runner.
        foreach (var runner in runners)
            runner.Begin(runnerGenome);
        for (int i = 0; i < agents.Length; i++)
            agents[i].Begin(population[i]);
    }

    private void EndGeneration()
    {
        var fitness = agents.Select(agent => agent.Fitness).ToArray();
        int best = Array.IndexOf(fitness, fitness.Max());

        LastGenerationBest = fitness[best];
        History.Add(new Vector2(LastGenerationBest, fitness.Average()));
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
        if (Scattered)
            Circuit.Scatter(random, AgentTemplate.transform.position);
        else
            Circuit.Rearrange(random);
        CircuitNumber++;
        CircuitStarts.Add(History.Count);
        BestOnCircuit = 0;
        // The elites were scored on the previous circuit: their fitness is not expected to repeat.
        expectedEliteFitness = Array.Empty<float>();
        LogCircuit();
    }

    private void LogCircuit()
    {
        if (Course == CourseKind.Exploration)
        {
            Debug.Log($"Exploration with {ExplorationCellSize} m cells starts at generation {Generation}");
            return;
        }
        string direction = Scattered ? "random points"
                         : Circuit.Clockwise ? "clockwise" : "counterclockwise";
        Debug.Log($"Circuit {CircuitNumber} ({direction}) starts at generation {Generation}:{Circuit.Describe()}");
    }

    // How the generation ended, and where the best agent stopped: tells crashes apart from agents stuck near a checkpoint.
    private string Outcomes(int best)
    {
        int crashed = agents.Count(agent => agent.Result == CarAgent.Outcome.Crashed);
        int stalled = agents.Count(agent => agent.Result == CarAgent.Outcome.Stalled);
        int finished = agents.Count(agent => agent.Result == CarAgent.Outcome.Finished);
        int running = agents.Count(agent => agent.Result == CarAgent.Outcome.Running);

        var champion = agents[best];
        Vector2 position = champion.transform.position;
        string ending = champion.Result switch
        {
            CarAgent.Outcome.Crashed => "crashed",
            CarAgent.Outcome.Stalled => "stalled",
            CarAgent.Outcome.Finished => "finished",
            _ => "ran out of time"
        };

        string chases = Course == CourseKind.Chase ? $"; {ChaseEndings()}" : "";
        return $"; crashed {crashed}, stalled {stalled}, finished {finished}, out of time {running}{chases}; " +
               $"best {ending} at ({position.x:F1}, {position.y:F1}) {champion.Task.Status}";
    }

    private string ChaseEndings()
    {
        var endings = agents.Select(agent => ((ChaseTask)agent.Task).Result).ToArray();
        return $"rammed {endings.Count(ending => ending == ChaseTask.Ending.Rammed)}, " +
               $"touched {endings.Count(ending => ending == ChaseTask.Ending.Touched)}, " +
               $"runner out {endings.Count(ending => ending == ChaseTask.Ending.RunnerOut)}";
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
            Course = Course.ToString(),
            Fitness = fitness,
            Layers = layers,
            Genome = genome
        };
        File.WriteAllText(SavePath, JsonUtility.ToJson(brain, true));
        Debug.Log($"Saved the champion of circuit {CircuitNumber} (fitness {fitness:F2}) to {SavePath}");
    }

    public bool ShowHud => agents != null;
    public string HudTitle => $"Generación {Generation}";
    public bool HasSpeed => true;
    public float Speed { get => SimulationSpeed; set => SimulationSpeed = value; }

    public IEnumerable<string> HudLines => new[]
    {
        Course == CourseKind.Exploration ? $"Exploración: celdas de {ExplorationCellSize} m" :
        $"{(Course == CourseKind.Chase ? "Persecución, recorrido" : Scattered ? "Recorrido al azar" : "Circuito")} {CircuitNumber} " +
        (CircuitChangeEvery > 0 ? $"(cambia cada {CircuitChangeEvery})" : "(fijo)"),
        $"Vivos {agents.Count(agent => agent.Alive)} / {agents.Length}",
        $"Tiempo {elapsed:F1} / {GenerationSeconds:F0} s",
        $"Mejor de la generación anterior {LastGenerationBest:F2}",
        $"{(Course == CourseKind.Exploration ? "Récord" : "Mejor en este circuito")} {BestOnCircuit:F2}"
    };

    [Serializable]
    public class SavedBrain
    {
        public int Seed;
        public int Generation;
        public int Circuit;
        // CourseKind the brain was trained on; empty in brains saved before courses existed (all rings) and in versus brains.
        public string Course;
        public float Fitness;
        public int[] Layers;
        public float[] Genome;
        // Normalized score on each environment of the validation battery; empty unless trained by the curriculum.
        public EnvironmentScore[] Scores;
    }

    [Serializable]
    public class EnvironmentScore
    {
        public string Name;
        public float Score;
    }
}
