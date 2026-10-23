using Assets.Logic.AI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using CourseKind = TrainingManager.CourseKind;

// Evolves brains that must do well everywhere. Every generation each genome drives, one stage after another, a ring,
// a random-points course and the exploration map; each stage gives a score normalized to [0, 1] and the fitness
// combines them, so an easy environment cannot hide one where the brain fails.
// Every ValidateEvery generations the best genomes drive a fixed battery of courses that training never uses. That
// score is comparable between generations and between runs (the battery comes from ValidationSeed), and a brain is
// only saved when it beats the best validation so far.
public class CurriculumTrainingManager : MonoBehaviour, IHudSource, IFitnessHistory
{
    private class Stage
    {
        public CourseKind Kind;
        // Checkpoint layout of the ring and random-points stages.
        public Vector2[] Checkpoints;
    }

    private static readonly CourseKind[] ENVIRONMENTS = { CourseKind.Ring, CourseKind.RandomPoints, CourseKind.Exploration };

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
    public TrainingManager.SelectionMethod Selection = TrainingManager.SelectionMethod.Tournament;
    [Min(1)] public int TournamentSize = 3;
    [Min(0)] public int EliteCount = 2;
    [Range(0, 1)] public float MutationRate = 0.1f;
    [Min(0)] public float MutationSigma = 0.3f;

    [Header("Stages")]
    [Min(1)] public float StageSeconds = 60;
    [Range(1, 10)] public float SimulationSpeed = 1;
    [Tooltip("New training courses every this many generations.")]
    [Min(1)] public int CircuitChangeEvery = 10;
    [Min(0.5f)] public float ExplorationCellSize = 2.5f;

    [Header("Scoring")]
    [Tooltip("Laps of the ring worth a full score.")]
    [Min(1)] public int RingLaps = 2;
    [Tooltip("Share of a stage's score lost by crashing into an obstacle: without it, agents learned to explore " +
             "at full speed until they crashed.")]
    [Range(0, 1)] public float CrashPenalty = 0.3f;
    [Tooltip("Weight of the worst environment in the combined fitness (the rest is the average).")]
    [Range(0, 1)] public float WorstWeight = 0.5f;

    [Header("Validation")]
    [Min(1)] public int ValidateEvery = 10;
    [Min(1)] public int ValidationCandidates = 5;
    [Tooltip("Always the same, so validation scores compare across runs.")]
    public int ValidationSeed = 20261018;
    [Min(1)] public int ValidationRings = 3;
    [Min(1)] public int ValidationPointCourses = 3;

    [Header("Exam")]
    [Tooltip("Brain files to examine instead of training (empty list: every brain that fits these cars). " +
             "Null trains. Each brain drives the validation battery once and gets a report card.")]
    [HideInInspector] public List<string> ExamFiles;

    public int Generation { get; private set; }
    public float LastGenerationBest { get; private set; }
    public float BestValidation { get; private set; }
    public List<Vector2> History { get; } = new();
    public List<int> CircuitStarts { get; } = new();

    private CarAgent[] agents;
    private CheckpointTask[] checkpointTasks;
    private ExplorationTask[] explorationTasks;
    private GeneticAlgorithm genetics;
    private System.Random random;
    private float[][] population;
    private int[] layers;
    private int seed;
    private List<Vector2> freeCells;

    private List<Stage> trainingStages;
    private List<Stage> validationStages;
    // The stages being driven and, for each slot, the population index it drives.
    private List<Stage> stages;
    private int[] genomeOf;
    private int stageIndex;
    private float[][] scores;
    private bool validating;
    private float elapsed;
    private float[] expectedEliteFitness = Array.Empty<float>();
    private string lastValidation = "";

    private bool Examining => ExamFiles != null;
    private List<BrainLibrary.Entry> examBrains;
    // Outcome of each slot on each stage, for the report card.
    private CarAgent.Outcome[][] outcomes;
    private string[][] obstacles;
    private readonly List<string> examReport = new();
    private bool examDone;

    private string SavePath => Path.Combine(Application.persistentDataPath, $"{TrainingManager.BRAIN_FILE_PREFIX}{seed}.json");
    private Stage CurrentStage => stages[stageIndex];

