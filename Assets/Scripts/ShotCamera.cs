using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// The shot view: the game camera leaves its isometric place, flies to the shooting unit and looks at the enemy over the
/// shooter's shoulder (the view the shooter has of the enemy), with the enemy glowing, a ring of bullet spread around it
/// (SpreadRing) and the chance to hit next to the ring (HitChanceMeter: how much of the ring the enemy covers). On the way the picture changes smoothly from the flat orthographic view to a perspective one, and the
/// shooter raises its weapon and turns to the enemy.
///
/// A shooter standing right behind cover leans out to the side for the shot (UnitPeek) and steps back when the view is over.
///
/// Several enemies in view: the shot can be switched from one to another (SwitchTarget / CycleTarget; Tab, the arrow keys, a click on
/// an enemy or on its icon). The camera flies on to the new one, the ring opens up again and closes in on it.
///
/// Begin(shooter, target) starts it. It stays until End() is called (the Fire button does that, and so do Esc and the
/// right mouse button), then the camera flies back to exactly where it was. While it is on, the map marks, the vision
/// fans and the cover markers are hidden, the see-through scenery effect is paused, and ShotCamera.Active is true
/// (the unit selector ignores the mouse and keys).
/// </summary>
public class ShotCamera : MonoBehaviour
{
    private enum State { Idle, MovingIn, Holding, MovingOut }

    [SerializeField] private Camera gameCamera;
    [Tooltip("The isometric camera script; it is paused while the shot view is on.")]
    [SerializeField] private IsometricCamera isometric;
    [SerializeField] private GridManager grid;

    [Header("Where the camera goes (relative to the shooter)")]
    [Tooltip("Height of the camera above the feet of the shooter, in world units (a unit is 1 tall).")]
    [SerializeField, Min(0f)] private float eyeHeight = 1.4f;
    [Tooltip("How far behind the shooter, along the line to the enemy.")]
    [SerializeField, Min(0f)] private float distanceBehind = 1.7f;
    [Tooltip("How far to the right of the shooter (over the shoulder).")]
    [SerializeField] private float shoulderOffset = 0.7f;
    [Tooltip("Where on the enemy the camera looks, above its feet.")]
    [SerializeField] private float targetHeight = 0.55f;
    [SerializeField, Range(20f, 90f)] private float fieldOfView = 48f;

    [Header("Timing")]
    [SerializeField, Min(0.05f)] private float moveTime = 0.7f;
    [SerializeField, Min(0.05f)] private float returnTime = 0.55f;

    private State state = State.Idle;
    private Unit shooter;
    private Unit target;
    private float progress;                      // 0 = isometric view, 1 = shot view
    private float holdTime;                      // how long the camera has been in place

    private Vector3 savedPosition;
    private Quaternion savedRotation;
    private float savedOrthoSize;
    private float savedNear, savedFar;
    private Vector3 shotPosition;
    private Quaternion shotRotation;

    private readonly List<MeshRenderer> hiddenRenderers = new List<MeshRenderer>();
    private MeshRenderer overlayRenderer;
    private bool overlayWasOn;
    private CoverMarkers coverMarkers;
    private OcclusionFader fader;
    private SpreadRing ring;
    private UnitHighlight highlight;
    private HitChanceMeter meter;
    private float chance;
    private float measureTimer;
    private UnitHitbox hitbox;
    private bool shotFired;
    private Vector3 peekOffset;                  // how far the shooter leans out of cover for this shot

    // the enemies the shooter could see when the view began, from left to right: the shot can be switched between them
    private readonly List<Unit> targets = new List<Unit>();
    [SerializeField, Min(0.05f)] private float switchTime = 0.5f;
    private float switchProgress = 1f;           // 1 = not switching
    private Vector3 switchFromPosition;
    private Quaternion switchFromRotation;
    private float afterShotTimer = -1f;

    // After a shot has landed the view stays a moment (to see what happened). Then it closes by itself if there is nothing more to
    // do here: the target is dead, or the shooter cannot pay for another shot.
    private const float AfterShotDelay = 1.1f;

