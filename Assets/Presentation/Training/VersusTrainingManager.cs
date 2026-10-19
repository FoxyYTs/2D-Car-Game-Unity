using Assets.Logic.AI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

// Evolves brains for the versus game by self-play. Each generation every agent plays one match from each side
// against different opponents; all matches run at once on the same arena. Agents of different matches ignore
// each other: contact inside a match is detected geometrically, with the game's bumper-versus-body rules.
// Every BenchmarkEvery generations the champion plays the champion of the previous benchmark (the first time, the
// seed brain): whether it beats, and above all rams, its predecessor is the objective measure of progress.
public class VersusTrainingManager : MonoBehaviour, IHudSource, IFitnessHistory
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
    // Every genome plays two matches per generation: one against a peer, the other against the hall of fame (or,
    // while it is empty, against another peer from the other side).
    private const int MATCHES_PER_GENOME = 2;

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
             "It is also the opponent of the first benchmark.")]
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
    [Tooltip("Taken off the score of a car that crashes into an obstacle (scores do not go below 0). Without it most " +
             "matches ended with a car hitting the mountain or an edge.")]
    [Min(0)] public float CrashPenalty = 3;

    [Header("Hall of fame")]
    [Tooltip("Past benchmark champions (and the seed brain) kept as opponents: playing only against the current " +
             "population, agents can forget how to beat strategies that are no longer around.")]
    [Min(1)] public int HallOfFameSize = 10;

    [Header("Benchmark")]
    [Min(1)] public int BenchmarkEvery = 10;
    [Min(1)] public int BenchmarkMatches = 10;

    [Range(1, 10)] public float SimulationSpeed = 1;

    public int Generation { get; private set; }
    // Best and average fitness of every finished generation, for the fitness chart.
    public List<Vector2> History { get; } = new();
    public List<int> CircuitStarts { get; } = new();

    private DuelAgent[] agents;
    private GeneticAlgorithm genetics;
    private System.Random random;
    private int seed;
    private float[][] population;
    private int[] layers;
    // Opponent of the next benchmark: the previous benchmark's champion. A fixed reference stopped measuring anything
    // once agents learned to dodge the mountain: it kept crashing into it and lost every match without being rammed.
    private float[] referenceGenome;
    private readonly List<float[]> hallOfFame = new();
    // Genomes that started on the left in the first round: against the hall of fame they take the other side.
    private readonly HashSet<int> startedLeft = new();
    private string referenceName;

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
        if (seedBrain != null)
            hallOfFame.Add(seedBrain.Genome);
        referenceName = "the seed brain";

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
        if (!PauseMenu.Paused)
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

    // Kinds of brains to start from, best first: chasing is the closest to dueling.
    private static readonly string[] SEED_PREFERENCE =
        { nameof(TrainingManager.CourseKind.Chase), "Curriculum", nameof(TrainingManager.CourseKind.RandomPoints) };

    // SeedBrainFile, or else the most recent brain of the most preferred kind there is; any checkpoint-trained
    // brain if none. The most recent file of any kind would start from whatever was trained last, even exploration.
    private TrainingManager.SavedBrain LoadSeedBrain()
    {
        var fitting = BrainLibrary.List()
                                  .Where(entry => !entry.IsVersus && entry.Brain.Layers[0] == AgentTemplate.InputCount &&
                                                  entry.Brain.Layers[entry.Brain.Layers.Length - 1] == CarPilot.OUTPUTS)
                                  .ToList();
        var chosen = !string.IsNullOrEmpty(SeedBrainFile)
                         ? fitting.FirstOrDefault(entry => entry.File == SeedBrainFile)
                         : fitting.OrderBy(entry => Preference(entry.Brain.Course)).FirstOrDefault();

        if (chosen is null)
        {
            Debug.LogWarning(string.IsNullOrEmpty(SeedBrainFile)
                                 ? "No seed brain found: training starts from random weights and there is no benchmark."
                                 : $"{SeedBrainFile} is missing or does not fit these cars: training starts from random weights.");
            return null;
        }

        Debug.Log($"Seed and benchmark brain: {chosen.File} ({chosen.Brain.Course}, generation {chosen.Brain.Generation}, " +
                  $"fitness {chosen.Brain.Fitness:F2})");
        return chosen.Brain;
    }

    // List() is newest first and OrderBy is stable, so the newest of each kind comes first.
    private static int Preference(string course)
    {
        int index = Array.IndexOf(SEED_PREFERENCE, course);
        return index < 0 ? SEED_PREFERENCE.Length : index;
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
        var pairs = number == 0 ? FirstRoundPairs() : hallOfFame.Count == 0 ? SecondRoundPairs() : null;

        round = number;
        benchmarking = false;
        elapsed = 0;
        matches.Clear();
        genomeOf.Clear();
        if (round == 0)
        {
            scoreSum = new float[PopulationSize];
            startedLeft.Clear();
            startedLeft.UnionWith(pairs.Select(pair => pair.left));
        }

        if (pairs is null)
            PlayHallOfFame(half: round - 1);
        else
            foreach (var (left, right) in pairs)
                Play(agents[left], population[left], agents[right], population[right]);
    }

    private int Rounds => hallOfFame.Count > 0 ? 3 : 2;

    // Half of the population against random members of the hall of fame, driven by the other half's cars.
    private void PlayHallOfFame(int half)
    {
        int count = PopulationSize / 2;
        for (int k = 0; k < count; k++)
        {
            int genome = half * count + k;
            DuelAgent own = agents[genome], rival = agents[(1 - half) * count + k];
            var opponent = hallOfFame[random.Next(hallOfFame.Count)];

            if (startedLeft.Contains(genome))
                Play(rival, opponent, own, population[genome]);
            else
                Play(own, population[genome], rival, opponent);
            // Not a genome of the population: its result is not scored.
            genomeOf[rival] = -1;
        }
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

    // Any contact other than a clean ram in the same step is a tie, as both cars would get both flags.
    private void ResolveContact(Match match)
    {
        switch (ContactRules.Resolve(match.Left, match.Right))
        {
            case ContactRules.Contact.FirstRams:
                End(match, Ending.Ram, winner: match.Left);
                break;
            case ContactRules.Contact.SecondRams:
                End(match, Ending.Ram, winner: match.Right);
                break;
            case ContactRules.Contact.Tie:
                End(match, Ending.HeadOn, winner: null);
                break;
        }
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
            else if (genomeOf[agent] >= 0)
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
        float penalty = agent.Crashed ? CrashPenalty : 0;
        return Mathf.Max(0, result + approach + survival - penalty);
    }

    private void EndRound()
    {
        if (benchmarking)
        {
            EndBenchmark();
            NextGeneration();
        }
        else if (round + 1 < Rounds)
            StartRound(round + 1);
        else
            EndGeneration();
    }

    private void EndGeneration()
    {
        var fitness = scoreSum.Select(sum => sum / MATCHES_PER_GENOME).ToArray();
        int champion = Array.IndexOf(fitness, fitness.Max());
        History.Add(new Vector2(fitness[champion], fitness.Average()));

        lastGeneration = $"best {fitness[champion]:F2}, average {fitness.Average():F2}";
        Debug.Log($"Generation {Generation}: {lastGeneration}; {Endings()}");
        endings.Clear();
        crashesInto.Clear();

        pendingFitness = fitness;
        if (Generation % BenchmarkEvery != 0)
            NextGeneration();
        else if (referenceGenome != null)
            StartBenchmark(population[champion], fitness[champion]);
        else
        {
            // Started from random weights: nobody to play yet, this champion becomes the first opponent.
            Save(population[champion], fitness[champion]);
            BecomeReference(population[champion]);
            NextGeneration();
        }
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

    // Rams first: beating an opponent that crashes on its own says little about fighting.
    private static readonly string[] BENCHMARK_ORDER =
        { "won ramming", "tie", "won, reference crashed", "time limit", "lost, rammed", "lost, crashed" };

    private void EndBenchmark()
    {
        lastBenchmark = string.Join(", ", BENCHMARK_ORDER.Select(key => $"{key} {benchmark.GetValueOrDefault(key)}"));
        endings.Clear();
        crashesInto.Clear();

        // Only a champion that does not lose to the reference replaces it: saving every champion let a worse one
        // (it crashed in 5 of 10 matches) overwrite the brain and join the hall of fame.
        int won = benchmark.GetValueOrDefault("won ramming") + benchmark.GetValueOrDefault("won, reference crashed");
        int lost = benchmark.GetValueOrDefault("lost, rammed") + benchmark.GetValueOrDefault("lost, crashed");
        bool better = won >= lost;
        Debug.Log($"Benchmark after generation {Generation}, champion vs {referenceName}: {lastBenchmark}. " +
                  (better ? "It becomes the new reference." : "The reference stays."));
        if (!better)
            return;

        Save(championGenome, championFitness);
        BecomeReference(championGenome);
    }

    private void BecomeReference(float[] genome)
    {
        referenceGenome = genome.ToArray();
        hallOfFame.Add(referenceGenome);
        if (hallOfFame.Count > HallOfFameSize)
            hallOfFame.RemoveAt(0);
        referenceName = $"the champion of generation {Generation}";
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

    public bool ShowHud => agents != null;
    public string HudTitle => $"Generación {Generation} · {(benchmarking ? "prueba contra la referencia" : $"ronda {round + 1} / {Rounds}{(round > 0 && hallOfFame.Count > 0 ? " contra el Hall of Fame" : "")}")}";
    public bool HasSpeed => true;
    public float Speed { get => SimulationSpeed; set => SimulationSpeed = value; }

    public IEnumerable<string> HudLines => new[]
    {
        $"Partidas terminadas {matches.Count(match => match.Over)} / {matches.Count}",
        $"Tiempo {elapsed:F1} / {RoundSeconds:F0} s",
        $"Generación anterior: {(lastGeneration == "" ? "-" : lastGeneration)}",
        $"Última prueba: {(lastBenchmark == "" ? "-" : lastBenchmark)}"
    };
}
