using UnityEngine;

// Reach the checkpoints of a circuit in order; the fitness is the checkpoints reached plus how close the agent
// got to the next one (0 to 1).
public class CheckpointTask : ITrainingTask
{
    private readonly CheckpointCircuit circuit;
    private Vector2 start;
    private bool reachedSinceLastStep;

    public int Reached { get; private set; }
    public int NextIndex => Reached % circuit.Count;
    public string Status => $"heading to checkpoint {NextIndex}";

    public CheckpointTask(CheckpointCircuit circuit)
    {
        this.circuit = circuit;
    }

    public void Begin(Vector2 start)
    {
        this.start = start;
        Reached = 0;
        reachedSinceLastStep = false;
    }

    public Vector2 Target(Vector2 position) => circuit[Reached].transform.position;

    // Checkpoints are triggers: they report the car between steps.
    public void Reach(Checkpoint checkpoint)
    {
        if (checkpoint != circuit[Reached])
            return;

        Reached++;
        reachedSinceLastStep = true;
    }

    public bool Advance(Vector2 position)
    {
        bool progress = reachedSinceLastStep;
        reachedSinceLastStep = false;
        return progress;
    }

    public float Fitness(Vector2 position) => Reached + Progress(position);

    private float Progress(Vector2 position)
    {
        Vector2 target = circuit[Reached].transform.position;
        Vector2 from = Reached == 0 ? start : (Vector2)circuit[Reached - 1].transform.position;

        float span = Vector2.Distance(from, target);
        if (span <= 0)
            return 0;

        return Mathf.Clamp01(1 - Vector2.Distance(position, target) / span);
    }
}