    /// <summary>True from the moment the shot view starts until the camera is back: the game ignores orders meanwhile.</summary>
    public static bool Active { get; private set; }

    /// <summary>The camera is in the shot view (or on its way there).</summary>
    public bool IsOn => state == State.MovingIn || state == State.Holding;

    /// <summary>The camera has arrived and stays: now it can be sent back.</summary>
    public bool IsHolding => state == State.Holding;

    public Unit Shooter => shooter;
    public Unit Target => target;

    /// <summary>The enemies the shot can be switched between, from left to right as the shooter sees them (empty when the view is off).</summary>
    public IReadOnlyList<Unit> Targets => targets;

    /// <summary>Place of the current target in Targets (-1 if none).</summary>
    public int TargetIndex => targets.IndexOf(target);

    /// <summary>True if the shot can be switched to this unit (it was in the shooter's view when the shot view began).</summary>
    public bool IsTarget(Unit unit)
    {
        return unit != null && targets.Contains(unit);
    }

    /// <summary>The camera is flying from one target to the next.</summary>
    public bool IsSwitching => switchProgress < 1f;

    /// <summary>Raised when the shot was switched to another target.</summary>
    public event System.Action TargetChanged;

    /// <summary>The ring of bullet spread of the shot that is on now (null when there is none).</summary>
    public SpreadRing Ring => ring;

    /// <summary>The chance to hit (0 to 1) of the shot that is on now: the share of the ring covered by the enemy.</summary>
    public float HitChance => chance;

    private void OnDisable()
    {
        if (state != State.Idle)
            Finish();
    }

    private void Update()
    {
        Tick(Time.unscaledDeltaTime);
    }

    /// <summary>Sends the camera to the shooter. Returns false when it is already busy or something is missing.</summary>
    public bool Begin(Unit shootingUnit, Unit targetUnit)
    {
        if (state != State.Idle || shootingUnit == null || targetUnit == null)
            return false;

        Camera cam = GameCamera();
        if (cam == null)
            return false;

        shooter = shootingUnit;
        target = targetUnit;
        CollectTargets();
        switchProgress = 1f;

        savedPosition = cam.transform.position;
        savedRotation = cam.transform.rotation;
        savedOrthoSize = cam.orthographicSize;
        savedNear = cam.nearClipPlane;
        savedFar = cam.farClipPlane;
        cam.nearClipPlane = 0.1f;

        if (isometric == null)
            isometric = cam.GetComponent<IsometricCamera>();
        if (isometric != null)
            isometric.enabled = false;

        HideClutter();

        // the shooter turns to the enemy and raises the weapon
        UnitAnimator animator = shooter.GetComponentInChildren<UnitAnimator>();
        if (animator != null)
        {
            animator.AimAt(target.transform.position);
            animator.StartAiming();
        }

        UpdatePeek();
        ComputeShotPose(out shotPosition, out shotRotation);

        // the ring of bullet spread around the enemy, and the enemy itself glows
        GameObject ringObject = new GameObject("SpreadRing");
        ringObject.hideFlags = HideFlags.DontSave;
        ringObject.transform.SetParent(transform, false);
        ring = ringObject.AddComponent<SpreadRing>();
        ring.Show(shooter, target);
        highlight = target.gameObject.AddComponent<UnitHighlight>();
        highlight.Show();
        holdTime = 0f;

        GameObject meterObject = new GameObject("HitChanceMeter");
        meterObject.hideFlags = HideFlags.DontSave;
        meterObject.transform.SetParent(transform, false);
        meter = meterObject.AddComponent<HitChanceMeter>();
        chance = 0f;
        measureTimer = 0f;
        shotFired = false;
        afterShotTimer = -1f;

        progress = 0f;
        state = State.MovingIn;
        Active = true;
        return true;
    }

    /// <summary>The shot view is on, the camera is in place and the last bullet is gone: a bullet can be fired.</summary>
    public bool CanShoot => state == State.Holding && ring != null && !Bullet.AnyInFlight && shooter != null && target != null && target.IsAlive && shooter.CanAffordShot && !IsSwitching;

