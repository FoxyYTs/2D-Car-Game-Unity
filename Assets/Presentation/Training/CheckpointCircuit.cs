using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
    [Tooltip("Describe reports how close each leg passes to every obstacle within this distance.")]
    [Min(0)] public float MaxReportedClearance = 3;
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

    // One line per checkpoint: position, turn angle there (positive = left) and the leg to the next one, with the
    // clearance between that straight leg and every obstacle closer than MaxReportedClearance. Agents aim at the next
    // checkpoint, so a small clearance means they must learn to steer around the obstacle (0.87 blocked a whole
    // population for 9 generations; 1.2 did not).
    public string Describe()
    {
        var text = new StringBuilder();

        for (int i = 0; i < Count; i++)
        {
            Vector2 previous = this[i + Count - 1].transform.position;
            Vector2 current = this[i].transform.position;
            Vector2 next = this[i + 1].transform.position;

            Vector2 leg = next - current;
            float turn = Vector2.SignedAngle(current - previous, leg);
            text.Append($"\n  {i}: ({current.x:F1}, {current.y:F1}), turn {turn:F0}°, to {(i + 1) % Count}: {leg.magnitude:F1}");

            var clearances = Clearances(current, next);
            if (clearances.Count > 0)
                text.Append(", clearance " + string.Join(", ", clearances.Select(pair => $"{pair.Key} {pair.Value:F2}")));
        }

        return text.ToString();
    }

    // Distance from the straight leg to each nearby obstacle, found by binary search on the radius of a circle cast.
    private SortedDictionary<string, float> Clearances(Vector2 from, Vector2 to)
    {
        const int SEARCH_STEPS = 12;
        var clearances = new SortedDictionary<string, float>();

        foreach (var obstacle in ObstaclesAlong(from, to, MaxReportedClearance))
        {
            float touching = MaxReportedClearance, free = 0;
            for (int step = 0; step < SEARCH_STEPS; step++)
            {
                float radius = (touching + free) / 2;
                if (ObstaclesAlong(from, to, radius).Contains(obstacle))
                    touching = radius;
                else
                    free = radius;
            }
            clearances[obstacle.name] = free;
        }

        return clearances;
    }

    private HashSet<Collider2D> ObstaclesAlong(Vector2 from, Vector2 to, float radius)
    {
        var obstacles = new ContactFilter2D { useTriggers = false };
        obstacles.SetLayerMask(ObstacleMask);
        var hits = new RaycastHit2D[16];

        Vector2 leg = to - from;
        int count = Physics2D.CircleCast(from, radius, leg.normalized, obstacles, hits, leg.magnitude);
        return new HashSet<Collider2D>(hits.Take(count).Select(hit => hit.collider));
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
