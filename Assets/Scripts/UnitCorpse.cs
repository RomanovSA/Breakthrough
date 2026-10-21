using UnityEngine;

/// <summary>
/// What is left of a unit that has died: it stops moving and breathing (the animator and the weapon IK are switched off, the pose
/// is frozen) and falls over backwards, away from where the bullet came from, and stays lying there.
/// Unit adds this when its health runs out. The fall is made by turning the model around its feet by 90 degrees (and lifting it a
/// little so that the body lies on the ground, not in it).
/// </summary>
public class UnitCorpse : MonoBehaviour
{
    [SerializeField, Min(0.1f)] private float fallTime = 0.75f;
    [Tooltip("How far the body is lifted when it has fallen (half its thickness), so that it lies on the tile and not inside it.")]
    [SerializeField, Min(0f)] private float lift = 0.17f;

    private Transform body;
    private Quaternion fromRotation;
    private Quaternion toRotation;
    private Vector3 fromPosition;
    private Vector3 toPosition;
    private float time;

    /// <summary>True until the body is on the ground.</summary>
    public bool IsFalling { get; private set; }

    /// <summary>Starts the fall of 'model' (the object that turns with the unit). 'direction' is where the body falls: along the bullet.</summary>
    public void Begin(Transform model, Vector3 direction)
    {
        body = model;
        direction.y = 0f;
        if (direction.sqrMagnitude < 1e-4f)
            direction = -model.forward;
        direction.Normalize();

        // turning by 90 degrees around this axis lays the head down towards 'direction'
        Vector3 axis = Vector3.Cross(Vector3.up, direction);
        float sway = Random.Range(-18f, 18f);                                     // not all of them lie exactly alike

        fromRotation = body.rotation;
        toRotation = Quaternion.AngleAxis(sway, Vector3.up) * Quaternion.AngleAxis(90f, axis) * fromRotation;
        fromPosition = body.position;
        toPosition = fromPosition + Vector3.up * lift;
        time = 0f;
        IsFalling = true;
    }

    private void Update()
    {
        Step(Time.deltaTime);
    }

    /// <summary>One step of the fall (done every frame by itself).</summary>
    public void Step(float deltaTime)
    {
        if (!IsFalling || body == null)
            return;

        time += deltaTime;
        float k = Mathf.Clamp01(time / fallTime);
        float eased = k * k * (3f - 2f * k) * 0.35f + k * k * k * 0.65f;           // slow to start, quick at the end, like a fall
        body.rotation = Quaternion.Slerp(fromRotation, toRotation, eased);
        body.position = Vector3.Lerp(fromPosition, toPosition, eased);

        if (k >= 1f)
            IsFalling = false;
    }
}
