using Assets.Logic.AI;
using Assets.Logic.CarLocation;
using EngineAbstractor;
using System.Collections.Generic;
using UnityEngine;

// Drives a car with a neural network: what the car perceives goes in, key presses come out.
// The network sees the distance sensors, speed, steering angle, and the angle and distance to a target point
// (the next checkpoint while training, the opponent in a versus match).
public class CarPilot
{
    // The network outputs throttle and steering in [-1, 1]; beyond this dead zone they press a key.
    private const float OUTPUT_THRESHOLD = 0.3f;
    private const int EXTRA_INPUTS = 4;
    public const int OUTPUTS = 2;

    public NeuralNetwork Brain { get; }

    private readonly CarWritter writer;
    private readonly CarSensors sensors;
    private readonly float targetDistanceScale;
    private readonly float[] observation;
    private readonly List<InputValue> inputs = new();

    public static int InputCount(CarSensors sensors) => sensors.Count + EXTRA_INPUTS;

    public CarPilot(CarWritter writer, CarSensors sensors, NeuralNetwork brain, float targetDistanceScale)
    {
        this.writer = writer;
        this.sensors = sensors;
        this.targetDistanceScale = targetDistanceScale;
        Brain = brain;
        observation = new float[InputCount(sensors)];
    }

    public IReadOnlyList<InputValue> Drive(Vector3 target)
    {
        var readings = sensors.Sense();
        for (int i = 0; i < readings.Count; i++)
            observation[i] = readings[i];

        var chassis = writer.Location.Chassis;
        observation[readings.Count] = chassis.Speed / CarPowerTrain.MaxSpeed;
        observation[readings.Count + 1] = chassis.SteeringAngle / CarChassis.MAX_STEERING_ANGLE;

        // Positive angle = target to the left, the same sign as steering left.
        var car = writer.transform;
        Vector2 toTarget = target - car.position;
        observation[readings.Count + 2] = Vector2.SignedAngle(car.up, toTarget) / 180f;
        observation[readings.Count + 3] = Mathf.Clamp01(toTarget.magnitude / targetDistanceScale);

        var output = Brain.Feedforward(observation);
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
}
