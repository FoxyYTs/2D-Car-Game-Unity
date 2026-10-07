using Assets.Logic.AI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

// Evolves brains for the versus game by self-play. Each generation every agent plays one match from each side
// against different opponents; all matches run at once on the same arena. Agents of different matches ignore
// each other: contact inside a match is detected geometrically, with the game's bumper-versus-body rules.
// Every BenchmarkEvery generations the champion plays a fixed reference brain, the objective measure of progress.
public class VersusTrainingManager : MonoBehaviour
{
    public enum SelectionMethod
    {
        Tournament,
        Roulette
    }

    private enum Ending
    {
        Ram,
        HeadOn,
        Crash,
        DoubleCrash,
        TimeLimit
    }

    private class Match
    {
        public DuelAgent Left;
        public DuelAgent Right;
        public bool Over;
    }

    public const string BRAIN_FILE_PREFIX = "versus-seed";
    private const int ROUNDS = 2;
    // Colliders closer than this count as touching, like the physics contact offset.
    private const float CONTACT_DISTANCE = 0.02f;

    [Header("Scene")]
    [Tooltip("Inactive car that every agent is cloned from.")]
    public DuelAgent AgentTemplate;
    public Transform LeftStart;
    public Transform RightStart;

    [Header("Population")]
    [Tooltip("Must be even: agents play in pairs.")]
    [Min(2)] public int PopulationSize = 30;
    [Tooltip("Start from copies of a checkpoint-trained brain instead of random weights.")]
    public bool StartFromBrain = true;
    [Tooltip("File in Application.persistentDataPath. Empty: the most recent brain-seed*.json. " +
             "It is also the fixed reference opponent of the benchmark.")]
    public string SeedBrainFile = "";
    [Tooltip("Hidden neurons when starting from random weights.")]
    [Min(1)] public int HiddenNeurons = 8;
    [Tooltip("0 picks a different seed on every run; the one used is logged, so a run can be repeated.")]
    public int Seed = 0;

    [Header("Evolution")]
    public SelectionMethod Selection = SelectionMethod.Tournament;
    [Min(1)] public int TournamentSize = 3;
    [Min(0)] public int EliteCount = 2;
    [Range(0, 1)] public float MutationRate = 0.1f;
    [Min(0)] public float MutationSigma = 0.3f;

    [Header("Matches")]
    [Min(1)] public float RoundSeconds = 30;
    [Min(0)] public float StartJitter = 1;
    [Range(0, 45)] public float HeadingJitter = 10;

    [Header("Scoring")]
    [Tooltip("The game counts a win the same however it happens; training rewards ramming far more than " +
             "waiting for the opponent to crash, or agents would learn to stay still.")]
    public float RamWin = 10;
    public float HeadOnTie = 4;
    public float OpponentCrashWin = 2;
    [Tooltip("Up to this much for getting close to the opponent (minimum distance against the starting one).")]
    public float ApproachBonus = 2;
    [Tooltip("Up to this much for not crashing into an obstacle.")]
    public float SurvivalBonus = 1;

    [Header("Benchmark")]
    [Min(1)] public int BenchmarkEvery = 10;
    [Min(1)] public int BenchmarkMatches = 10;

    [Range(1, 10)] public float SimulationSpeed = 1;

    public int Generation { get; private set; }

    private DuelAgent[] agents;
    private GeneticAlgorithm genetics;
    private System.Random random;
    private int seed;
    private float[][] population;
    private int[] layers;
    private float[] referenceGenome;

    private readonly List<Match> matches = new();
    private readonly Dictionary<DuelAgent, int> genomeOf = new();
    private float[] scoreSum;
    private float[] pendingFitness;
    private int round;
    private bool benchmarking;
    private float elapsed;

    private readonly Dictionary<Ending, int> endings = new();
    private readonly Dictionary<string, int> crashesInto = new();
    private readonly Dictionary<string, int> benchmark = new();
    private string lastGeneration = "";
    private string lastBenchmark = "";

    private string SavePath => Path.Combine(Application.persistentDataPath, $"{BRAIN_FILE_PREFIX}{seed}.json");