    void Start()
    {
        if (AgentTemplate == null || Circuit == null || Circuit.Count == 0)
        {
            Debug.LogError("CurriculumTrainingManager needs an agent template and a circuit with at least one checkpoint.");
            enabled = false;
            return;
        }

        AgentTemplate.gameObject.SetActive(false);
        layers = new[] { AgentTemplate.InputCount, HiddenNeurons, CarPilot.OUTPUTS };
        if (Examining && !LoadExamBrains())
        {
            enabled = false;
            return;
        }
        freeCells = ExplorationTask.FreeCells(Circuit.ArenaMin, Circuit.ArenaMax, ExplorationCellSize, Circuit.ObstacleMask);

        seed = Seed != 0 ? Seed : Math.Max(1, Environment.TickCount & int.MaxValue);
        random = new System.Random(seed);
        Debug.Log($"Curriculum training seed {seed} (set it as Seed to repeat this run); brains are saved to {SavePath}. " +
                  $"Exploration map: {freeCells.Count} free cells of {ExplorationCellSize} m");

        ISelection selection = Selection == TrainingManager.SelectionMethod.Roulette
                                   ? new RouletteSelection()
                                   : new TournamentSelection(TournamentSize);
        genetics = new GeneticAlgorithm(new GeneticSettings
        {
            PopulationSize = PopulationSize,
            EliteCount = Math.Min(EliteCount, PopulationSize),
            MutationRate = MutationRate,
            MutationSigma = MutationSigma
        }, selection, random);

        agents = Enumerable.Range(0, PopulationSize).Select(SpawnAgent).ToArray();
        checkpointTasks = agents.Select(_ => new CheckpointTask(Circuit)).ToArray();
        explorationTasks = agents.Select(_ => new ExplorationTask(Circuit.ArenaMin, Circuit.ArenaMax, ExplorationCellSize, freeCells)).ToArray();
        population = Examining
                         ? examBrains.Select(entry => entry.Brain.Genome).ToArray()
                         : genetics.CreatePopulation(NeuralNetwork.CountParameters(layers));

        // Its own random, so the battery does not depend on the training seed.
        validationStages = BuildStages(new System.Random(ValidationSeed), ValidationRings, ValidationPointCourses);
        trainingStages = BuildStages(random, 1, 1);

        Generation = 1;
        if (Examining)
            StartValidation(Enumerable.Range(0, PopulationSize).ToArray());
        else
            StartGeneration();
    }

    // Every brain of ExamFiles (or every one, if empty) whose inputs and outputs fit these cars.
    private bool LoadExamBrains()
    {
        examBrains = BrainLibrary.List()
                                 .Where(entry => ExamFiles.Count == 0 || ExamFiles.Contains(entry.File))
                                 .Where(entry => entry.Brain.Layers[0] == AgentTemplate.InputCount &&
                                                 entry.Brain.Layers[entry.Brain.Layers.Length - 1] == CarPilot.OUTPUTS)
                                 .ToList();
        if (examBrains.Count == 0)
        {
            Debug.LogError("There is no brain to examine that fits these cars.");
            return false;
        }

        PopulationSize = examBrains.Count;
        Debug.Log($"Examining {examBrains.Count} brains: {string.Join(", ", examBrains.Select(entry => entry.File))}");
        return true;
    }

    void Update()
    {
        if (!PauseMenu.Paused)
            Time.timeScale = examDone ? 1 : SimulationSpeed;
    }

    void OnDisable()
    {
        Time.timeScale = 1;
    }

    // Like the other trainers, the manager alone advances the simulation, so runs are repeatable.
    void FixedUpdate()
    {
        if (agents is null || examDone)
            return;

        float deltaTime = Time.fixedDeltaTime;
        foreach (var agent in agents)
            agent.Tick(deltaTime);
        elapsed += deltaTime;

        if (elapsed >= StageSeconds || agents.All(agent => !agent.Alive))
            EndStage();
    }