    /// <summary>The bullet that was fired last (null if none yet in this shot view).</summary>
    public Bullet LastBullet { get; private set; }

    /// <summary>The point the last bullet was aimed at (a random point inside the ring).</summary>
    public Vector3 LastAimPoint { get; private set; }

    /// <summary>
    /// Fires one bullet: from the muzzle of the shooter's weapon to a random point inside the ring (as big as it is now).
    /// The bullet is a real body: whatever it meets on the way takes the hit instead of the target. The target's collider is
    /// made in the exact shape of the body in its present pose. Returns false when a shot is not possible just now.
    /// </summary>
    public bool Shoot()
    {
        if (!CanShoot || shooter == null || target == null)
            return false;

        // the points are paid when the bullet leaves
        if (!shooter.TrySpendActionPoints(shooter.ShotCost))
            return false;
        shotFired = true;
        afterShotTimer = -1f;

        hitbox = target.GetComponent<UnitHitbox>();
        if (hitbox == null)
            hitbox = target.gameObject.AddComponent<UnitHitbox>();
        hitbox.Build();

        UnitAnimator animator = shooter.GetComponentInChildren<UnitAnimator>();
        if (animator != null)
            animator.PlayShoot();

        LastAimPoint = ring.RandomPointInRing();
        LastBullet = Bullet.Fire(shooter, target, shooter.MuzzlePosition, LastAimPoint, shooter.WeaponDamage);
        return true;
    }

    /// <summary>
    /// Like Begin, but only if the shooter can really shoot at this enemy right now: it is alive, an enemy, in the shooter's field of
    /// view, the shooter has the points for a shot and nobody is moving. Used by clicks on an enemy and on its icon.
    /// </summary>
    public bool TryBegin(Unit shootingUnit, Unit targetUnit)
    {
        if (state != State.Idle || shootingUnit == null || targetUnit == null)
            return false;
        if (!shootingUnit.IsAlive || !shootingUnit.CanAffordShot || Unit.AnyMoving)
            return false;
        if (!targetUnit.IsAlive || targetUnit.Faction == shootingUnit.Faction)
            return false;

        if (grid == null)
            grid = FindFirstObjectByType<GridManager>();
        if (grid == null || !grid.CanSee(shootingUnit, targetUnit))
            return false;

        return Begin(shootingUnit, targetUnit);
    }

    /// <summary>
    /// Switches the shot to another of the enemies in Targets: the old one stops glowing, the new one glows, the camera flies to the
    /// shooter's view of it, the ring opens up again and closes in on it. Not possible while a bullet is in the air, while the camera
    /// is still flying, or for an enemy that was not in view when the shot view began. Returns whether it was done.
    /// </summary>
    public bool SwitchTarget(Unit newTarget)
    {
        if (state != State.Holding || IsSwitching || Bullet.AnyInFlight || shooter == null)
            return false;
        if (newTarget == null || newTarget == target || !newTarget.IsAlive || !targets.Contains(newTarget))
            return false;

        // what belonged to the old target
        if (hitbox != null)
        {
            hitbox.Clear();
            if (Application.isPlaying) Destroy(hitbox); else DestroyImmediate(hitbox);
            hitbox = null;
        }
        if (highlight != null)
        {
            highlight.Hide();
            if (Application.isPlaying) Destroy(highlight); else DestroyImmediate(highlight);
            highlight = null;
        }

        target = newTarget;
        targets.RemoveAll(u => u == null || !u.IsAlive);        // the dead are no longer to choose from
        highlight = target.gameObject.AddComponent<UnitHighlight>();
        highlight.Show();

        UnitAnimator animator = shooter.GetComponentInChildren<UnitAnimator>();
        if (animator != null)
            animator.AimAt(target.transform.position);

        // the ring starts again around the new enemy
        ring.Show(shooter, target);
        holdTime = 0f;
        chance = 0f;
        measureTimer = 0f;
        shotFired = false;
        afterShotTimer = -1f;

        // and the camera flies from where it is to the new place
        Camera cam = GameCamera();
        switchFromPosition = cam.transform.position;
        switchFromRotation = cam.transform.rotation;
        UpdatePeek();
        ComputeShotPose(out shotPosition, out shotRotation);
        switchProgress = 0f;

        TargetChanged?.Invoke();
        return true;
    }

