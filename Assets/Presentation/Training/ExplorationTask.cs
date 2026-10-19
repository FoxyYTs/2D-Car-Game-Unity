using System.Collections.Generic;
using UnityEngine;

// No target: cover as much of the map as possible. The arena is split into square cells and the fitness is the
// number of different cells the car drove through; staying in cells already visited is no progress.
public class ExplorationTask : ITrainingTask
{
    private readonly Vector2 arenaMin;
    private readonly float cellSize;
    private readonly int columns;
    private readonly HashSet<int> visited = new();

    public int Visited => visited.Count;
    public string Status => $"visited {Visited} cells";

    public ExplorationTask(Vector2 arenaMin, Vector2 arenaMax, float cellSize)
    {
        this.arenaMin = arenaMin;
        this.cellSize = cellSize;
        columns = Mathf.CeilToInt((arenaMax.x - arenaMin.x) / cellSize) + 1;
    }

    // Cells whose center is not inside an obstacle: about how many an agent could visit.
    public static int CountFreeCells(Vector2 arenaMin, Vector2 arenaMax, float cellSize, LayerMask obstacles)
    {
        // Checkpoints are triggers on the obstacle layer: they are not in the way.
        var filter = new ContactFilter2D { useTriggers = false };
        filter.SetLayerMask(obstacles);
        var hits = new Collider2D[1];

        int free = 0;
        for (float x = arenaMin.x + cellSize / 2; x < arenaMax.x; x += cellSize)
            for (float y = arenaMin.y + cellSize / 2; y < arenaMax.y; y += cellSize)
                if (Physics2D.OverlapPoint(new Vector2(x, y), filter, hits) == 0)
                    free++;
        return free;
    }

    public void Begin(Vector2 start)
    {
        visited.Clear();
        visited.Add(Cell(start));
    }

    public Vector2 Target(Vector2 position) => position;

    public bool Advance(Vector2 position) => visited.Add(Cell(position));

    public float Fitness(Vector2 position) => Visited - 1;

    private int Cell(Vector2 position)
    {
        int column = Mathf.FloorToInt((position.x - arenaMin.x) / cellSize);
        int row = Mathf.FloorToInt((position.y - arenaMin.y) / cellSize);
        return row * columns + column;
    }
}