    private CarAgent SpawnAgent(int index)
    {
        var agent = Instantiate(AgentTemplate, AgentTemplate.transform.parent);
        agent.name = $"Agent {index}";
        agent.gameObject.SetActive(true);
        // Examined brains may differ in their hidden layers.
        agent.Setup(new CheckpointTask(Circuit), new NeuralNetwork(Examining ? examBrains[index].Brain.Layers : layers));
        return agent;
    }

    // Rings and random-points courses, plus the exploration map, in that order.
    private List<Stage> BuildStages(System.Random source, int rings, int pointCourses)
    {
        var built = new List<Stage>();
        for (int i = 0; i < rings; i++)
        {
            Circuit.Rearrange(source);
            built.Add(new Stage { Kind = CourseKind.Ring, Checkpoints = Circuit.Positions });
        }
        for (int i = 0; i < pointCourses; i++)
        {
            Circuit.Scatter(source, AgentTemplate.transform.position);
            built.Add(new Stage { Kind = CourseKind.RandomPoints, Checkpoints = Circuit.Positions });
        }
        built.Add(new Stage { Kind = CourseKind.Exploration });
        return built;
    }

    private void StartGeneration()
    {
        validating = false;
        stages = trainingStages;
        genomeOf = Enumerable.Range(0, PopulationSize).ToArray();
        StartStages();
    }

    private void StartValidation(int[] candidates)
    {
        validating = true;
        stages = validationStages;
        genomeOf = candidates;
        StartStages();
    }

    private void StartStages()
    {
        scores = genomeOf.Select(_ => new float[stages.Count]).ToArray();
        outcomes = genomeOf.Select(_ => new CarAgent.Outcome[stages.Count]).ToArray();
        obstacles = genomeOf.Select(_ => new string[stages.Count]).ToArray();
        stageIndex = 0;
        StartStage();
    }

    private void StartStage()
    {
        var stage = CurrentStage;
        bool exploration = stage.Kind == CourseKind.Exploration;
        Circuit.gameObject.SetActive(!exploration);
        if (!exploration)
            Circuit.Positions = stage.Checkpoints;

        elapsed = 0;
        for (int slot = 0; slot < agents.Length; slot++)
        {
            if (slot >= genomeOf.Length)
            {
                agents[slot].Bench();
                continue;
            }

            agents[slot].UseTask(exploration ? explorationTasks[slot] : checkpointTasks[slot]);
            agents[slot].Begin(population[genomeOf[slot]]);
        }
    }

    private void EndStage()
    {
        var stage = CurrentStage;
        for (int slot = 0; slot < genomeOf.Length; slot++)
        {
            scores[slot][stageIndex] = Score(agents[slot], stage);
            outcomes[slot][stageIndex] = agents[slot].Result;
            obstacles[slot][stageIndex] = agents[slot].Result == CarAgent.Outcome.Crashed ? agents[slot].CrashedInto : null;
        }

        if (!validating)
            LogStage(stage);

        stageIndex++;
        if (stageIndex < stages.Count)
            StartStage();
        else if (validating)
            EndValidation();
        else
            EndGeneration();
    }

    // How well the agent did on this stage, from 0 to 1.
    private float Score(CarAgent agent, Stage stage)
    {
        float fitness = agent.Fitness;
        float score = stage.Kind switch
        {
            CourseKind.Ring => fitness / (Circuit.Count * RingLaps),
            CourseKind.RandomPoints => fitness / Circuit.Count,
            _ => fitness / Math.Max(1, freeCells.Count)
        };

        if (agent.Result == CarAgent.Outcome.Crashed)
            score *= 1 - CrashPenalty;
        return Mathf.Clamp01(score);
    }

    private void LogStage(Stage stage)
    {
        var stageScores = scores.Select(row => row[stageIndex]).ToArray();
        int crashed = agents.Count(agent => agent.Result == CarAgent.Outcome.Crashed);
        int stalled = agents.Count(agent => agent.Result == CarAgent.Outcome.Stalled);
        Debug.Log($"Generation {Generation} {stage.Kind}: best {stageScores.Max():F2}, average {stageScores.Average():F2}; " +
                  $"crashed {crashed}, stalled {stalled}, out of time {agents.Length - crashed - stalled}");
    }

