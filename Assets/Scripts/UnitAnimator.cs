using UnityEngine;

/// <summary>
/// Procedural run animation for the little man. It does not control the unit, it only watches it:
///  - while the unit is moving, the legs and arms swing and the body bobs;
///  - the figure turns to face the direction of travel;
///  - when the unit stops, the limbs smoothly return to rest.
/// The swing is tied to the distance travelled, so the steps always match the speed.
/// Sits on the Model object (the part that turns); Body is the child that bobs.
/// </summary>
public class UnitAnimator : MonoBehaviour
{
    [Header("Parts")]
    [SerializeField] private Transform body;
    [SerializeField] private Transform leftArm;
    [SerializeField] private Transform rightArm;
    [SerializeField] private Transform leftLeg;
    [SerializeField] private Transform rightLeg;

    [Header("Run cycle")]
    [SerializeField] private float legSwing = 45f;      // degrees
    [SerializeField] private float armSwing = 50f;      // degrees
    [SerializeField] private float strideLength = 1.2f; // world units covered by one full cycle
    [SerializeField] private float bobHeight = 0.04f;   // world units
    [SerializeField] private float turnSpeed = 720f;    // degrees per second
    [SerializeField] private float blendSpeed = 8f;     // how fast the run fades in and out

    private const float MovingThreshold = 0.1f;   // world units per second
    private const float TeleportThreshold = 20f;  // faster than this is a jump (PlaceOn), not running

    private Vector3 lastPosition;
    private bool hasLastPosition;
    private float phase;
    private float weight;
    private Vector3 bodyRestPosition;
    private bool bodyRestStored;

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

        if (!bodyRestStored && body != null)
        {
            bodyRestPosition = body.localPosition;
            bodyRestStored = true;
        }

        Vector3 position = transform.position;
        Vector3 velocity = hasLastPosition ? (position - lastPosition) / dt : Vector3.zero;
        lastPosition = position;
        hasLastPosition = true;

        Vector3 flat = new Vector3(velocity.x, 0f, velocity.z);
        float speed = flat.magnitude;
        bool moving = speed > MovingThreshold && speed < TeleportThreshold;

        if (moving)
        {
            phase += speed * dt / Mathf.Max(0.01f, strideLength) * Mathf.PI * 2f;

            Quaternion target = Quaternion.LookRotation(flat / speed, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeed * dt);
        }

        weight = Mathf.MoveTowards(weight, moving ? 1f : 0f, blendSpeed * dt);
        Apply();
    }

    private void Apply()
    {
        float swing = Mathf.Sin(phase) * weight;

        SetSwing(leftLeg, swing * legSwing);
        SetSwing(rightLeg, -swing * legSwing);
        SetSwing(leftArm, -swing * armSwing);   // each arm swings against the leg on its side
        SetSwing(rightArm, swing * armSwing);

        if (body != null && bodyRestStored)
            body.localPosition = bodyRestPosition + Vector3.up * (Mathf.Abs(Mathf.Cos(phase)) * bobHeight * weight);
    }

    private static void SetSwing(Transform limb, float angle)
    {
        if (limb != null)
            limb.localRotation = Quaternion.Euler(angle, 0f, 0f);
    }
}