    /// <summary>Switches to the next enemy to the right (step 1) or to the left (step -1), round and round. Returns whether it was done.</summary>
    public bool CycleTarget(int step)
    {
        if (targets.Count < 2 || step == 0)
            return false;

        int index = targets.IndexOf(target);
        int direction = step > 0 ? 1 : -1;
        for (int n = 1; n <= targets.Count; n++)
        {
            Unit candidate = targets[((index + direction * n) % targets.Count + targets.Count) % targets.Count];
            if (candidate != null && candidate != target && candidate.IsAlive)
                return SwitchTarget(candidate);
        }
        return false;
    }

    // A shooter who stands right behind cover leans out to the side for the shot, so the bullets do not hit the cover.
    private void UpdatePeek()
    {
        peekOffset = Vector3.zero;
        if (shooter == null || target == null)
            return;

        UnitPeek peek = shooter.GetComponent<UnitPeek>();
        Vector3 chest = target.transform.position + Vector3.up * (targetHeight - target.StanceChestDrop);
        if (UnitPeek.FindOffset(shooter, chest, out Vector3 offset))
        {
            if (peek == null)
                peek = shooter.gameObject.AddComponent<UnitPeek>();
            peek.PeekTo(offset);
            peekOffset = offset;
        }
        else if (peek != null)
        {
            peek.Return();
        }
    }

    // The shot view is over: the shooter steps back to where he stood.
    private void ReleasePeek()
    {
        peekOffset = Vector3.zero;
        if (shooter == null)
            return;

        UnitPeek peek = shooter.GetComponent<UnitPeek>();
        if (peek != null)
            peek.Return();
    }

    // Every enemy the shooter sees now, from left to right (the angle from the way the shooter faces).
    private void CollectTargets()
    {
        targets.Clear();
        if (grid == null)
            grid = FindFirstObjectByType<GridManager>();

        foreach (Unit u in Unit.All)
        {
            if (u == null || !u.IsAlive || u.Faction == shooter.Faction)
                continue;
            if (u == target || (grid != null && grid.CanSee(shooter, u)))
                targets.Add(u);
        }

        Vector3 forward = shooter.FacingDirection;
        Unit self = shooter;
        targets.Sort((a, b) => SideAngle(forward, a.transform.position - self.transform.position).CompareTo(SideAngle(forward, b.transform.position - self.transform.position)));
    }

    private static float SideAngle(Vector3 forward, Vector3 toUnit)
    {
        toUnit.y = 0f;
        return Vector3.SignedAngle(forward, toUnit, Vector3.up);       // negative: to the left, positive: to the right
    }

    /// <summary>Sends the camera back to where it was.</summary>
    public void End()
    {
        if (state == State.MovingIn || state == State.Holding)
            state = State.MovingOut;
    }

    /// <summary>One step of the flight (done every frame by itself).</summary>
    public void Tick(float deltaTime)
    {
        switch (state)
        {
            case State.MovingIn:
                progress = Mathf.Min(1f, progress + deltaTime / moveTime);
                Apply(progress);
                UpdateEffects();
                if (progress >= 1f)
                    state = State.Holding;
                break;

            case State.Holding:
                holdTime += deltaTime;
                UpdateEffects();
                UpdateAfterShot(deltaTime);
                UpdateSwitch(deltaTime);
                HandleTargetInput();
                if (shooter == null || target == null || !shooter.IsAlive || ExitPressed())
                    state = State.MovingOut;
                break;

            case State.MovingOut:
                ReleasePeek();
                progress = Mathf.Max(0f, progress - deltaTime / returnTime);
                Apply(progress);
                UpdateEffects();
                if (progress <= 0f)
                    Finish();
                break;
        }
    }

