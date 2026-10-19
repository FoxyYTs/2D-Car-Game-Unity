using UnityEngine;
using UnityEngine.SceneManagement;

public enum GameMode
{
    HumanVsHuman,
    HumanVsAI,
    AIVsAI,
    TrainCheckpoints,
    TrainRandomPoints,
    TrainExploration,
    TrainCurriculum,
    TrainVersus
}

// What was picked in the main menu, applied to each scene right after it loads (before any Start), so the
// components read it as if it had been set in the inspector. A scene played straight from the editor, without
// the menu, keeps the setup of its inspector.
public static class GameSession
{
    public const string MENU_SCENE = "MainMenu";
    private const string VERSUS_SCENE = "SampleScene";
    private const string TRAINING_SCENE = "TrainingScene";
    private const string VERSUS_TRAINING_SCENE = "VersusTrainingScene";

    public static bool Chosen { get; private set; }
    public static GameMode Mode { get; private set; }

    // Car ids of the versus scene: A is the green car (WASD), B the blue one (arrows).
    public static string HumanCarId = "A";
    // Files in Application.persistentDataPath; empty picks the most recent one, like AIDriver does.
    public static string BrainA = "";
    public static string BrainB = "";
    public static string OpponentBrain = "";
    public static string SeedBrainFile = "";

    public static int PopulationSize = 30;
    public static int Seed = 0;
    public static float SimulationSpeed = 1;
    public static int CircuitChangeEvery = 10;

    public static bool HasMenu => Application.CanStreamedLevelBeLoaded(MENU_SCENE);
    public static bool InMenu => SceneManager.GetActiveScene().name == MENU_SCENE;

    public static void Play(GameMode mode)
    {
        Mode = mode;
        Chosen = true;
        Time.timeScale = 1;
        SceneManager.LoadScene(SceneOf(mode));
    }

    public static void ToMenu()
    {
        Time.timeScale = 1;
        SceneManager.LoadScene(MENU_SCENE);
    }

    private static string SceneOf(GameMode mode) => mode switch
    {
        GameMode.TrainCheckpoints or GameMode.TrainRandomPoints or GameMode.TrainExploration or GameMode.TrainCurriculum => TRAINING_SCENE,
        GameMode.TrainVersus => VERSUS_TRAINING_SCENE,
        _ => VERSUS_SCENE
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Hook()
    {
        SceneManager.sceneLoaded -= Apply;
        SceneManager.sceneLoaded += Apply;
    }

    private static void Apply(Scene scene, LoadSceneMode mode)
    {
        if (!Chosen || scene.name != SceneOf(Mode))
            return;

        // A disabled AIDriver never starts: its car keeps the keyboard it gets in CarWritter.Awake.
        foreach (var driver in Object.FindObjectsByType<AIDriver>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            string id = driver.GetComponent<CarWritter>().Id;
            driver.enabled = Mode == GameMode.AIVsAI || Mode == GameMode.HumanVsAI && id != HumanCarId;
            driver.BrainFile = Mode == GameMode.HumanVsAI ? OpponentBrain : id == "A" ? BrainA : BrainB;
        }

        var training = Object.FindAnyObjectByType<TrainingManager>();
        if (training != null && Mode == GameMode.TrainCurriculum)
        {
            // Same scene, its own trainer: the TrainingManager is disabled before it starts.
            var curriculum = training.gameObject.AddComponent<CurriculumTrainingManager>();
            curriculum.AgentTemplate = training.AgentTemplate;
            curriculum.Circuit = training.Circuit;
            curriculum.PopulationSize = PopulationSize;
            curriculum.Seed = Seed;
            curriculum.SimulationSpeed = SimulationSpeed;
            curriculum.CircuitChangeEvery = Mathf.Max(1, CircuitChangeEvery);
            training.enabled = false;
        }
        else if (training != null)
        {
            training.PopulationSize = PopulationSize;
            training.Seed = Seed;
            training.SimulationSpeed = SimulationSpeed;
            training.CircuitChangeEvery = CircuitChangeEvery;
            training.Course = Mode switch
            {
                GameMode.TrainRandomPoints => TrainingManager.CourseKind.RandomPoints,
                GameMode.TrainExploration => TrainingManager.CourseKind.Exploration,
                _ => TrainingManager.CourseKind.Ring
            };
        }

        var versus = Object.FindAnyObjectByType<VersusTrainingManager>();
        if (versus != null)
        {
            versus.PopulationSize = PopulationSize;
            versus.Seed = Seed;
            versus.SimulationSpeed = SimulationSpeed;
            versus.SeedBrainFile = SeedBrainFile;
        }
    }
}
