using System;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// Closed circuit made of the Checkpoint children of this object, in hierarchy order: after the last one comes the first.
// It can rearrange itself into a new random ring around RingCenter, so agents must follow the checkpoints instead of
// memorizing one route. The ring defaults fit the SampleScene map (mountain ridge around x = -0.3, y = 2.1).
public class CheckpointCircuit : MonoBehaviour
{
    private const int RING_SEGMENTS = 32;
    private const int MAX_PLACEMENT_ATTEMPTS = 30;
    private const int MARKER_SORTING_ORDER = 1;
    private static readonly Color MARKER_COLOR = new(1f, 0.85f, 0.2f, 0.6f);

    [Header("Random ring")]
    public Vector2 RingCenter = new(-0.3f, 2.1f);
    [Tooltip("Horizontal distance from the center, sampled per checkpoint.")]
    public Vector2 RadiusXRange = new(8, 20);
    [Tooltip("Vertical distance from the center, sampled per checkpoint.")]
    public Vector2 RadiusYRange = new(6.5f, 8);
    [Tooltip("Angle of the first checkpoint around the center; the rest follow clockwise, evenly spaced.")]
    public float FirstAngle = 150;
    [Range(0, 30)] public float AngleJitter = 15;
    [Tooltip("Checkpoint centers stay at least this far from any obstacle.")]
    [Min(0)] public float Clearance = 1.5f;
    [Tooltip("Layers that count as obstacles when placing checkpoints (not the agents).")]
    public LayerMask ObstacleMask = 1 << 0;
    public Vector2 ArenaMin = new(-24.5f, -6.5f);
    public Vector2 ArenaMax = new(25, 10.5f);

    [Header("Game view")]
    public bool ShowInGame = true;

    private Checkpoint[] checkpoints;
    private LineRenderer path;
    private LineRenderer[] rings;

    private Checkpoint[] Checkpoints => checkpoints ??= GetComponentsInChildren<Checkpoint>();

    public int Count => Checkpoints.Length;

    // Wraps around, so index Count is the first checkpoint of the next lap.
    public Checkpoint this[int index] => Checkpoints[index % Count];

    void Start()
    {
        if (ShowInGame)
            BuildMarkers();
    }

    // Moves every checkpoint to a new spot of a random ring. Spots that overlap an obstacle are re-sampled;
    // if none fits, the checkpoint keeps its place.
    public void Rearrange(System.Random random)
    {
        float step = 360f / Count;

        for (int i = 0; i < Count; i++)
        {
            float baseAngle = FirstAngle - step * i;
            if (TryFindSpot(baseAngle, random, out var spot))
                Checkpoints[i].transform.position = spot;
            else
                Debug.LogWarning($"No free spot for {Checkpoints[i].name}; it keeps its position.");
        }

        RefreshMarkers();
    }

    private bool TryFindSpot(float baseAngle, System.Random random, out Vector2 spot)
    {
        var obstacles = new ContactFilter2D { useTriggers = false };
        obstacles.SetLayerMask(ObstacleMask);
        var overlaps = new Collider2D[1];

        for (int attempt = 0; attempt < MAX_PLACEMENT_ATTEMPTS; attempt++)
        {
            float angle = (baseAngle + Range(random, -AngleJitter, AngleJitter)) * Mathf.Deg2Rad;
            spot = RingCenter + new Vector2(Mathf.Cos(angle) * Range(random, RadiusXRange.x, RadiusXRange.y),
                                            Mathf.Sin(angle) * Range(random, RadiusYRange.x, RadiusYRange.y));

            bool insideArena = spot.x >= ArenaMin.x && spot.x <= ArenaMax.x && spot.y >= ArenaMin.y && spot.y <= ArenaMax.y;
            if (insideArena && Physics2D.OverlapCircle(spot, Clearance, obstacles, overlaps) == 0)
                return true;
        }

        spot = default;
        return false;
    }

    private static float Range(System.Random random, float min, float max) => min + (float)random.NextDouble() * (max - min);

    private void BuildMarkers()
    {
        var material = new Material(Shader.Find("Sprites/Default"));

        path = CreateLine("Path", material, loop: true);
        rings = new LineRenderer[Count];
        for (int i = 0; i < Count; i++)
            rings[i] = CreateLine($"Ring {i}", material, loop: true);

        RefreshMarkers();
    }

    private LineRenderer CreateLine(string name, Material material, bool loop)
    {
        var line = new GameObject(name).AddComponent<LineRenderer>();
        line.transform.SetParent(transform, false);
        line.sharedMaterial = material;
        line.useWorldSpace = true;
        line.loop = loop;
        line.widthMultiplier = 0.08f;
        line.startColor = line.endColor = MARKER_COLOR;
        line.sortingOrder = MARKER_SORTING_ORDER;
        return line;
    }

    private void RefreshMarkers()
    {
        if (path == null)
            return;

        path.positionCount = Count;
        for (int i = 0; i < Count; i++)
        {
            Vector3 center = Checkpoints[i].transform.position;
            path.SetPosition(i, center);

            float radius = Radius(Checkpoints[i]);
            rings[i].positionCount = RING_SEGMENTS;
            for (int segment = 0; segment < RING_SEGMENTS; segment++)
            {
                float angle = segment * Mathf.PI * 2 / RING_SEGMENTS;
                rings[i].SetPosition(segment, center + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
            }
        }
    }

    private static float Radius(Checkpoint checkpoint)
    {
        var bounds = checkpoint.GetComponent<Collider2D>().bounds;
        return Math.Max(bounds.extents.x, bounds.extents.y);
    }

    void OnGUI()
    {
        if (!ShowInGame || Camera.main == null)
            return;

        for (int i = 0; i < Count; i++)
        {
            Vector3 screen = Camera.main.WorldToScreenPoint(Checkpoints[i].transform.position);
            GUI.Label(new Rect(screen.x - 5, Screen.height - screen.y - 10, 30, 20), i.ToString());
        }
    }

    void OnDrawGizmos()
    {
        var points = GetComponentsInChildren<Checkpoint>();

        Gizmos.color = Color.yellow;
        for (int i = 0; i < points.Length; i++)
        {
            Gizmos.DrawLine(points[i].transform.position, points[(i + 1) % points.Length].transform.position);
#if UNITY_EDITOR
            Handles.Label(points[i].transform.position, i.ToString());
#endif
        }
    }
}