    private void UpdateAfterShot(float deltaTime)
    {
        if (!shotFired || Bullet.AnyInFlight)
        {
            afterShotTimer = -1f;
            return;
        }

        if (afterShotTimer < 0f)
        {
            afterShotTimer = AfterShotDelay;
            return;
        }

        afterShotTimer -= deltaTime;
        if (afterShotTimer > 0f)
            return;

        shotFired = false;
        afterShotTimer = -1f;
        if (shooter == null || !shooter.CanAffordShot)
        {
            state = State.MovingOut;
            return;
        }

        // the target is dead: on to the nearest of the others, if there is one; otherwise there is nothing more to do here
        if (target == null || !target.IsAlive)
        {
            Unit next = NearestOtherTarget();
            if (next == null || !SwitchTarget(next))
                state = State.MovingOut;
        }
    }

    private Unit NearestOtherTarget()
    {
        Unit best = null;
        float bestDistance = float.PositiveInfinity;
        foreach (Unit u in targets)
        {
            if (u == null || u == target || !u.IsAlive)
                continue;
            float d = (u.transform.position - shooter.transform.position).sqrMagnitude;
            if (d < bestDistance)
            {
                bestDistance = d;
                best = u;
            }
        }
        return best;
    }

    // The camera flies from one target to the next.
    private void UpdateSwitch(float deltaTime)
    {
        if (switchProgress >= 1f)
            return;

        switchProgress = Mathf.Min(1f, switchProgress + deltaTime / switchTime);
        float e = switchProgress * switchProgress * (3f - 2f * switchProgress);
        Camera cam = GameCamera();
        cam.transform.SetPositionAndRotation(Vector3.Lerp(switchFromPosition, shotPosition, e), Quaternion.Slerp(switchFromRotation, shotRotation, e));
    }

    // Tab / Right arrow: the next enemy to the right; Shift+Tab / Left arrow: the next to the left; a click on an enemy: that one.
    private void HandleTargetInput()
    {
        if (IsSwitching || Bullet.AnyInFlight || targets.Count < 2)
            return;

        int step = ReadStep();
        if (step != 0)
        {
            CycleTarget(step);
            return;
        }

        if (!TryGetClick(out Vector2 screenPos))
            return;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        Camera cam = GameCamera();
        if (cam != null && Physics.Raycast(cam.ScreenPointToRay(screenPos), out RaycastHit hit, 300f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            Unit clicked = hit.collider.GetComponentInParent<Unit>();
            if (clicked != null && clicked != shooter)
                SwitchTarget(clicked);
        }
    }

    private static int ReadStep()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard kb = Keyboard.current;
        if (kb != null)
        {
            bool shift = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            if (kb.rightArrowKey.wasPressedThisFrame || (kb.tabKey.wasPressedThisFrame && !shift))
                return 1;
            if (kb.leftArrowKey.wasPressedThisFrame || (kb.tabKey.wasPressedThisFrame && shift))
                return -1;
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        bool legacyShift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        if (Input.GetKeyDown(KeyCode.RightArrow) || (Input.GetKeyDown(KeyCode.Tab) && !legacyShift))
            return 1;
        if (Input.GetKeyDown(KeyCode.LeftArrow) || (Input.GetKeyDown(KeyCode.Tab) && legacyShift))
            return -1;
#endif
        return 0;
    }

    private static bool TryGetClick(out Vector2 screenPos)
    {
        screenPos = default;
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            screenPos = Mouse.current.position.ReadValue();
            return true;
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetMouseButtonDown(0))
        {
            screenPos = Input.mousePosition;
            return true;
        }
#endif
        return false;
    }

    private Camera GameCamera()
    {
        if (gameCamera == null)
            gameCamera = Camera.main;
        return gameCamera;
    }

