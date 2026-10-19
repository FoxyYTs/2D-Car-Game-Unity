using UnityEngine;

// Catch a runner that drives along random checkpoints. Ramming it (front bumper on its body) is worth 2 and any
// other contact 1, plus up to 1 more the sooner it happens; otherwise up to 1 for how close the chaser got,
// against the starting distance. Without the time bonus every chaser that rammed scored the same, so the population
// stopped improving from the first generation.
public class ChaseTask : ITrainingTask
{
    public enum Ending
    {
        None,
        Rammed,
        Touched,
        RunnerOut
    }

    private const float RAM_SCORE = 2;
    private const float TOUCH_SCORE = 1;

    public CarAgent Runner { get; }
    public Ending Result { get; private set; }

    private readonly float stepSeconds;
    private readonly float timeLimit;
    private float startDistance;
    private float minDistance;
    private float time;
    private float endTime;

    public string Status => Result switch
    {
        Ending.Rammed => "rammed the runner",
        Ending.Touched => "touched the runner",
        Ending.RunnerOut => "runner out of the chase",
        _ => $"{minDistance:F1} m from the runner at best"
    };

    // stepSeconds: simulated time of each Advance; timeLimit: length of the chase, for the time bonus.
    public ChaseTask(CarAgent runner, float stepSeconds, float timeLimit)
    {
        Runner = runner;
        this.stepSeconds = stepSeconds;
        this.timeLimit = timeLimit;
    }

    // The runner must already be at its start.
    public void Begin(Vector2 start)
    {
        startDistance = minDistance = Vector2.Distance(start, Runner.transform.position);
        Result = Ending.None;
        time = 0;
    }

    public Vector2 Target(Vector2 position) => Runner.transform.position;

    // Getting closer than ever is progress.
    public bool Advance(Vector2 position)
    {
        time += stepSeconds;
        float distance = Vector2.Distance(position, Runner.transform.position);
        if (distance >= minDistance)
            return false;

        minDistance = distance;
        return true;
    }

    public void End(Ending ending)
    {
        Result = ending;
        endTime = time;
    }

    private float TimeBonus => Mathf.Clamp01(1 - endTime / timeLimit);

    public float Fitness(Vector2 position) => Result switch
    {
        Ending.Rammed => RAM_SCORE + TimeBonus,
        Ending.Touched => TOUCH_SCORE + TimeBonus,
        _ => startDistance > 0 ? Mathf.Clamp01(1 - minDistance / startDistance) : 0
    };
}
