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
