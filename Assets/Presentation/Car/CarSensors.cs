using System;
using System.Collections.Generic;
using UnityEngine;

// Distance sensors for the AI: rays cast from the car, normalized to [0, 1] (1 = nothing within MaxDistance).
public class CarSensors : MonoBehaviour
{
    private const string CAR_TAG = "Player";
    private const int HIT_BUFFER_SIZE = 8;

    [Min(1)] public int RayCount = 7;
    [Range(0, 360)] public float FieldOfView = 180;
    public bool RearRay = true;
    [Min(0.1f)] public float MaxDistance = 15;
    public LayerMask ObstacleMask = ~0;
    public bool DetectCars = false;
    [Tooltip("Debug: sense every frame so the rays can be inspected with Gizmos while driving by hand.")]
    public bool SenseOnUpdate = false;

    private readonly RaycastHit2D[] hitBuffer = new RaycastHit2D[HIT_BUFFER_SIZE];
    private Collider2D[] ownColliders;
    private float[] readings;
    private float[] rayAngles;

    public int Count => RayAngles.Length;

    public IReadOnlyList<float> Readings => readings ??= Fill(new float[Count], 1);

    private float[] RayAngles => rayAngles ??= BuildRayAngles();

    void Awake()
    {
        ownColliders = GetComponents<Collider2D>();
    }

    void Update()
    {
        if (SenseOnUpdate)
            Sense();
    }

    void OnValidate()
    {
        rayAngles = null;
        readings = null;
    }

    public IReadOnlyList<float> Sense()
    {
        var filter = new ContactFilter2D { useTriggers = false };
        filter.SetLayerMask(ObstacleMask);

        var angles = RayAngles;
        if (readings is null || readings.Length != angles.Length)
            readings = new float[angles.Length];

        for (int i = 0; i < angles.Length; i++)
            readings[i] = Cast(DirectionAt(angles[i]), filter) / MaxDistance;

        return readings;
    }

    private float Cast(Vector2 direction, ContactFilter2D filter)
    {
        int hits = Physics2D.Raycast(transform.position, direction, filter, hitBuffer, MaxDistance);

        float nearest = MaxDistance;
        for (int i = 0; i < hits; i++)
        {
            var collider = hitBuffer[i].collider;
            if (Array.IndexOf(ownColliders, collider) >= 0)
                continue;
            if (!DetectCars && collider.CompareTag(CAR_TAG))
                continue;
            nearest = Math.Min(nearest, hitBuffer[i].distance);
        }
        return nearest;
    }

    // The car sprite faces transform.up (see CarWritter.RotationFrom).
    private Vector2 DirectionAt(float angle) => Quaternion.Euler(0, 0, angle) * transform.up;

    private float[] BuildRayAngles()
    {
        var angles = new List<float>();

        if (RayCount == 1)
            angles.Add(0);
        else
        {
            // A full circle would place the first and last rays on the same direction.
            float step = FieldOfView / (FieldOfView >= 360 ? RayCount : RayCount - 1);
            for (int i = 0; i < RayCount; i++)
                angles.Add(-FieldOfView / 2 + step * i);
        }

        if (RearRay && FieldOfView < 360)
            angles.Add(180);

        return angles.ToArray();
    }

    private static float[] Fill(float[] values, float value)
    {
        Array.Fill(values, value);
        return values;
    }

    void OnDrawGizmos()
    {
        var angles = RayAngles;
        var values = Readings;

        for (int i = 0; i < angles.Length; i++)
        {
            float reading = i < values.Count ? values[i] : 1;
            Gizmos.color = Color.Lerp(Color.red, Color.green, reading);
            Gizmos.DrawLine(transform.position, (Vector2)transform.position + DirectionAt(angles[i]) * reading * MaxDistance);
        }
    }
}