    void Start()
    {
        if (AgentTemplate == null || LeftStart == null || RightStart == null)
        {
            Debug.LogError("VersusTrainingManager needs an agent template and both starting points.");
            enabled = false;
            return;
        }
        if (PopulationSize % 2 != 0)
            PopulationSize++;

        AgentTemplate.gameObject.SetActive(false);

        seed = Seed != 0 ? Seed : Math.Max(1, Environment.TickCount & int.MaxValue);
        random = new System.Random(seed);
        Debug.Log($"Versus training seed {seed} (set it as Seed to repeat this run); brains are saved to {SavePath}");

        var seedBrain = LoadSeedBrain();
        layers = seedBrain?.Layers ?? new[] { AgentTemplate.InputCount, HiddenNeurons, CarPilot.OUTPUTS };
        referenceGenome = seedBrain?.Genome;

        ISelection selection = Selection == SelectionMethod.Roulette
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
        population = StartFromBrain && seedBrain != null
                         ? MutatedCopies(seedBrain.Genome)
                         : genetics.CreatePopulation(NeuralNetwork.CountParameters(layers));

        Generation = 1;
        StartRound(0);
    }

    void Update()
    {
        Time.timeScale = SimulationSpeed;
    }

    void OnDisable()
    {
        Time.timeScale = 1;
    }

    // Like the checkpoint trainer, the manager alone advances the simulation, so runs are repeatable.
    void FixedUpdate()
    {
        float deltaTime = Time.fixedDeltaTime;
        foreach (var agent in agents)
            agent.Tick(deltaTime);
        elapsed += deltaTime;

        // Contacts are checked on the poses just set, not on the ones of the last physics step.
        Physics2D.SyncTransforms();

        foreach (var match in matches.Where(match => !match.Over))
        {
            ResolveCrashes(match);
            if (!match.Over)
                ResolveContact(match);
            if (!match.Over)
            {
                match.Left.TrackDistance();
                match.Right.TrackDistance();
            }
        }

        if (elapsed >= RoundSeconds)
            foreach (var match in matches.Where(match => !match.Over))
                End(match, Ending.TimeLimit, winner: null);

        if (matches.All(match => match.Over))
            EndRound();
    }

    private DuelAgent SpawnAgent(int index)
    {
        var agent = Instantiate(AgentTemplate, AgentTemplate.transform.parent);
        agent.name = $"Agent {index}";
        agent.gameObject.SetActive(true);
        agent.Setup(new NeuralNetwork(layers));
        return agent;
    }

    private TrainingManager.SavedBrain LoadSeedBrain()
    {
        string path = string.IsNullOrEmpty(SeedBrainFile)
                          ? new DirectoryInfo(Application.persistentDataPath)
                                .GetFiles($"{TrainingManager.BRAIN_FILE_PREFIX}*.json")
                                .OrderByDescending(file => file.LastWriteTimeUtc)
                                .FirstOrDefault()?.FullName
                          : Path.Combine(Application.persistentDataPath, SeedBrainFile);

        if (path is null || !File.Exists(path))
        {
            Debug.LogWarning("No seed brain found: training starts from random weights and there is no benchmark.");
            return null;
        }

        var brain = JsonUtility.FromJson<TrainingManager.SavedBrain>(File.ReadAllText(path));
        if (brain.Layers is null || brain.Layers.Length < 2 || brain.Layers[0] != AgentTemplate.InputCount ||
            brain.Layers[brain.Layers.Length - 1] != CarPilot.OUTPUTS)
        {
            Debug.LogWarning($"{Path.GetFileName(path)} does not fit these cars: training starts from random weights.");
            return null;
        }

        Debug.Log($"Seed and benchmark brain: {Path.GetFileName(path)} (generation {brain.Generation}, fitness {brain.Fitness:F2})");
        return brain;
    }

    // The seed brain itself plus mutated copies of it.
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

    private void StartRound(int number)
    {
        // The second round's pairs come from the first round's matches: compute them before clearing.
        var pairs = number == 0 ? FirstRoundPairs() : SecondRoundPairs();

        round = number;
        benchmarking = false;
        elapsed = 0;
        matches.Clear();
        genomeOf.Clear();
        if (round == 0)
            scoreSum = new float[PopulationSize];

        foreach (var (left, right) in pairs)
            Play(agents[left], population[left], agents[right], population[right]);
    }

    // Random pairs; the first of each pair starts on the left.
    private List<(int left, int right)> FirstRoundPairs()
    {
        var order = Shuffled(Enumerable.Range(0, PopulationSize).ToList());
        return Enumerable.Range(0, PopulationSize / 2).Select(i => (order[2 * i], order[2 * i + 1])).ToList();
    }

    // Everyone switches side and, when possible, opponent.
    private List<(int left, int right)> SecondRoundPairs()
    {
        var previous = matches.ToList();
        var nowLeft = Shuffled(previous.Select(match => genomeOf[match.Right]).ToList());
        var nowRight = Shuffled(previous.Select(match => genomeOf[match.Left]).ToList());
        var previousOpponent = previous.ToDictionary(match => genomeOf[match.Right], match => genomeOf[match.Left]);

        for (int i = 0; i < nowLeft.Count && nowLeft.Count > 1; i++)
            if (previousOpponent[nowLeft[i]] == nowRight[i])
                (nowRight[i], nowRight[(i + 1) % nowRight.Count]) = (nowRight[(i + 1) % nowRight.Count], nowRight[i]);

        return nowLeft.Zip(nowRight, (left, right) => (left, right)).ToList();
    }

