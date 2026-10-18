using Assets.Logic.Game;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

// While every car is driven by the AI, matches restart on their own and a scoreboard counts the results.
// If a human takes a car (its AIDriver disabled), the game goes back to its usual rules.
public class VersusExhibition : MonoBehaviour
{
    private const float RESTART_DELAY_SECONDS = 3;
    [Tooltip("Matches where nobody crashes end in a draw after this long.")]
    public float MatchTimeLimitSeconds = 60;

    // Static, so the scoreboard survives the scene reload between matches.
    private static readonly Dictionary<string, int> wins = new();
    private static readonly Dictionary<string, int> crashesInto = new();
    private static int rams;
    private static int ties;
    private static int timeouts;
    private static int matches;

    private float matchTime;
    private float restartIn = -1;
    private string lastResult;

    void Start()
    {
        // GameGoal is static: the previous match's result would otherwise look like this match already ended.
        GameGoal.ResetResult();

        foreach (var car in FindObjectsByType<CarWritter>(FindObjectsSortMode.None))
            wins.TryAdd(car.Name, 0);
    }

    void Update()
    {
        if (restartIn >= 0)
        {
            restartIn -= Time.unscaledDeltaTime;
            if (restartIn < 0)
                NextMatch();
            return;
        }

        if (!AllCarsDrivenByAI())
            return;

        if (GameGoal.Winner != null)
            EndMatch(GameGoal.WinnerName is null ? Tie() : Win(GameGoal.WinnerName));
        else if ((matchTime += Time.deltaTime) >= MatchTimeLimitSeconds)
        {
            Time.timeScale = 0;
            EndMatch(Timeout());
        }
    }

    private static bool AllCarsDrivenByAI() =>
        FindObjectsByType<CarWritter>(FindObjectsSortMode.None)
            .All(car => car.TryGetComponent(out AIDriver driver) && driver.enabled && driver.Driving);

    // The game counts a win the same whether the winner rammed the opponent or the opponent crashed into an obstacle.
    private static string Win(string winner)
    {
        wins[winner] = wins.GetValueOrDefault(winner) + 1;

        var loser = FindObjectsByType<CarWritter>(FindObjectsSortMode.None).First(car => car.Name != winner);
        var collisions = loser.GetComponent<CarCollisionReader>().Collisions;
        if (collisions.OntoStatic)
        {
            crashesInto[collisions.Obstacle] = crashesInto.GetValueOrDefault(collisions.Obstacle) + 1;
            return $"{winner} won, {loser.Name} hit {collisions.Obstacle}";
        }

        rams++;
        return $"{winner} won, it rammed {loser.Name}";
    }

    private static string Tie()
    {
        ties++;
        return "head-on tie";
    }

    private string Timeout()
    {
        timeouts++;
        return $"no crash in {MatchTimeLimitSeconds:F0} s";
    }

    private void EndMatch(string result)
    {
        matches++;
        lastResult = result;
        restartIn = RESTART_DELAY_SECONDS;
        string crashes = string.Join(", ", crashesInto.OrderByDescending(pair => pair.Value).Select(pair => $"{pair.Key} {pair.Value}"));
        Debug.Log($"Match {matches}: {result}. {Scoreboard()}; crashes into: {(crashes == "" ? "none" : crashes)}");
    }

    private static void NextMatch()
    {
        Time.timeScale = 1;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    private static string Scoreboard()
    {
        var cars = string.Join("  ", wins.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key} {pair.Value}"));
        int crashes = crashesInto.Values.Sum();
        return $"{cars} | rammed {rams}  crashed {crashes}  ties {ties}  time limit {timeouts}";
    }

    void OnGUI()
    {
        if (restartIn < 0 && !AllCarsDrivenByAI())
            return;

        var area = new Rect(Screen.width / 2f - 210, 10, 420, restartIn >= 0 ? 75 : 50);
        GUILayout.BeginArea(area, GUI.skin.box);
        GUILayout.Label($"AI vs AI — match {matches + (restartIn >= 0 ? 0 : 1)}");
        GUILayout.Label(Scoreboard());
        if (restartIn >= 0)
            GUILayout.Label($"Last: {lastResult}. Next match in {restartIn:F1} s");
        GUILayout.EndArea();
    }
}
