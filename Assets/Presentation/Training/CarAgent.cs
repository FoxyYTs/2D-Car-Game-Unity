using Assets.Logic.AI;
using EngineAbstractor;
using System.Collections.Generic;
using UnityEngine;

// Drives a car with a neural network and scores how far it gets along a CheckpointCircuit.
[RequireComponent(typeof(CarWritter), typeof(CarSensors), typeof(CarCollisionReader))]
public class CarAgent : MonoBehaviour, IInputSource
{
    public enum Outcome
    {
        Running,
        Crashed,
        Stalled
    }

    private static readonly Color DEAD_COLOR = new(0.4f, 0.4f, 0.4f, 0.5f);

    [Tooltip("Seconds without reaching the next checkpoint before the agent is eliminated.")]
    [Min(0.1f)] public float StallTimeout = 5;

    [Tooltip("Distance to the next checkpoint is divided by this (about the arena width) to fit in [0, 1].")]
    [Min(1)] public float TargetDistanceScale = 50;

    public bool Alive { get; private set; }
    public Outcome Result { get; private set; }
    public int CheckpointsReached { get; private set; }
    public int NextCheckpointIndex => CheckpointsReached % circuit.Count;

    // Checkpoints reached in order, plus how close it got to the next one (0 to 1).
    public float Fitness => Alive ? CheckpointsReached + Progress() : finalFitness;

    public int InputCount => CarPilot.InputCount(GetComponent<CarSensors>());

    private CarWritter writer;
    private CarSensors sensors;
    private CarCollisionReader collisionReader;
    private Rigidbody2D body;
    private SpriteRenderer sprite;
    private Color aliveColor;

    private CarPilot pilot;
    private CheckpointCircuit circuit;
    private Vector3 startPosition;
    private Quaternion startRotation;

    private float stallTime;
    private float finalFitness;

    // Call once, right after the agent is activated and before its first Start.
    public void Setup(CheckpointCircuit circuit, NeuralNetwork brain)
    {
        writer = GetComponent<CarWritter>();
        sensors = GetComponent<CarSensors>();
        collisionReader = GetComponent<CarCollisionReader>();
        body = GetComponent<Rigidbody2D>();
        sprite = GetComponent<SpriteRenderer>();
        aliveColor = sprite.color;

        this.circuit = circuit;
        pilot = new CarPilot(writer, sensors, brain, TargetDistanceScale);
        startPosition = transform.position;
        startRotation = transform.rotation;

        writer.ExternalStep = true;
        writer.Location.InputSource = this;
        collisionReader.CrashHandler = _ => Die(Outcome.Crashed);

        // Ignore other agents inside the raycast itself: filtering them afterwards let a crowd of agents
        // fill the hit buffer and hide the obstacle behind them, so a genome's score depended on the others.
        if (gameObject.layer == 0)
            Debug.LogWarning($"{name} is on the Default layer, together with the obstacles: its sensors will still see other agents.");
        else
            sensors.ObstacleMask &= ~(1 << gameObject.layer);
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

        CheckpointsReached = 0;
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

        stallTime += deltaTime;
        if (stallTime > StallTimeout)
            Die(Outcome.Stalled);
    }

    public void Reach(Checkpoint checkpoint)
    {
        if (!Alive || checkpoint != circuit[CheckpointsReached])
            return;

        CheckpointsReached++;
        stallTime = 0;
    }

    public IReadOnlyList<InputValue> Read() => pilot.Drive(circuit[CheckpointsReached].transform.position);

    private void Die(Outcome outcome)
    {
        if (!Alive)
            return;

        finalFitness = CheckpointsReached + Progress();
        Alive = false;
        Result = outcome;

        body.simulated = false;
        sprite.color = DEAD_COLOR;
    }

    private float Progress()
    {
        Vector2 target = circuit[CheckpointsReached].transform.position;
        Vector2 from = CheckpointsReached == 0
                           ? startPosition
                           : circuit[CheckpointsReached - 1].transform.position;

        float span = Vector2.Distance(from, target);
        if (span <= 0)
            return 0;

        return Mathf.Clamp01(1 - Vector2.Distance(transform.position, target) / span);
    }
}
