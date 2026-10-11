using UnityEngine;

/// <summary>
/// Connects the unit's movement to the character's Animator. It does not control the unit, it only watches it:
///  - while the unit is moving, the Animator plays the Run clip, otherwise Idle;
///  - the run clip is sped up or slowed down to match the real speed, so the steps fit the ground covered;
///  - the figure turns to face the direction of travel.
/// Sits on the Model object (the part that turns); the Animator lives on the character below it.
/// </summary>
public class UnitAnimator : MonoBehaviour
{
    private static readonly int MovingId = Animator.StringToHash("Moving");
    private static readonly int RunSpeedId = Animator.StringToHash("RunSpeed");

    [SerializeField] private Animator animator;

    [Header("Run")]
    [Tooltip("World units covered by one full cycle of the Run clip (two steps).")]
    [SerializeField] private float strideLength = 1.4f;
    [SerializeField] private float minRunSpeed = 0.6f;
    [SerializeField] private float maxRunSpeed = 3.5f;

    [Header("Turning")]
    [SerializeField] private float turnSpeed = 720f; // degrees per second

    private const float MovingThreshold = 0.1f;   // world units per second
    private const float TeleportThreshold = 20f;  // faster than this is a jump (PlaceOn), not running

    private Vector3 lastPosition;
    private bool hasLastPosition;

    private void OnEnable()
    {
        hasLastPosition = false;
    }

    private void LateUpdate()
    {
        Step(Time.deltaTime);
    }

    private void Step(float dt)
    {
        if (dt <= 0f)
            return;

        Vector3 position = transform.position;
        Vector3 velocity = hasLastPosition ? (position - lastPosition) / dt : Vector3.zero;
        lastPosition = position;
        hasLastPosition = true;

        Vector3 flat = new Vector3(velocity.x, 0f, velocity.z);
        float speed = flat.magnitude;
        bool moving = speed > MovingThreshold && speed < TeleportThreshold;

        if (moving)
        {
            Quaternion target = Quaternion.LookRotation(flat / speed, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeed * dt);
        }

        if (animator != null)
        {
            animator.SetBool(MovingId, moving);
            float multiplier = moving ? Mathf.Clamp(speed / Mathf.Max(0.01f, strideLength), minRunSpeed, maxRunSpeed) : 1f;
            animator.SetFloat(RunSpeedId, multiplier);
        }
    }
}
