using Assets.Logic.AI;
using EngineAbstractor;
using System.Collections.Generic;
using UnityEngine;

// A car in a training duel: its brain aims at the opponent. The VersusTrainingManager, which sees both cars,
// decides how each match ends; the agent only reports its own crashes into obstacles.
[RequireComponent(typeof(CarWritter), typeof(CarSensors), typeof(CarCollisionReader))]
public class DuelAgent : MonoBehaviour, IInputSource
{
    private static readonly Color OUT_COLOR = new(0.4f, 0.4f, 0.4f, 0.5f);

    [Tooltip("Must be the value the seed brain was trained with.")]
    [Min(1)] public float TargetDistanceScale = 50;

    public bool Playing { get; private set; }
    public bool Crashed { get; private set; }
    public string CrashedInto { get; private set; }
    public float AliveTime { get; private set; }
    public float StartDistance { get; private set; }
    public float MinDistance { get; private set; }
    public DuelAgent Opponent { get; private set; }

    // The game decides who rammed whom by which collider touched first: the front bumper or the body.
    public CircleCollider2D Bumper { get; private set; }
    public BoxCollider2D Body { get; private set; }

    public int InputCount => CarPilot.InputCount(GetComponent<CarSensors>());

    private CarWritter writer;
    private CarCollisionReader collisionReader;
    private Rigidbody2D body;
    private SpriteRenderer sprite;
    private Color playingColor;
    private CarPilot pilot;

    // Call once, right after the agent is activated and before its first Start.
    public void Setup(NeuralNetwork brain)
    {
        writer = GetComponent<CarWritter>();
        var sensors = GetComponent<CarSensors>();
        collisionReader = GetComponent<CarCollisionReader>();
        body = GetComponent<Rigidbody2D>();
        sprite = GetComponent<SpriteRenderer>();
        playingColor = sprite.color;
        Bumper = GetComponent<CircleCollider2D>();
        Body = GetComponent<BoxCollider2D>();

        pilot = new CarPilot(writer, sensors, brain, TargetDistanceScale);
        writer.ExternalStep = true;
        writer.Location.InputSource = this;
        collisionReader.CrashHandler = _ => Crash();

        // Agents of other matches share the arena: they must not hide obstacles from the sensors.
        if (gameObject.layer == 0)
            Debug.LogWarning($"{name} is on the Default layer, together with the obstacles: its sensors will still see other agents.");
        else
            sensors.ObstacleMask &= ~(1 << gameObject.layer);
    }

    public void Begin(float[] genome, Vector3 position, Quaternion rotation, DuelAgent opponent)
    {
        gameObject.SetActive(true);
        pilot.Brain.SetParameters(genome);

        writer.ResetTo(position, rotation);
        body.linearVelocity = Vector2.zero;
        body.angularVelocity = 0;
        body.simulated = true;
        collisionReader.Collisions = new();
        sprite.color = playingColor;

        Opponent = opponent;
        Playing = true;
        Crashed = false;
        CrashedInto = null;
        AliveTime = 0;
    }

    // Call once both cars of the match are in place.
    public void MeasureStartDistance()
    {
        StartDistance = MinDistance = DistanceToOpponent();
    }

    public void Tick(float deltaTime)
    {
        if (!Playing)
            return;

        writer.Step(deltaTime);
        AliveTime += deltaTime;
    }

    public void TrackDistance()
    {
        MinDistance = Mathf.Min(MinDistance, DistanceToOpponent());
    }

    // The match is over for this car: it stays where it is, out of the physics world.
    public void Stop(bool lost)
    {
        Playing = false;
        body.simulated = false;
        if (lost)
            sprite.color = OUT_COLOR;
    }

    public void Bench()
    {
        Playing = false;
        gameObject.SetActive(false);
    }

    public IReadOnlyList<InputValue> Read() => pilot.Drive(Opponent.transform.position);

    private void Crash()
    {
        if (!Playing)
            return;

        Crashed = true;
        CrashedInto = collisionReader.Collisions.Obstacle;
        Stop(lost: true);
    }

    private float DistanceToOpponent() => Vector2.Distance(transform.position, Opponent.transform.position);
}
