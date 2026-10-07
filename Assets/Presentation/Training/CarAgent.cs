using Assets.Logic.AI;
using Assets.Logic.CarLocation;
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

    // The network outputs throttle and steering in [-1, 1]; beyond this dead zone they press a key.
    private const float OUTPUT_THRESHOLD = 0.3f;
    // Speed, steering, and the angle and distance to the next checkpoint.
    private const int EXTRA_INPUTS = 4;
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

    // Sensor readings, speed, steering angle, and angle and distance to the next checkpoint.
    public int InputCount => GetComponent<CarSensors>().Count + EXTRA_INPUTS;

    private CarWritter writer;
    private CarSensors sensors;
    private CarCollisionReader collisionReader;
    private Rigidbody2D body;
    private SpriteRenderer sprite;
    private Color aliveColor;

    private NeuralNetwork brain;
    private CheckpointCircuit circuit;
    private Vector3 startPosition;
    private Quaternion startRotation;

    private float[] observation;
    private readonly List<InputValue> inputs = new();
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
        this.brain = brain;
        observation = new float[InputCount];
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
        brain.SetParameters(genome);

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

    public IReadOnlyList<InputValue> Read()
    {
        var readings = sensors.Sense();
        for (int i = 0; i < readings.Count; i++)
            observation[i] = readings[i];

        var chassis = writer.Location.Chassis;
        observation[readings.Count] = chassis.Speed / CarPowerTrain.MaxSpeed;
        observation[readings.Count + 1] = chassis.SteeringAngle / CarChassis.MAX_STEERING_ANGLE;

        // Positive angle = target to the left, the same sign as steering left.
        Vector2 toTarget = circuit[CheckpointsReached].transform.position - transform.position;
        observation[readings.Count + 2] = Vector2.SignedAngle(transform.up, toTarget) / 180f;
        observation[readings.Count + 3] = Mathf.Clamp01(toTarget.magnitude / TargetDistanceScale);

        var output = brain.Feedforward(observation);
        float throttle = output[0];
        float steering = output[1];

        inputs.Clear();
        if (throttle > OUTPUT_THRESHOLD)
            inputs.Add(InputValue.Foward);
        else if (throttle < -OUTPUT_THRESHOLD)
            inputs.Add(InputValue.Backward);

        if (steering > OUTPUT_THRESHOLD)
            inputs.Add(InputValue.Left);
        else if (steering < -OUTPUT_THRESHOLD)
            inputs.Add(InputValue.Right);

        return inputs;
    }

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
