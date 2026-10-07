using UnityEngine;

// A trigger of a CheckpointCircuit. Its place in the circuit is its order among the circuit's children.
[RequireComponent(typeof(Collider2D))]
public class Checkpoint : MonoBehaviour
{
    void Reset()
    {
        GetComponent<Collider2D>().isTrigger = true;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        var agent = other.GetComponentInParent<CarAgent>();
        if (agent != null)
            agent.Reach(this);
    }
}
