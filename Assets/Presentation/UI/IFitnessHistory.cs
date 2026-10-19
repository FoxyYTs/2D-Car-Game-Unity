using System.Collections.Generic;
using UnityEngine;

// A trainer's fitness per generation, for the chart of the brain panel.
public interface IFitnessHistory
{
    // Best and average fitness of every finished generation.
    List<Vector2> History { get; }
    // Indices of History where the environment changed, so scores before and after are not comparable.
    List<int> CircuitStarts { get; }
}
