using UnityEngine;

/// <summary>
/// Lets a unit lean out of cover to shoot. A unit that stands right behind something big has its bullets stopped by it; for a shot
/// the unit steps a little to the side (the body, the weapon and the camera move with it, the unit's tile does not change), far enough
/// that the line from the weapon to the target is free, and goes back to where it stood when the shot view is over.
///
/// FindOffset works out how far to the side (if at all): only when something solid is within NearDistance on the line to the target,
/// the smallest step to either side (the right one first) that clears it, never into something solid and never more than MaxShift.
/// PeekTo / Return move the body there and back smoothly (the model of the unit is moved, nothing else).
/// ShotCamera uses it when the shot view opens, when the shot is switched to another target and when the view closes.
/// </summary>
public class UnitPeek : MonoBehaviour
{
    /// <summary>Cover closer than this (world units, from the unit's middle) counts as 'right in front'.</summary>
    public const float NearDistance = 1.6f;

    /// <summary>The unit never steps out further than this.</summary>
    public const float MaxShift = 0.6f;

    private const float SearchStep = 0.05f;
    private const float Margin = 0.08f;          // a little further than just clearing the edge
    private const float BodyRadius = 0.2f;

    [Tooltip("How fast the unit steps out and back, in world units per second.")]
    [SerializeField, Min(0.1f)] private float speed = 3.5f;

    private Transform body;
    private Vector3 baseLocal;
    private bool baseKnown;
    private Vector3 offset;
    private Vector3 targetOffset;

    /// <summary>How far the unit is out of place right now (world space).</summary>
    public Vector3 Offset => offset;

    /// <summary>Where it is going (zero: back to its place).</summary>
    public Vector3 TargetOffset => targetOffset;

    /// <summary>True while it is stepping out or standing out.</summary>
    public bool IsOut => targetOffset.sqrMagnitude > 1e-6f || offset.sqrMagnitude > 1e-6f;

    public void PeekTo(Vector3 worldOffset)
    {
        targetOffset = worldOffset;
    }

    public void Return()
    {
        targetOffset = Vector3.zero;
    }

    /// <summary>Back in place at once.</summary>
    public void Snap()
    {
        targetOffset = Vector3.zero;
        offset = Vector3.zero;
        Apply();
    }

    private void LateUpdate()
    {
        Step(Time.unscaledDeltaTime);
    }

    /// <summary>One step of the movement (done every frame by itself).</summary>
    public void Step(float deltaTime)
    {
        if (!IsOut)
            return;

        offset = Vector3.MoveTowards(offset, targetOffset, speed * Mathf.Max(0f, deltaTime));
        Apply();
    }

    private void Apply()
    {
        Unit unit = GetComponent<Unit>();
        if (unit == null)
            return;

        if (body == null)
            body = unit.Model;
        if (body == null)
            return;

        if (!baseKnown)
        {
            baseLocal = body.localPosition;
            baseKnown = true;
        }
        body.localPosition = baseLocal + transform.InverseTransformVector(offset);
    }

    private void OnDestroy()
    {
        // a unit that loses this component is where it belongs
        if (baseKnown && body != null)
            body.localPosition = baseLocal;
    }

    /// <summary>
    /// How far (a world-space step to the side of the line to the target) the shooter must lean to have a free line to the point.
    /// Returns false, with a zero offset, when there is no need (nothing solid right in front), or when a step of up to MaxShift
    /// does not help (or has no room).
    /// </summary>
    public static bool FindOffset(Unit shooter, Vector3 targetPoint, out Vector3 offset)
    {
        offset = Vector3.zero;
        if (shooter == null)
            return false;

        Vector3 feet = shooter.transform.position;
        float height = Mathf.Max(0.6f, shooter.MuzzlePosition.y - feet.y);
        return Search(feet, height, shooter, null, targetPoint, out offset);
    }

    /// <summary>
    /// Can a bullet get from a unit standing at 'feet' (not necessarily where it stands now) to the point on the target? True if the
    /// line is free as it is (offset zero) or if the unit can lean to one side to make it free (offset says how far). Any other unit on
    /// the line counts as in the way (a unit does not shoot its friend). Used by the enemies to choose where to shoot from.
    /// </summary>
    public static bool CanClear(Vector3 feet, float muzzleHeight, Unit shooter, Unit target, Vector3 targetPoint, out Vector3 offset)
    {
        offset = Vector3.zero;
        float height = Mathf.Max(0.6f, muzzleHeight);
        if (FirstBlock(feet + Vector3.up * height, targetPoint, shooter, target) < 0f)
            return true;
        return Search(feet, height, shooter, target, targetPoint, out offset);
    }

    private static bool Search(Vector3 feet, float height, Unit shooter, Unit target, Vector3 targetPoint, out Vector3 offset)
    {
        offset = Vector3.zero;
        Vector3 origin = feet + Vector3.up * height;

        Vector3 flat = targetPoint - origin;
        flat.y = 0f;
        if (flat.sqrMagnitude < 0.01f)
            return false;
        flat.Normalize();
        Vector3 side = Vector3.Cross(Vector3.up, flat);          // to the right of the line to the target

        float blockedAt = FirstBlock(origin, targetPoint, shooter, target);
        if (blockedAt < 0f || blockedAt > NearDistance)
            return false;                                         // free, or the thing in the way is not right in front

        for (float s = SearchStep; s <= MaxShift + 1e-4f; s += SearchStep)
        {
            for (int k = 0; k < 2; k++)
            {
                float signed = k == 0 ? s : -s;
                Vector3 from = origin + side * signed;
                if (!Fits(feet + side * signed, shooter))
                    continue;
                if (FirstBlock(from, targetPoint, shooter, target) >= 0f)
                    continue;

                float total = Mathf.Min(MaxShift, s + Margin);
                if (!Fits(feet + side * (signed > 0f ? total : -total), shooter))
                    total = s;
                offset = side * (signed > 0f ? total : -total);
                return true;
            }
        }
        return false;
    }

    // The distance to the first solid thing on the line (not counting the ground and the shooter), or -1 if the line is free. A unit
    // on the line ends it: with no 'wanted' target that is not cover (-1); with one, only the wanted unit is free, any other is in the way.
    private static float FirstBlock(Vector3 from, Vector3 to, Unit shooter, Unit wanted)
    {
        Vector3 d = to - from;
        float distance = d.magnitude;
        if (distance < 1e-3f)
            return -1f;

        RaycastHit[] hits = Physics.RaycastAll(from, d / distance, distance, ~(1 << Bullet.BulletLayer), QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (RaycastHit hit in hits)
        {
            Collider c = hit.collider;
            Unit u = c.GetComponentInParent<Unit>();
            if (u == shooter)
                continue;
            if (u != null)
                return wanted != null && u != wanted ? hit.distance : -1f;
            if (c.GetComponentInParent<Tile>() != null || c.GetComponentInParent<RoofPick>() != null)
                continue;                                         // the ground
            return hit.distance;
        }
        return -1f;
    }

    // Is there room for the body at this place (nothing solid within a body's width around the middle of the body)?
    private static bool Fits(Vector3 feetPosition, Unit shooter)
    {
        Collider[] found = Physics.OverlapSphere(feetPosition + Vector3.up * 0.6f, BodyRadius, ~(1 << Bullet.BulletLayer), QueryTriggerInteraction.Ignore);
        foreach (Collider c in found)
        {
            if (c.GetComponentInParent<Unit>() != null)
                continue;
            if (c.GetComponentInParent<Tile>() != null || c.GetComponentInParent<RoofPick>() != null)
                continue;
            return false;
        }
        return true;
    }
}