    private void EndGeneration()
    {
        var fitness = scores.Select(row => FitnessAggregator.Combine(row, WorstWeight)).ToArray();
        int best = Array.IndexOf(fitness, fitness.Max());

        LastGenerationBest = fitness[best];
        History.Add(new Vector2(LastGenerationBest, fitness.Average()));
        string bestScores = string.Join(", ", stages.Select((stage, i) => $"{stage.Kind} {scores[best][i]:F2}"));
        Debug.Log($"Generation {Generation}: best {LastGenerationBest:F3} ({bestScores}), average {fitness.Average():F3}" +
                  EliteReplay(fitness));

        expectedEliteFitness = fitness.OrderByDescending(value => value).Take(genetics.Settings.EliteCount).ToArray();
        pendingFitness = fitness;

        if (Generation % ValidateEvery == 0)
        {
            var candidates = Enumerable.Range(0, PopulationSize)
                                       .OrderByDescending(individual => fitness[individual])
                                       .Take(Math.Min(ValidationCandidates, PopulationSize))
                                       .ToArray();
            StartValidation(candidates);
        }
        else
            NextGeneration();
    }

    private float[] pendingFitness;

    private void NextGeneration()
    {
        population = genetics.NextGeneration(population, pendingFitness);
        Generation++;

        if ((Generation - 1) % CircuitChangeEvery == 0)
        {
            trainingStages = BuildStages(random, 1, 1);
            CircuitStarts.Add(History.Count);
            // The elites were scored on the previous courses: their fitness is not expected to repeat.
            expectedEliteFitness = Array.Empty<float>();
        }

        StartGeneration();
    }

    // Stages of the same kind are averaged first, so the battery's several rings weigh as much as its one map.
    private void EndValidation()
    {
        if (Examining)
        {
            EndExam();
            return;
        }

        int champion = -1;
        float championScore = -1;
        EnvironmentScores championScores = null;
        var lines = new List<string>();

        for (int slot = 0; slot < genomeOf.Length; slot++)
        {
            var byEnvironment = ByEnvironment(scores[slot]);
            float combined = FitnessAggregator.Combine(byEnvironment.Values, WorstWeight);
            lines.Add($"{combined:F3} ({byEnvironment})");
            if (combined > championScore)
            {
                champion = genomeOf[slot];
                championScore = combined;
                championScores = byEnvironment;
            }
        }

        bool improved = championScore > BestValidation;
        lastValidation = $"{championScore:F3} ({championScores})";
        Debug.Log($"Validation after generation {Generation}: {string.Join("; ", lines)}. " +
                  (improved ? $"New best (was {BestValidation:F3})." : $"Best so far is still {BestValidation:F3}."));

        if (improved)
        {
            BestValidation = championScore;
            Save(population[champion], championScore, championScores);
        }

        NextGeneration();
    }

    // One line per brain, best first: score per environment and how its stages ended. The scores are written into
    // the brain's file, so the menu shows them.
    private void EndExam()
    {
        examDone = true;
        var cards = new List<(float combined, string line)>();

        for (int slot = 0; slot < genomeOf.Length; slot++)
        {
            var entry = examBrains[genomeOf[slot]];
            var byEnvironment = ByEnvironment(scores[slot]);
            float combined = FitnessAggregator.Combine(byEnvironment.Values, WorstWeight);

            int crashes = outcomes[slot].Count(outcome => outcome == CarAgent.Outcome.Crashed);
            int stalls = outcomes[slot].Count(outcome => outcome == CarAgent.Outcome.Stalled);
            var hit = obstacles[slot].Where(name => name != null).GroupBy(name => name)
                                     .Select(group => $"{group.Key} {group.Count()}");
            var failures = ExamCriteria.Failures(byEnvironment.Values[0], byEnvironment.Values[1], byEnvironment.Values[2], crashes);
            string verdict = failures.Count == 0 ? "PASSED" : $"FAILED: {string.Join(", ", failures)}";
            string line = $"{entry.File}: {combined:F3} ({byEnvironment}); of {stages.Count} stages crashed {crashes}" +
                          (crashes > 0 ? $" ({string.Join(", ", hit)})" : "") + $", stalled {stalls}. {verdict}";
            cards.Add((combined, line));
            WriteScores(entry, byEnvironment);
        }

        foreach (var (_, line) in cards.OrderByDescending(card => card.combined))
        {
            examReport.Add(line);
            Debug.Log($"Exam: {line}");
        }
    }

