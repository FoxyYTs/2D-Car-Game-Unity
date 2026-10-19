using UnityEngine;

// What a CarAgent is trained to do: where it aims, when it makes progress and how it is scored.
// Each agent has its own task, so it can keep that agent's progress.
public interface ITrainingTask
{
    void Begin(Vector2 start);

    // The point the pilot aims at. The car's own position means "no target": angle and distance 0.
    Vector2 Target(Vector2 position);

    // Called after every step; true when the agent made progress, which resets its stall timer.
    bool Advance(Vector2 position);

    float Fitness(Vector2 position);

    // For the training log, e.g. "heading to checkpoint 3".
    string Status { get; }
}
