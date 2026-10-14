using UnityEngine;

/// <summary>
/// Connects the unit's movement and actions to the character's Animator. It does not control the unit,
/// it only watches it and plays the right animations:
///  - while the unit is moving, the Animator plays the Run clip, otherwise Idle; the run clip is sped up or
///    slowed down to match the real speed, so the steps fit the ground covered;
///  - the figure turns to face the direction of travel (or the target it is aiming at);
///  - AimAt / StopAiming raise and lower the rifle, PlayShoot fires one shot (recoil).
/// Sits on the Model object (the part that turns); the Animator lives on the character below it.
/// </summary>
public class UnitAnimator : MonoBehaviour
{
    private static readonly int MovingId = Animator.StringToHash("Moving");
    private static readonly int RunSpeedId = Animator.StringToHash("RunSpeed");
    private static readonly int AimingId = Animator.StringToHash("Aiming");
    private static readonly int ShootId = Animator.StringToHash("Shoot");

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

    private bool aiming;
    private bool hasAimPoint;
    private Vector3 aimPoint;

    /// <summary>True while the rifle is raised.</summary>
    public bool IsAiming => aiming;

    private void OnEnable()
    {
        hasLastPosition = false;
    }

    private void LateUpdate()
    {
        Step(Time.deltaTime);
    }

    /// <summary>Raises the rifle and turns the unit towards the given point.</summary>
    public void AimAt(Vector3 worldPoint)
    {
        aiming = true;
        hasAimPoint = true;
        aimPoint = worldPoint;
        SetAimingFlag(true);
    }

    /// <summary>Raises the rifle without turning the unit.</summary>
    public void StartAiming()
    {
        aiming = true;
        hasAimPoint = false;
        SetAimingFlag(true);
    }

    /// <summary>Lowers the rifle again.</summary>
    public void StopAiming()
    {
        aiming = false;
        hasAimPoint = false;
        SetAimingFlag(false);
    }

    /// <summary>Fires one shot: a quick recoil. If the rifle is lowered it is raised for the shot and lowered again.</summary>
    public void PlayShoot()
    {
        if (animator != null)
            animator.SetTrigger(ShootId);
    }

    // Handy for trying the animations out in Play mode: right-click the Unit Animator component header.
    [ContextMenu("Test: raise / lower the rifle")]
    private void TestToggleAim()
    {
        if (aiming)
            StopAiming();
        else
            StartAiming();
    }

    [ContextMenu("Test: shoot")]
    private void TestShoot()
    {
        PlayShoot();
    }

    private void SetAimingFlag(bool value)
    {
        if (animator != null)
            animator.SetBool(AimingId, value);
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

        // A unit cannot aim while it is running.
        if (moving && aiming)
            StopAiming();

        if (moving)
        {
            Quaternion target = Quaternion.LookRotation(flat / speed, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeed * dt);
        }
        else if (hasAimPoint)
        {
            Vector3 toTarget = aimPoint - position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude > 0.0001f)
            {
                Quaternion target = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeed * dt);
            }
        }

        if (animator != null)
        {
            animator.SetBool(MovingId, moving);
            float multiplier = moving ? Mathf.Clamp(speed / Mathf.Max(0.01f, strideLength), minRunSpeed, maxRunSpeed) : 1f;
            animator.SetFloat(RunSpeedId, multiplier);
        }
    }
}