    private void Play(DuelAgent left, float[] leftGenome, DuelAgent right, float[] rightGenome)
    {
        left.Begin(leftGenome, StartPosition(LeftStart), StartRotation(LeftStart), right);
        right.Begin(rightGenome, StartPosition(RightStart), StartRotation(RightStart), left);
        left.MeasureStartDistance();
        right.MeasureStartDistance();

        genomeOf[left] = Array.IndexOf(agents, left);
        genomeOf[right] = Array.IndexOf(agents, right);
        matches.Add(new Match { Left = left, Right = right });
    }

    private Vector3 StartPosition(Transform start)
    {
        float angle = (float)(random.NextDouble() * 2 * Math.PI);
        float distance = (float)Math.Sqrt(random.NextDouble()) * StartJitter;
        return start.position + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
    }

    private Quaternion StartRotation(Transform start)
    {
        float heading = (float)(random.NextDouble() * 2 - 1) * HeadingJitter;
        return start.rotation * Quaternion.Euler(0, 0, heading);
    }

    private List<int> Shuffled(List<int> items)
    {
        for (int i = items.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
        return items;
    }

    private void ResolveCrashes(Match match)
    {
        bool left = match.Left.Crashed, right = match.Right.Crashed;
        if (left && right)
            End(match, Ending.DoubleCrash, winner: null);
        else if (left)
            End(match, Ending.Crash, winner: match.Right);
        else if (right)
            End(match, Ending.Crash, winner: match.Left);
    }

    // The game's rules: my bumper on your body, I rammed you; bumper on bumper or body on body, a tie.
    // Any other contact in the same step also makes it a tie, as both cars would get both flags.
    private void ResolveContact(Match match)
    {
        DuelAgent a = match.Left, b = match.Right;
        bool aRamsB = Touching(a.Bumper, b.Body);
        bool bRamsA = Touching(b.Bumper, a.Body);
        bool bumpers = Touching(a.Bumper, b.Bumper);
        bool bodies = Touching(a.Body, b.Body);

        if (!(aRamsB || bRamsA || bumpers || bodies))
            return;

        if (aRamsB && !bRamsA && !bumpers && !bodies)
            End(match, Ending.Ram, winner: a);
        else if (bRamsA && !aRamsB && !bumpers && !bodies)
            End(match, Ending.Ram, winner: b);
        else
            End(match, Ending.HeadOn, winner: null);
    }

    private static bool Touching(Collider2D first, Collider2D second)
    {
        var distance = first.Distance(second);
        return distance.isValid && distance.distance <= CONTACT_DISTANCE;
    }

    private void End(Match match, Ending ending, DuelAgent winner)
    {
        match.Over = true;
        endings[ending] = endings.GetValueOrDefault(ending) + 1;
        foreach (var crashed in new[] { match.Left, match.Right }.Where(agent => agent.Crashed))
            crashesInto[crashed.CrashedInto] = crashesInto.GetValueOrDefault(crashed.CrashedInto) + 1;

        float winnerResult = ending switch
        {
            Ending.Ram => RamWin,
            Ending.Crash => OpponentCrashWin,
            _ => 0
        };
        float tieResult = ending == Ending.HeadOn ? HeadOnTie : 0;

        foreach (var agent in new[] { match.Left, match.Right })
        {
            bool won = agent == winner;
            bool lost = winner != null && !won || agent.Crashed;
            float result = winner is null ? tieResult : won ? winnerResult : 0;

            if (benchmarking)
                RecordBenchmark(agent, ending, won, lost);
            else
                scoreSum[genomeOf[agent]] += Score(agent, result);

            if (agent.Playing)
                agent.Stop(lost);
        }
    }

    private float Score(DuelAgent agent, float result)
    {
        float approach = agent.StartDistance > 0
                             ? ApproachBonus * Mathf.Clamp01(1 - agent.MinDistance / agent.StartDistance)
                             : 0;
        float survival = agent.Crashed ? SurvivalBonus * agent.AliveTime / RoundSeconds : SurvivalBonus;
        return result + approach + survival;
    }

    private void EndRound()
    {
        if (benchmarking)
        {
            EndBenchmark();
            NextGeneration();
        }
        else if (round + 1 < ROUNDS)
            StartRound(round + 1);
        else
            EndGeneration();
    }

    private void EndGeneration()
    {
        var fitness = scoreSum.Select(sum => sum / ROUNDS).ToArray();
        int champion = Array.IndexOf(fitness, fitness.Max());

        lastGeneration = $"best {fitness[champion]:F2}, average {fitness.Average():F2}";
        Debug.Log($"Generation {Generation}: {lastGeneration}; {Endings()}");
        endings.Clear();
        crashesInto.Clear();

        pendingFitness = fitness;
        if (Generation % BenchmarkEvery == 0 && referenceGenome != null)
            StartBenchmark(population[champion], fitness[champion]);
        else
            NextGeneration();
    }

    private void NextGeneration()
    {
        population = genetics.NextGeneration(population, pendingFitness);
        Generation++;
        StartRound(0);
    }

    private string Endings()
    {
        string crashes = string.Join(", ", crashesInto.OrderByDescending(pair => pair.Value).Select(pair => $"{pair.Key} {pair.Value}"));
        return $"rammed {endings.GetValueOrDefault(Ending.Ram)}, head-on {endings.GetValueOrDefault(Ending.HeadOn)}, " +
               $"crash {endings.GetValueOrDefault(Ending.Crash)}, double crash {endings.GetValueOrDefault(Ending.DoubleCrash)}, " +
               $"time limit {endings.GetValueOrDefault(Ending.TimeLimit)}; crashes into: {(crashes == "" ? "none" : crashes)}";
    }

    // The champion plays the fixed reference brain, half of the matches from each side.
    private float[] championGenome;
    private float championFitness;

    private void StartBenchmark(float[] champion, float fitness)
    {
        benchmarking = true;
        elapsed = 0;
        matches.Clear();
        genomeOf.Clear();
        benchmark.Clear();
        championGenome = champion;
        championFitness = fitness;

        int count = Math.Min(BenchmarkMatches, PopulationSize / 2);
        for (int i = 0; i < count; i++)
        {
            DuelAgent championAgent = agents[i], referenceAgent = agents[count + i];
            if (i % 2 == 0)
                Play(championAgent, champion, referenceAgent, referenceGenome);
            else
                Play(referenceAgent, referenceGenome, championAgent, champion);
        }

        for (int i = 2 * count; i < agents.Length; i++)
            agents[i].Bench();
    }

    // Only the champion's side is recorded: agents below the match count play the champion.
    private void RecordBenchmark(DuelAgent agent, Ending ending, bool won, bool lost)
    {
        int count = Math.Min(BenchmarkMatches, PopulationSize / 2);
        if (Array.IndexOf(agents, agent) >= count)
            return;

        string key = won ? (ending == Ending.Ram ? "won ramming" : "won, reference crashed")
                   : lost ? (agent.Crashed ? "lost, crashed" : "lost, rammed")
                   : ending == Ending.TimeLimit ? "time limit" : "tie";
        benchmark[key] = benchmark.GetValueOrDefault(key) + 1;
    }

    private void EndBenchmark()
    {
        lastBenchmark = string.Join(", ", benchmark.OrderByDescending(pair => pair.Value).Select(pair => $"{pair.Key} {pair.Value}"));
        Debug.Log($"Benchmark after generation {Generation}, champion vs reference: {lastBenchmark}");
        endings.Clear();
        crashesInto.Clear();
        Save(championGenome, championFitness);
    }

    private void Save(float[] genome, float fitness)
    {
        var brain = new TrainingManager.SavedBrain
        {
            Seed = seed,
            Generation = Generation,
            Fitness = fitness,
            Layers = layers,
            Genome = genome
        };
        File.WriteAllText(SavePath, JsonUtility.ToJson(brain, true));
        Debug.Log($"Saved the champion of generation {Generation} (fitness {fitness:F2}) to {SavePath}");
    }

    void OnGUI()
    {
        if (agents is null)
            return;

        GUILayout.BeginArea(new Rect(10, 10, 300, 175), GUI.skin.box);
        GUILayout.Label($"Generation {Generation}, {(benchmarking ? "benchmark vs reference" : $"round {round + 1} / {ROUNDS}")}");
        GUILayout.Label($"Matches over {matches.Count(match => match.Over)} / {matches.Count}");
        GUILayout.Label($"Time {elapsed:F1} / {RoundSeconds:F0} s");
        GUILayout.Label($"Last generation: {lastGeneration}");
        GUILayout.Label($"Last benchmark: {(lastBenchmark == "" ? "-" : lastBenchmark)}");
        GUILayout.Label($"Speed x{SimulationSpeed:F1}");
        SimulationSpeed = GUILayout.HorizontalSlider(SimulationSpeed, 1, 10);
        GUILayout.EndArea();
    }
}
