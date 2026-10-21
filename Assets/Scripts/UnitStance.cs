using UnityEngine;

/// <summary>
/// Standing or crouching. The character has no crouching animation, so the pose is made in code on top of whatever the animator
/// plays (idle or run): every frame, after the animator, the hips are lowered and moved back a little, the upper body leans forward,
/// and both legs are bent by a two-bone solver so that the feet stay exactly where the animation put them (the knees go forward).
/// The arms and the weapon sit on the upper body and simply go down with it. The body collider shrinks, and the unit's chest, where
/// the ring of spread is centred, goes down (Unit.StanceChestDrop). The crouching unit is a smaller target: the hit chance, which is
/// measured from the picture, and the exact body shape that bullets hit both follow the pose by themselves.
/// Unit adds this component when it first changes stance (Unit.ToggleStance).
/// </summary>
[DefaultExecutionOrder(100)]
public class UnitStance : MonoBehaviour
{
    [Tooltip("How far the hips go down in a crouch (world units).")]
    [SerializeField, Min(0f)] private float hipsDrop = 0.19f;
    [Tooltip("How far the hips move back (the knees go forward), world units.")]
    [SerializeField, Min(0f)] private float hipsBack = 0.08f;
    [Tooltip("How far the upper body leans forward, degrees.")]
    [SerializeField, Range(0f, 40f)] private float lean = 14f;
    [Tooltip("How long the change takes, seconds.")]
    [SerializeField, Min(0.05f)] private float changeTime = 0.3f;
    [Tooltip("The body collider is this tall (share of the standing height) when crouching.")]
    [SerializeField, Range(0.4f, 1f)] private float colliderHeight = 0.78f;

    private Transform hips, spine;
    private Transform upperLeft, lowerLeft, footLeft, upperRight, lowerRight, footRight;
    private bool searched;
    private CapsuleCollider capsule;
    private float standingHeight;
    private Vector3 standingCenter;
    private bool colliderKnown;

    /// <summary>The state the unit is going to (true: crouching).</summary>
    public bool Crouched { get; private set; }

    /// <summary>How far along the change is, 0 (standing) to 1 (crouching), smoothed.</summary>
    public float Amount { get; private set; }

    /// <summary>How much lower the chest is than when standing, right now (world units).</summary>
    public float ChestDrop => Amount * (hipsDrop + 0.02f);

    public void SetCrouched(bool crouched)
    {
        Crouched = crouched;
    }

    private void LateUpdate()
    {
        Step(Time.deltaTime);
        Apply();
    }

    /// <summary>Moves the change along (done every frame by itself).</summary>
    public void Step(float deltaTime)
    {
        Amount = Mathf.MoveTowards(Amount, Crouched ? 1f : 0f, Mathf.Max(0f, deltaTime) / changeTime);
        UpdateCollider();
    }

    /// <summary>
    /// Bends the pose by the present amount. It works on the pose the animator has just made, so it must be called once after every
    /// animator update (LateUpdate does that).
    /// </summary>
    public void Apply()
    {
        if (Amount <= 0.0005f)
            return;
        if (!FindBones())
            return;

        Unit unit = GetComponent<Unit>();
        Vector3 forward = unit != null ? unit.FacingDirection : transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 1e-6f)
            return;
        forward.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, forward);

        float e = Amount * Amount * (3f - 2f * Amount);

        // where the animation has the feet
        Vector3 leftFoot = footLeft.position, rightFoot = footRight.position;
        Quaternion leftFootRotation = footLeft.rotation, rightFootRotation = footRight.rotation;

        // the body goes down and back, the upper body leans forward
        hips.position += (Vector3.down * hipsDrop + -forward * hipsBack) * e;
        spine.rotation = Quaternion.AngleAxis(lean * e, right) * spine.rotation;

        // the legs bend so that the feet stay where they were
        SolveLeg(upperLeft, lowerLeft, footLeft, leftFoot, forward);
        SolveLeg(upperRight, lowerRight, footRight, rightFoot, forward);
        footLeft.rotation = leftFootRotation;
        footRight.rotation = rightFootRotation;
    }

    // Two bones (thigh, shin) reach the point with the foot; the knee points to 'pole'.
    private static void SolveLeg(Transform upper, Transform lower, Transform foot, Vector3 target, Vector3 pole)
    {
        Vector3 hip = upper.position;
        float l1 = Vector3.Distance(hip, lower.position);
        float l2 = Vector3.Distance(lower.position, foot.position);

        Vector3 toTarget = target - hip;
        float d = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(l1 - l2) + 0.001f, l1 + l2 - 0.001f);
        if (toTarget.sqrMagnitude < 1e-8f)
            return;
        Vector3 direction = toTarget.normalized;

        float cosA = Mathf.Clamp((l1 * l1 + d * d - l2 * l2) / (2f * l1 * d), -1f, 1f);
        float sinA = Mathf.Sqrt(1f - cosA * cosA);
        Vector3 bend = pole - direction * Vector3.Dot(pole, direction);
        if (bend.sqrMagnitude < 1e-6f)
            bend = Vector3.Cross(direction, Vector3.up);
        bend.Normalize();

        Vector3 knee = hip + l1 * (direction * cosA + bend * sinA);
        upper.rotation = Quaternion.FromToRotation(lower.position - hip, knee - hip) * upper.rotation;

        Vector3 reach = hip + direction * d;
        lower.rotation = Quaternion.FromToRotation(foot.position - lower.position, reach - lower.position) * lower.rotation;
    }

    private bool FindBones()
    {
        if (searched)
            return hips != null;

        searched = true;
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            switch (t.name)
            {
                case "Hips": hips = t; break;
                case "Spine": spine = t; break;
                case "LeftUpperLeg": upperLeft = t; break;
                case "LeftLowerLeg": lowerLeft = t; break;
                case "LeftFoot": footLeft = t; break;
                case "RightUpperLeg": upperRight = t; break;
                case "RightLowerLeg": lowerRight = t; break;
                case "RightFoot": footRight = t; break;
            }
        }

        bool all = hips != null && spine != null && upperLeft != null && lowerLeft != null && footLeft != null && upperRight != null && lowerRight != null && footRight != null;
        if (!all)
        {
            Debug.LogWarning("UnitStance: the bones of the legs were not found on " + name + ", the crouch does nothing visible.");
            hips = null;
        }
        return all;
    }

    // The body collider (the capsule the mouse and the other units' bullets meet) gets lower, its bottom stays on the ground.
    private void UpdateCollider()
    {
        if (!colliderKnown)
        {
            Unit unit = GetComponent<Unit>();
            capsule = unit != null ? unit.Model.GetComponent<CapsuleCollider>() : null;
            if (capsule != null)
            {
                standingHeight = capsule.height;
                standingCenter = capsule.center;
            }
            colliderKnown = true;
        }
        if (capsule == null)
            return;

        float e = Amount * Amount * (3f - 2f * Amount);
        float height = Mathf.Lerp(standingHeight, standingHeight * colliderHeight, e);
        capsule.height = height;
        capsule.center = new Vector3(standingCenter.x, standingCenter.y - (standingHeight - height) * 0.5f, standingCenter.z);
    }
}