    // Puts the camera on its way between the isometric view (k = 0) and the shot view (k = 1).
    private void Apply(float k)
    {
        Camera cam = GameCamera();
        float e = k * k * (3f - 2f * k);                                   // smooth start and stop

        cam.transform.SetPositionAndRotation(Vector3.Lerp(savedPosition, shotPosition, e), Quaternion.Slerp(savedRotation, shotRotation, e));

        if (k >= 1f)
        {
            // arrived: an ordinary perspective camera
            cam.ResetProjectionMatrix();
            cam.orthographic = false;
            cam.fieldOfView = fieldOfView;
            return;
        }

        // on the way: the projection is a blend of the flat and the perspective one
        cam.orthographic = true;
        float aspect = cam.aspect;
        Matrix4x4 ortho = Matrix4x4.Ortho(-savedOrthoSize * aspect, savedOrthoSize * aspect, -savedOrthoSize, savedOrthoSize, cam.nearClipPlane, savedFar);
        Matrix4x4 perspective = Matrix4x4.Perspective(fieldOfView, aspect, cam.nearClipPlane, savedFar);
        Matrix4x4 blend = new Matrix4x4();
        for (int i = 0; i < 16; i++)
            blend[i] = Mathf.Lerp(ortho[i], perspective[i], e);
        cam.projectionMatrix = blend;
    }

