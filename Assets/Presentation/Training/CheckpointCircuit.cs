using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// Closed circuit made of the Checkpoint children of this object, in hierarchy order: after the last one comes the first.
public class CheckpointCircuit : MonoBehaviour
{
    private Checkpoint[] checkpoints;

    private Checkpoint[] Checkpoints => checkpoints ??= GetComponentsInChildren<Checkpoint>();

    public int Count => Checkpoints.Length;

    // Wraps around, so index Count is the first checkpoint of the next lap.
    public Checkpoint this[int index] => Checkpoints[index % Count];

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
