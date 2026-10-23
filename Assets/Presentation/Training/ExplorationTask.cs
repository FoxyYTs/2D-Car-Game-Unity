using System.Collections.Generic;
using UnityEngine;

// Cover as much of the map as possible. The arena is split into square cells and the fitness is the number of
// different cells the car drove through; staying in cells already visited is no progress. The target is the
// nearest free cell not visited yet, so exploring is also "drive to a target without crashing".
public class ExplorationTask : ITrainingTask
{
    private readonly Vector2 arenaMin;
    private readonly float cellSize;
    private readonly int columns;
    private readonly IReadOnlyList<Vector2> freeCells;
    private readonly HashSet<int> visited = new();

    public int Visited => visited.Count;
    public string Status => $"visited {Visited} cells";

    // freeCells: from FreeCells, shared by every agent.
    public ExplorationTask(Vector2 arenaMin, Vector2 arenaMax, float cellSize, IReadOnlyList<Vector2> freeCells)
    {
        this.arenaMin = arenaMin;
        this.freeCells = freeCells;
        this.cellSize = cellSize;
        columns = Mathf.CeilToInt((arenaMax.x - arenaMin.x) / cellSize) + 1;
    }

    // Centers of the cells that are not inside an obstacle: about the cells an agent could visit.
    public static List<Vector2> FreeCells(Vector2 arenaMin, Vector2 arenaMax, float cellSize, LayerMask obstacles)
    {
        // Checkpoints are triggers on the obstacle layer: they are not in the way.
        var filter = new ContactFilter2D { useTriggers = false };
        filter.SetLayerMask(obstacles);
        var hits = new Collider2D[1];

        var free = new List<Vector2>();
        for (float x = arenaMin.x + cellSize / 2; x < arenaMax.x; x += cellSize)
            for (float y = arenaMin.y + cellSize / 2; y < arenaMax.y; y += cellSize)
                if (Physics2D.OverlapPoint(new Vector2(x, y), filter, hits) == 0)
                    free.Add(new Vector2(x, y));
        return free;
    }

    public void Begin(Vector2 start)
    {
        visited.Clear();
        visited.Add(Cell(start));
    }

    // shortcut: straight-line nearest, it can be behind a wall; use a path distance if agents get stuck on walls.
    public Vector2 Target(Vector2 position)
    {
        Vector2 target = position;
        float best = float.MaxValue;
        foreach (var cell in freeCells)
        {
            float distance = (cell - position).sqrMagnitude;
            if (distance < best && !visited.Contains(Cell(cell)))
            {
                best = distance;
                target = cell;
            }
        }
        return target;
    }

    public bool Advance(Vector2 position) => visited.Add(Cell(position));

    public float Fitness(Vector2 position) => Visited - 1;

    private int Cell(Vector2 position)
    {
        int column = Mathf.FloorToInt((position.x - arenaMin.x) / cellSize);
        int row = Mathf.FloorToInt((position.y - arenaMin.y) / cellSize);
        return row * columns + column;
    }
}
