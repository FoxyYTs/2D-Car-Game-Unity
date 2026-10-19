using Assets.Logic.AI;
using EngineAbstractor;
using System.Collections.Generic;
using UnityEngine;

// Drives a car with a neural network and scores it on its ITrainingTask.
[RequireComponent(typeof(CarWritter), typeof(CarSensors), typeof(CarCollisionReader))]
public class CarAgent : MonoBehaviour, IInputSource, IPilotedCar
{
    public enum Outcome
    {
        Running,
        Crashed,
        Stalled,
        // Its task ended before the time limit, e.g. it caught the runner it was chasing.
        Finished
    }

    private static readonly Color DEAD_COLOR = new(0.4f, 0.4f, 0.4f, 0.5f);

    [Tooltip("Seconds without progress in its task (e.g. reaching the next checkpoint) before the agent is eliminated.")]
    [Min(0.1f)] public float StallTimeout = 5;

    [Tooltip("Distance to the target is divided by this (about the arena width) to fit in [0, 1].")]
    [Min(1)] public float TargetDistanceScale = 50;

    public bool Alive { get; private set; }
    public CarPilot Pilot => pilot;
    public bool Active => Alive;
    public Outcome Result { get; private set; }
    public ITrainingTask Task { get; private set; }
    // Drives a fixed brain for others to train against (the runner of a chase): not part of the population. It never
    // drops out: identical runners crashing on the same course ended the chase for every chaser at once, in about
    // 70 % of the courses. Set it before Setup.
    public bool IsRunner { get; set; }

    public float Fitness => Alive ? Task.Fitness(transform.position) : finalFitness;

    public int InputCount => CarPilot.InputCount(GetComponent<CarSensors>());

    private CarWritter writer;
    private CarSensors sensors;
    private CarCollisionReader collisionReader;
    private Rigidbody2D body;
    private SpriteRenderer sprite;
    private Color aliveColor;

    private CarPilot pilot;
    private Vector3 startPosition;
    private Quaternion startRotation;

    private float stallTime;
    private float finalFitness;

    // Call once, right after the agent is activated and before its first Start.
    public void Setup(ITrainingTask task, NeuralNetwork brain)
    {
        writer = GetComponent<CarWritter>();
        sensors = GetComponent<CarSensors>();
        collisionReader = GetComponent<CarCollisionReader>();
        body = GetComponent<Rigidbody2D>();
        sprite = GetComponent<SpriteRenderer>();
        aliveColor = sprite.color;

        Task = task;
        pilot = new CarPilot(writer, sensors, brain, TargetDistanceScale);
        startPosition = transform.position;
        startRotation = transform.rotation;

        writer.ExternalStep = true;
        writer.Location.InputSource = this;
        collisionReader.CrashHandler = _ =>
        {
            if (!IsRunner)
                Die(Outcome.Crashed);
        };

        // Ignore other agents inside the raycast itself: filtering them afterwards let a crowd of agents
        // fill the hit buffer and hide the obstacle behind them, so a genome's score depended on the others.
        if (gameObject.layer == 0)
            Debug.LogWarning($"{name} is on the Default layer, together with the obstacles: its sensors will still see other agents.");
        else
            sensors.ObstacleMask &= ~(1 << gameObject.layer);
    }

    // Gives the agent another task; takes effect with the next Begin.
    public void UseTask(ITrainingTask task)
    {
        Task = task;
    }

    // Out of this round: neither driving nor drawn, until the next Begin.
    public void Bench()
    {
        Alive = false;
        Result = Outcome.Running;
        body.simulated = false;
        sprite.enabled = false;
    }

    public void Begin(float[] genome)
    {
        pilot.Brain.SetParameters(genome);

        writer.ResetTo(startPosition, startRotation);
        body.linearVelocity = Vector2.zero;
        body.angularVelocity = 0;
        body.simulated = true;
        collisionReader.Collisions = new();
        sprite.color = aliveColor;
        sprite.enabled = true;

        Task.Begin(startPosition);
        stallTime = 0;
        Alive = true;
        Result = Outcome.Running;
    }

    // Advances this agent one simulation step. The TrainingManager steps every agent in a fixed order.
    public void Tick(float deltaTime)
    {
        if (!Alive)
            return;

        writer.Step(deltaTime);

        if (Task.Advance(transform.position))
            stallTime = 0;
        stallTime += deltaTime;
        if (stallTime > StallTimeout && IsRunner && Task is CheckpointTask stuck)
        {
            // Stuck against something or circling a checkpoint: let it head for the next one.
            stuck.Skip();
            stallTime = 0;
        }
        else if (stallTime > StallTimeout)
            Die(Outcome.Stalled);
    }

    public void Reach(Checkpoint checkpoint)
    {
        if (Alive && Task is CheckpointTask checkpoints)
            checkpoints.Reach(checkpoint);
    }

    public void Finish() => Die(Outcome.Finished);

    public IReadOnlyList<InputValue> Read() => pilot.Drive(Task.Target(transform.position));

    private void Die(Outcome outcome)
    {
        if (!Alive)
            return;

        finalFitness = Task.Fitness(transform.position);
        Alive = false;
        Result = outcome;

        body.simulated = false;
        sprite.color = DEAD_COLOR;
    }
}
