using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A physical bullet: a small body with a collider that really flies (a Rigidbody with a velocity, no gravity, continuous collision
/// detection so that it cannot jump through thin things) from the muzzle of the weapon towards the point it was aimed at.
/// What it meets first decides what happens, and then it is gone:
///  - a unit: the unit takes damage (the target, or anybody else who stood in the way);
///  - something with health (a car, a crate, a wall, a palm...): that thing takes damage, and breaks if it has none left;
///  - the ground or anything else: nothing, the bullet is simply stopped.
/// The shooter cannot be hit by his own bullet, and the bullets of one volley do not collide with each other (BulletLayer).
/// The target is hit by the exact shape of its body (UnitHitbox), not by its fat capsule.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class Bullet : MonoBehaviour
{
    public enum Result { InFlight, HitTarget, HitUnit, HitObstacle, HitGround, Expired }

    /// <summary>
    /// The layer of all bullets. Bullets of one volley must not collide with each other: a whole layer is told to ignore itself
    /// (once), which is far cheaper and safer than one 'ignore' entry for every pair of bullets.
    /// </summary>
    public const int BulletLayer = 31;

    private static readonly List<Bullet> flying = new List<Bullet>();

    static Bullet()
    {
        Physics.IgnoreLayerCollision(BulletLayer, BulletLayer, true);
    }

    /// <summary>True while any bullet is in the air.</summary>
    public static bool AnyInFlight => flying.Count > 0;

    /// <summary>How many bullets ended each way since the game started (index = Result), for checking.</summary>
    public static readonly int[] Counts = new int[6];

    /// <summary>What the last bullet hit (the name of the object), for checking.</summary>
    public static string LastHit { get; private set; } = "";

    /// <summary>How the last bullet ended, for checking.</summary>
    public static Result LastResult { get; private set; } = Result.InFlight;

    private Unit shooter;
    private Unit target;
    private int damage;
    private float lifetime = 2.2f;            // 2.2 s at 40 per second is 88 units: more than the field is wide
    private float age;
    private Vector3 start;
    private Collider ownCollider;
    private bool resolved;

    public Result Outcome { get; private set; } = Result.InFlight;

    /// <summary>The thing the bullet hit (null while it flies, or when it ran out of range).</summary>
    public Collider HitCollider { get; private set; }

    /// <summary>
    /// Fires a bullet from the muzzle to the aim point. The speed is in world units per second (a tile is 1).
    /// </summary>
    public static Bullet Fire(Unit shootingUnit, Unit targetUnit, Vector3 muzzle, Vector3 aimPoint, int bulletDamage, float speed = 40f)
    {
        Vector3 direction = aimPoint - muzzle;
        if (direction.sqrMagnitude < 1e-6f)
            direction = shootingUnit != null ? shootingUnit.FacingDirection : Vector3.forward;
        direction.Normalize();

        GameObject go = new GameObject("Bullet");
        go.transform.SetPositionAndRotation(muzzle, Quaternion.LookRotation(direction));
        go.layer = BulletLayer;

        // a very thin body (2 mm; every extra millimetre around a soldier's long outline adds to the hits): the ring and its percentage are about a line through the target; a fat bullet would hit more often than the number says
        // (what you see - the glowing capsule - is only a picture and has no collider)
        SphereCollider collider = go.AddComponent<SphereCollider>();
        collider.radius = 0.002f;
        // physics also reports a touch when two bodies are within their 'contact offset' (1 cm by default): that would be a fat bullet again
        collider.contactOffset = 0.0001f;

        Bullet bullet = go.AddComponent<Bullet>();
        bullet.shooter = shootingUnit;
        bullet.target = targetUnit;
        bullet.damage = bulletDamage;
        bullet.start = muzzle;
        bullet.ownCollider = collider;

        Rigidbody body = go.GetComponent<Rigidbody>();
        body.useGravity = false;
        body.mass = 0.01f;
        body.linearDamping = 0f;
        body.angularDamping = 0f;
        body.constraints = RigidbodyConstraints.FreezeRotation;
        body.interpolation = RigidbodyInterpolation.None;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.linearVelocity = direction * speed;

        bullet.BuildLook();
        bullet.IgnoreFriends();
        flying.Add(bullet);

        // the shot is heard by the other side if it is near enough (NoiseSystem)
        NoiseSystem.Emit(NoiseKind.Shot, shootingUnit, muzzle);
        return bullet;
    }

    // What the bullet looks like: a glowing little capsule and a short trail.
    private void BuildLook()
    {
        GameObject tracer = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        tracer.name = "Tracer";
        DestroyImmediate(tracer.GetComponent<Collider>());                  // the bullet has its own, and a second one would be part of the Rigidbody
        tracer.transform.SetParent(transform, false);
        tracer.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);       // the capsule lies along the flight
        tracer.transform.localScale = new Vector3(0.05f, 0.13f, 0.05f);

        Renderer r = tracer.GetComponent<Renderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        Material m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        m.SetColor("_BaseColor", new Color(1f, 0.82f, 0.35f));
        r.sharedMaterial = m;

        TrailRenderer trail = gameObject.AddComponent<TrailRenderer>();
        trail.time = 0.14f;
        trail.startWidth = 0.05f;
        trail.endWidth = 0f;
        trail.numCapVertices = 2;
        trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        trail.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
        trail.startColor = new Color(1f, 0.85f, 0.4f, 0.9f);
        trail.endColor = new Color(1f, 0.55f, 0.15f, 0f);
        trail.Clear();
    }

    // Nothing the bullet should not touch: the shooter and the fat capsule of the target (its body shape is used). The other bullets: see BulletLayer.
    private void IgnoreFriends()
    {
        if (shooter != null)
            IgnoreUnit(shooter, false);
        if (target != null && target != shooter)
            IgnoreUnit(target, true);
    }

    private void IgnoreUnit(Unit unit, bool keepHitbox)
    {
        UnitHitbox hitbox = unit.GetComponent<UnitHitbox>();
        foreach (Collider c in unit.GetComponentsInChildren<Collider>())
        {
            if (c == null || c.isTrigger)
                continue;
            if (keepHitbox && hitbox != null && hitbox.Root != null && c.transform.IsChildOf(hitbox.Root))
                continue;                                                         // the exact body shape stays solid for the bullet
            Physics.IgnoreCollision(ownCollider, c, true);
        }
    }

    private void FixedUpdate()
    {
        if (resolved)
            return;

        age += Time.fixedDeltaTime;
        if (age > lifetime || (transform.position - start).sqrMagnitude > 90f * 90f)
            Finish(Result.Expired, null, transform.position);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (resolved)
            return;

        Vector3 point = collision.contactCount > 0 ? collision.GetContact(0).point : transform.position;
        Collider other = collision.collider;

        Unit unit = other.GetComponentInParent<Unit>();
        if (unit != null)
        {
            unit.TakeDamage(damage, transform.forward);
            Finish(unit == target ? Result.HitTarget : Result.HitUnit, other, point);
            return;
        }

        Destructible thing = other.GetComponentInParent<Destructible>();
        if (thing != null)
        {
            thing.Damage(damage, point);
            Finish(Result.HitObstacle, other, point);
            return;
        }

        Finish(Result.HitGround, other, point);
    }

    private void Finish(Result result, Collider hit, Vector3 point)
    {
        resolved = true;
        Outcome = result;
        HitCollider = hit;
        LastResult = result;
        LastHit = hit != null ? hit.transform.root == hit.transform ? hit.name : hit.transform.root.name + "/" + hit.name : "";
        Counts[(int)result]++;
        flying.Remove(this);

        if (result != Result.Expired)
            ImpactEffect.Spark(point, new Color(1f, 0.8f, 0.35f));

        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        flying.Remove(this);
    }
}
