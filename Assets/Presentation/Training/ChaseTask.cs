using UnityEngine;

// Catch a runner that drives along random checkpoints. Ramming it (front bumper on its body) is worth 2, any other
// contact 1; otherwise up to 1 for how close the chaser got, against the starting distance.
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

    private float startDistance;
    private float minDistance;

    public string Status => Result switch
    {
        Ending.Rammed => "rammed the runner",
        Ending.Touched => "touched the runner",
        Ending.RunnerOut => "runner out of the chase",
        _ => $"{minDistance:F1} m from the runner at best"
    };

    public ChaseTask(CarAgent runner)
    {
        Runner = runner;
    }

    // The runner must already be at its start.
    public void Begin(Vector2 start)
    {
        startDistance = minDistance = Vector2.Distance(start, Runner.transform.position);
        Result = Ending.None;
    }

    public Vector2 Target(Vector2 position) => Runner.transform.position;

    // Getting closer than ever is progress.
    public bool Advance(Vector2 position)
    {
        float distance = Vector2.Distance(position, Runner.transform.position);
        if (distance >= minDistance)
            return false;

        minDistance = distance;
        return true;
    }

    public void End(Ending ending)
    {
        Result = ending;
    }

    public float Fitness(Vector2 position) => Result switch
    {
        Ending.Rammed => RAM_SCORE,
        Ending.Touched => TOUCH_SCORE,
        _ => startDistance > 0 ? Mathf.Clamp01(1 - minDistance / startDistance) : 0
    };
}