    private static void WriteScores(BrainLibrary.Entry entry, EnvironmentScores environmentScores)
    {
        string path = Path.Combine(Application.persistentDataPath, entry.File);
        var brain = JsonUtility.FromJson<TrainingManager.SavedBrain>(File.ReadAllText(path));
        brain.Scores = ENVIRONMENTS.Select((kind, i) => new TrainingManager.EnvironmentScore
        {
            Name = kind.ToString(),
            Score = environmentScores.Values[i]
        }).ToArray();
        File.WriteAllText(path, JsonUtility.ToJson(brain, true));
    }

    private class EnvironmentScores
    {
        public readonly float[] Values = new float[ENVIRONMENTS.Length];

        public override string ToString() =>
            string.Join(", ", ENVIRONMENTS.Select((kind, i) => $"{kind} {Values[i]:F2}"));
    }

    private EnvironmentScores ByEnvironment(float[] stageScores)
    {
        var result = new EnvironmentScores();
        for (int e = 0; e < ENVIRONMENTS.Length; e++)
        {
            var ofKind = stages.Select((stage, i) => (stage, i)).Where(pair => pair.stage.Kind == ENVIRONMENTS[e]).ToArray();
            result.Values[e] = ofKind.Length == 0 ? 0 : ofKind.Average(pair => stageScores[pair.i]);
        }
        return result;
    }

    // The elites open the population (see GeneticAlgorithm.NextGeneration), so they are the first agents.
    private string EliteReplay(float[] fitness)
    {
        if (expectedEliteFitness.Length == 0)
            return "";

        var replays = expectedEliteFitness.Select((expected, i) => $"{expected:F3}->{fitness[i]:F3}");
        return $", elites {string.Join(" ", replays)}";
    }

    private void Save(float[] genome, float fitness, EnvironmentScores environmentScores)
    {
        var brain = new TrainingManager.SavedBrain
        {
            Seed = seed,
            Generation = Generation,
            Course = "Curriculum",
            Fitness = fitness,
            Layers = layers,
            Genome = genome,
            Scores = ENVIRONMENTS.Select((kind, i) => new TrainingManager.EnvironmentScore
            {
                Name = kind.ToString(),
                Score = environmentScores.Values[i]
            }).ToArray()
        };
        File.WriteAllText(SavePath, JsonUtility.ToJson(brain, true));
        Debug.Log($"Saved the validation champion of generation {Generation} ({environmentScores}) to {SavePath}");
    }

    private static string StageName(CourseKind kind) => kind switch
    {
        CourseKind.Ring => "anillo",
        CourseKind.RandomPoints => "puntos al azar",
        _ => "exploración"
    };

    public bool ShowHud => agents != null;
    public string HudTitle => Examining ? (examDone ? "Examen terminado" : $"Examen de {PopulationSize} cerebros")
                            : validating ? $"Validación tras la generación {Generation}" : $"Generación {Generation}";
    public bool HasSpeed => true;
    public float Speed { get => SimulationSpeed; set => SimulationSpeed = value; }

    // The HUD is narrow: the best few brains, the full report is in the log.
    public IEnumerable<string> HudLines => examDone
                                               ? examReport.Take(6).Append(examReport.Count > 6 ? $"… y {examReport.Count - 6} más en el log" : "")
                                               : StatusLines;

    private IEnumerable<string> StatusLines => new[]
    {
        $"Etapa {stageIndex + 1} / {stages.Count}: {StageName(CurrentStage.Kind)}",
        $"Vivos {agents.Count(agent => agent.Alive)} / {genomeOf.Length}",
        $"Tiempo {elapsed:F1} / {StageSeconds:F0} s",
        $"Mejor de la generación anterior {LastGenerationBest:F3}",
        $"Mejor validación {(lastValidation == "" ? "-" : $"{BestValidation:F3}")}",
        $"Cada {ValidateEvery} generaciones se validan los {ValidationCandidates} mejores"
    };
}