    // Behind the shooter's right shoulder, looking at the chest of the enemy.
    private void ComputeShotPose(out Vector3 position, out Quaternion rotation)
    {
        Vector3 feet = shooter.transform.position;
        Vector3 towards = target.transform.position - feet;
        towards.y = 0f;
        if (towards.sqrMagnitude < 0.01f)
            towards = shooter.FacingDirection;
        towards.Normalize();

        Vector3 right = Vector3.Cross(Vector3.up, towards);
        Vector3 head = feet + peekOffset + Vector3.up * (eyeHeight - shooter.StanceChestDrop);      // a crouching shooter's eyes are lower
        // Leaning out of cover: the camera goes behind the leaning weapon, on the side of the lean (not on the right shoulder as usual),
        // so that it sees the enemy past the cover like the bullets do. Otherwise the cover would be in the camera's way and the chance
        // to hit, which is worked out from here, would be wrong.
        float shoulder = shoulderOffset;
        float lean = Vector3.Dot(peekOffset, right);
        if (Mathf.Abs(lean) > 0.01f)
            shoulder = Mathf.Sign(lean) * Mathf.Min(Mathf.Abs(shoulderOffset), 0.35f);
        position = head - towards * distanceBehind + right * shoulder;

        // not inside a wall: pull the camera in if something solid is in the way (a collider, or a tall building)
        RaycastHit hit;
        if (Physics.Linecast(head, position, out hit, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
            && hit.collider.GetComponentInParent<Unit>() != shooter)
            position = hit.point + (head - position).normalized * 0.12f;

        if (grid == null)
            grid = FindFirstObjectByType<GridManager>();
        Vector3 back = position - head;
        int steps = Mathf.Max(1, Mathf.CeilToInt(back.magnitude / 0.1f));
        Vector3 last = head;
        for (int i = 1; i <= steps; i++)
        {
            Vector3 p = head + back * (i / (float)steps);
            if (InsideBuilding(p))
            {
                position = last;                              // stop just before the wall
                break;
            }
            last = p;
        }

        Vector3 look = target.transform.position + Vector3.up * (targetHeight - target.StanceChestDrop);
        rotation = Quaternion.LookRotation(look - position, Vector3.up);
    }

    // The ring, the number and the glow follow the flight: they fade in as the camera arrives and out as it leaves.
    // The chance to hit is measured a few times a second while the ring is closing in.
    private void UpdateEffects()
    {
        Camera cam = GameCamera();
        float visibility = Mathf.Clamp01((progress - 0.45f) / 0.55f);

        if (ring != null)
            ring.Tick(visibility, holdTime, cam);
        if (highlight != null)
            highlight.Pulse(Time.unscaledTime, Mathf.Clamp01(progress * 1.6f));

        if (ring != null && meter != null && visibility > 0.3f)
        {
            measureTimer -= Time.unscaledDeltaTime;
            if (measureTimer <= 0f)
            {
                measureTimer = 0.1f;
                chance = meter.Measure(shooter, target, cam.transform.position, ring.Center, ring.BaseRadius, ring.RingRenderer, ring.LabelRenderer);
                ring.SetChance(chance);
            }
        }
    }

    private void RemoveEffects()
    {
        if (hitbox != null)
        {
            hitbox.Clear();
            if (Application.isPlaying) Destroy(hitbox); else DestroyImmediate(hitbox);
            hitbox = null;
        }

        if (meter != null)
        {
            meter.Dispose();
            if (Application.isPlaying) Destroy(meter.gameObject); else DestroyImmediate(meter.gameObject);
            meter = null;
        }
        chance = 0f;

        if (ring != null)
        {
            ring.Hide();
            ring.Dispose();
            if (Application.isPlaying) Destroy(ring.gameObject); else DestroyImmediate(ring.gameObject);
            ring = null;
        }

        if (highlight != null)
        {
            highlight.Hide();
            if (Application.isPlaying) Destroy(highlight); else DestroyImmediate(highlight);
            highlight = null;
        }

        holdTime = 0f;
    }

    // Is this point inside a building (on the field, under a tall thing that blocks the view and reaches above the point)?
    private bool InsideBuilding(Vector3 point)
    {
        if (grid == null)
            return false;

        Tile origin = grid.GetTile(Vector2Int.zero);
        if (origin == null)
            return false;

        Vector3 d = point - origin.transform.position;
        Tile tile = grid.GetTile(new Vector2Int(Mathf.RoundToInt(d.x / grid.TileSize), Mathf.RoundToInt(d.z / grid.TileSize)));
        return tile != null && tile.BlocksSight && tile.BlockTopY > point.y;
    }

    // Everything that is only for the isometric view goes out of sight, and comes back with the camera.
    private void HideClutter()
    {
        if (grid == null)
            grid = FindFirstObjectByType<GridManager>();

        overlayRenderer = grid != null && grid.Overlay != null ? grid.Overlay.GetComponent<MeshRenderer>() : null;
        overlayWasOn = overlayRenderer != null && overlayRenderer.enabled;
        if (overlayRenderer != null)
            overlayRenderer.enabled = false;

        hiddenRenderers.Clear();
        foreach (UnitVisionDisplay fan in FindObjectsByType<UnitVisionDisplay>(FindObjectsSortMode.None))
        {
            MeshRenderer r = fan.GetComponent<MeshRenderer>();
            if (r != null && r.enabled)
            {
                r.enabled = false;
                hiddenRenderers.Add(r);
            }
        }

        coverMarkers = FindFirstObjectByType<CoverMarkers>();
        if (coverMarkers != null)
            coverMarkers.enabled = false;

        fader = FindFirstObjectByType<OcclusionFader>();
        if (fader != null)
            fader.enabled = false;
    }

    private void ShowClutter()
    {
        if (overlayRenderer != null && overlayWasOn)
            overlayRenderer.enabled = true;

        foreach (MeshRenderer r in hiddenRenderers)
            if (r != null)
                r.enabled = true;
        hiddenRenderers.Clear();

        if (coverMarkers != null)
            coverMarkers.enabled = true;
        if (fader != null)
            fader.enabled = true;
    }

    // The camera is back: exactly the old view.
    private void Finish()
    {
        RemoveEffects();

        Camera cam = GameCamera();
        if (cam != null)
        {
            cam.ResetProjectionMatrix();
            cam.orthographic = true;
            cam.orthographicSize = savedOrthoSize;
            cam.nearClipPlane = savedNear;
            cam.farClipPlane = savedFar;
            cam.transform.SetPositionAndRotation(savedPosition, savedRotation);
        }

        if (isometric != null)
            isometric.enabled = true;

        if (shooter != null)
        {
            UnitAnimator animator = shooter.GetComponentInChildren<UnitAnimator>();
            if (animator != null)
                animator.StopAiming();
        }

        ShowClutter();
        ReleasePeek();
        targets.Clear();
        switchProgress = 1f;
        shooter = null;
        target = null;
        state = State.Idle;
        progress = 0f;
        Active = false;
    }

    private static bool ExitPressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            return true;
        if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame)
            return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
            return true;
#endif
        return false;
    }
}
