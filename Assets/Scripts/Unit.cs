using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A unit standing on a tile of the field. Can be selected / deselected and driven to another tile
/// along a route that goes around walls.
///
/// Action points: for cost purposes the unit cannot move diagonally, so every tile of the route costs
/// 1 point (a ladder costs the field's ladder cost). While running it still cuts corners diagonally when that is possible; such a diagonal hop
/// is charged as the 2 steps it replaces. With 0 action points the unit cannot move.
/// </summary>
public class Unit : MonoBehaviour
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"); // URP / HDRP
    private static readonly int ColorId = Shader.PropertyToID("_Color");         // Built-in

    [SerializeField] private Vector2Int coords;

    [Header("Faction")]
    [SerializeField] private Faction faction = Faction.Player;

    [SerializeField] private Color selectedColor = new Color(1f, 0.9f, 0.1f, 1f);
    [Tooltip("Parts that change colour when the unit is selected. Empty = the whole unit.")]
    [SerializeField] private Renderer[] selectionRenderers;
    [Tooltip("Which materials of those renderers change colour (e.g. only the shirt). Empty = all of them.")]
    [SerializeField] private int[] selectionMaterialIndices;
    [SerializeField, Min(0.1f)] private float moveSpeed = 4f; // world units per second

    [Header("Vision")]
    [Tooltip("Width of the field of view in degrees (the whole cone, not half of it).")]
    [SerializeField, Range(1f, 360f)] private float viewAngle = 30f;
    [Tooltip("How far the unit sees, in tiles.")]
    [SerializeField, Min(0f)] private float viewRange = 5f;

    [Header("Shooting")]
    [Tooltip("How well the unit aims: the half-angle (in degrees) of the cone its bullets land in. The spread ring in the shot view is distance x tan(this) wide. Smaller = more accurate.")]
    [SerializeField, Range(0.1f, 20f)] private float aimSpread = 3f;
    [Tooltip("How much health a bullet of this unit takes away from whatever it hits.")]
    [SerializeField, Min(1)] private int weaponDamage = 30;
    [Tooltip("The end of the barrel: bullets start here. (An empty object on the weapon.)")]
    [SerializeField] private Transform muzzle;
    [Tooltip("Action points one shot costs.")]
    [SerializeField, Min(0)] private int shotCost = 2;

    [Header("Weapon")]
    [Tooltip("The weapon this unit carries (a WeaponData asset): its accuracy, damage, bullet speed and ways of firing. With a weapon here, the Aim Spread, Weapon Damage and Shot Cost above are not used.")]
    [SerializeField] private WeaponData weapon;
    [Tooltip("The ammunition in the weapon. Empty = the weapon's own.")]
    [SerializeField] private AmmoType ammo;
    [Tooltip("Action points it costs to turn towards the mouse (right click).")]
    [SerializeField, Min(0)] private int turnCost = 1;
    [Tooltip("Action points it costs to crouch, and the same to stand up again.")]
    [SerializeField, Min(0)] private int stanceCost = 1;
    [Tooltip("The part of the unit that turns (the Model object). Its forward direction is where the unit looks.")]
    [SerializeField] private Transform facing;
    [Tooltip("Switched off while the selected unit cannot see this unit (its health bar and vision fan).")]
    [SerializeField] private GameObject[] hiddenWhileUnseen;

    [Header("Action points")]
    [SerializeField, Min(0)] private int maxActionPoints = 4;
    [SerializeField, Min(0)] private int actionPoints = 4;
    [Tooltip("Speed: how many tiles the unit runs for one action point (1 = a point per tile; 2 = two tiles per point). A whole number.")]
    [SerializeField, Min(1)] private int speed = 1;

    [Header("Health")]
    [SerializeField, Min(1)] private int maxHealth = 100;
    [SerializeField, Min(0)] private int health = 100;

    private Renderer[] visualRenderers;
    private bool isFogged;
    private float fogFactor = 1f;

    private static readonly List<Unit> allUnits = new List<Unit>();

    /// <summary>Every unit that is currently active in the scene.</summary>
    public static IReadOnlyList<Unit> All => allUnits;

    /// <summary>Goes up every time some unit appears, disappears, takes a tile or leaves one.</summary>
    public static int OccupancyVersion { get; private set; }

    /// <summary>Something on the field changed (an obstacle broke): maps that depend on it (reach, cover) look again.</summary>
    public static void NotifyMapChanged()
    {
        OccupancyVersion++;
    }

    /// <summary>Coordinates of every tile that is taken by a unit other than 'exclude'.</summary>
    public static HashSet<Vector2Int> GetOccupiedCoords(Unit exclude)
    {
        HashSet<Vector2Int> result = new HashSet<Vector2Int>();

        foreach (Unit u in allUnits)
        {
            if (u != null && u != exclude)
                u.AddOccupiedCoords(result);
        }

        return result;
    }

    /// <summary>True if a unit other than 'exclude' stands on the tile or is about to walk onto it.</summary>
    public static bool IsOccupied(Tile tile, Unit exclude)
    {
        return tile != null && GetOccupiedCoords(exclude).Contains(tile.Coords);
    }

    private static void NotifyOccupancyChanged()
    {
        OccupancyVersion++;
    }

    /// <summary>True while at least one unit is on the move.</summary>
    public static bool AnyMoving
    {
        get
        {
            foreach (Unit u in allUnits)
            {
                if (u != null && u.IsMoving)
                    return true;
            }

            return false;
        }
    }
    private MaterialPropertyBlock block;
    private bool isSelected;

    private Tile currentTile;   // tile the unit last arrived on
    private Tile stepTarget;    // tile the unit is running to right now (null when idle)
    private Vector3 stepPosition;
    private readonly Queue<Tile> waypoints = new Queue<Tile>();
    private readonly Queue<Vector3> detour = new Queue<Vector3>();   // extra points to pass before the tile itself (the rungs of a ladder)

    /// <summary>Action points left in the current turn.</summary>
    public int ActionPoints => actionPoints;

    /// <summary>Speed: how many tiles the unit runs for one action point.</summary>
    public int Speed => speed;

    // Walking is paid by the point, and a point buys 'speed' tiles. What was bought and not used yet (a point bought for two tiles when
    // only one was walked) stays for the rest of the turn.
    private int movementCredit;

    /// <summary>Tiles that have been paid for already and not yet walked (they are lost when the turn ends).</summary>
    public int MovementCredit => movementCredit;

    /// <summary>How many tiles of route (as GridManager.PathCost counts them) the unit can walk for this many action points.</summary>
    public int MoveRange(int points)
    {
        return movementCredit + Mathf.Max(0, points) * speed;
    }

    /// <summary>How many action points walking this many tiles of route takes (what is already paid for is taken off first).</summary>
    public int PointsForMove(int tileCost)
    {
        int unpaid = Mathf.Max(0, tileCost - movementCredit);
        return (unpaid + speed - 1) / speed;                 // rounded up: a point that is started is paid whole
    }

    /// <summary>Action points the unit gets back at the start of every turn.</summary>
    public int MaxActionPoints => maxActionPoints;

    /// <summary>Current health. 0 means the unit has no health left.</summary>
    public int Health => health;

    /// <summary>The most health the unit can have.</summary>
    public int MaxHealth => maxHealth;

    public bool IsAlive => health > 0;

    /// <summary>The squad this unit belongs to (null for a unit that is not in one). UnitSpawner sets it.</summary>
    public EnemyGroup Group { get; set; }

    /// <summary>Raised once, when the unit's health runs out.</summary>
    public event System.Action<Unit> Died;

    private bool dead;
    private Vector3 lastHitDirection;

    /// <summary>Raised whenever the unit's health changes (damage or healing).</summary>
    public event System.Action<Unit> HealthChanged;

    /// <summary>The side this unit fights for.</summary>
    public Faction Faction => faction;

    /// <summary>Half-angle (degrees) of the cone the bullets of this unit land in; smaller = a better shot.</summary>
    /// <summary>The weapon this unit carries (null if it has none: then the plain numbers on the unit are used).</summary>
    public WeaponData Weapon => weapon;

    /// <summary>The ammunition in the weapon.</summary>
    public AmmoType Ammo => ammo != null ? ammo : weapon != null ? weapon.DefaultAmmo : null;

    private int fireModeIndex;

    /// <summary>The way of firing that is selected now (null if the unit has no weapon).</summary>
    public FireModeData FireMode
    {
        get
        {
            if (weapon == null || weapon.FireModes.Count == 0)
                return null;
            return weapon.FireModes[Mathf.Clamp(fireModeIndex, 0, weapon.FireModes.Count - 1)];
        }
    }

    public int FireModeIndex => fireModeIndex;

    /// <summary>How many ways of firing the weapon has (1 for a unit without a weapon).</summary>
    public int FireModeCount => weapon != null ? Mathf.Max(1, weapon.FireModes.Count) : 1;

    /// <summary>
    /// How far, in degrees, the bullets scatter: the weapon's accuracy, made worse or better by the way of firing and by the ammunition.
    /// (Without a weapon: the plain Aim Spread of the unit.)
    /// </summary>
    public float AimSpread
    {
        get
        {
            if (weapon == null)
                return aimSpread;

            float spread = weapon.SpreadDegrees;
            FireModeData mode = FireMode;
            if (mode != null)
                spread *= mode.spreadMultiplier;
            AmmoType a = Ammo;
            if (a != null)
                spread *= a.SpreadMultiplier;
            return spread;
        }
    }

    /// <summary>Health that a bullet of this unit takes away.</summary>
    public int WeaponDamage => weapon != null ? weapon.DamagePerBullet(ammo) : weaponDamage;

    /// <summary>How fast this unit's bullets fly (world units per second).</summary>
    public float BulletSpeed => weapon != null ? weapon.BulletSpeed : 40f;

    /// <summary>How many bullets one shot in the present way of firing sends (3 for a burst of three).</summary>
    public int ShotsPerFire => FireMode != null ? Mathf.Max(1, FireMode.shots) : 1;

    /// <summary>How many bullets leave the weapon together in each shot (8 for a shotgun's shot of pellets).</summary>
    public int PelletsPerShot => FireMode != null ? Mathf.Max(1, FireMode.pellets) : 1;

    /// <summary>All the bullets one use of the present way of firing sends: the shots of a burst times the pellets of each.</summary>
    public int BulletsPerFire => ShotsPerFire * PelletsPerShot;

    /// <summary>Seconds between the bullets of a burst.</summary>
    public float ShotInterval => FireMode != null ? FireMode.shotInterval : 0.1f;

    /// <summary>Action points one shot costs.</summary>
    public int ShotCost => FireMode != null ? FireMode.apCost : shotCost;

    /// <summary>What the cheapest way of firing costs.</summary>
    public int CheapestShotCost
    {
        get
        {
            if (weapon == null || weapon.FireModes.Count == 0)
                return shotCost;
            int cheapest = int.MaxValue;
            foreach (FireModeData m in weapon.FireModes)
                cheapest = Mathf.Min(cheapest, m.apCost);
            return cheapest;
        }
    }

    /// <summary>Action points a turn on the spot (TurnTo) costs.</summary>
    public int TurnCost => turnCost;

    /// <summary>Action points crouching costs, and standing up again.</summary>
    public int StanceCost => stanceCost;

    private UnitStance stance;

    /// <summary>True while the unit is crouching (or going down).</summary>
    public bool IsCrouched
    {
        get
        {
            if (stance == null)
                stance = GetComponent<UnitStance>();
            return stance != null && stance.Crouched;
        }
    }

    /// <summary>How much lower the unit's chest is than when standing, right now (0 when standing): the ring of spread and the camera follow it.</summary>
    public float StanceChestDrop
    {
        get
        {
            if (stance == null)
                stance = GetComponent<UnitStance>();
            return stance != null ? stance.ChestDrop : 0f;
        }
    }

    /// <summary>True if the unit may change its stance now: it is alive, not moving, and has the points.</summary>
    public bool CanChangeStance => !dead && !IsMoving && actionPoints >= stanceCost;

    /// <summary>
    /// Crouches a standing unit, stands up a crouching one; costs StanceCost action points. Nothing happens (false) when the unit is
    /// moving, dead, or has too few points.
    /// </summary>
    public bool ToggleStance()
    {
        if (!CanChangeStance)
            return false;

        if (stance == null)
            stance = GetComponent<UnitStance>();
        if (stance == null)
            stance = gameObject.AddComponent<UnitStance>();

        stance.SetCrouched(!stance.Crouched);
        actionPoints -= stanceCost;
        return true;
    }

    /// <summary>A turn by less than this many degrees is no turn: nothing happens and nothing is paid.</summary>
    public const float MinTurnAngle = 3f;

    /// <summary>True if there are enough action points left for one shot.</summary>
    public bool CanAffordShot => IsAlive && actionPoints >= CheapestShotCost;

    /// <summary>True if there are enough action points for a shot in the way of firing that is selected now.</summary>
    public bool CanAffordCurrentShot => IsAlive && actionPoints >= ShotCost;

    /// <summary>True if there is another way of firing that the unit has the points for (so that switching the mode would do something).</summary>
    public bool CanSwitchFireMode
    {
        get
        {
            if (weapon == null)
                return false;
            for (int i = 0; i < weapon.FireModes.Count; i++)
            {
                if (i != fireModeIndex && weapon.FireModes[i].apCost <= actionPoints)
                    return true;
            }
            return false;
        }
    }

    /// <summary>Selects another way of firing (false if there is no such one or it is the one selected already).</summary>
    public bool SetFireMode(int index)
    {
        if (weapon == null || index < 0 || index >= weapon.FireModes.Count || index == fireModeIndex)
            return false;
        fireModeIndex = index;
        return true;
    }

    /// <summary>
    /// Selects the next way of firing, round and round; with 'onlyAffordable' only one that the unit has the points for.
    /// Returns whether the selection changed.
    /// </summary>
    public bool CycleFireMode(bool onlyAffordable)
    {
        if (weapon == null || weapon.FireModes.Count < 2)
            return false;

        int count = weapon.FireModes.Count;
        for (int step = 1; step < count; step++)
        {
            int candidate = (fireModeIndex + step) % count;
            if (!onlyAffordable || weapon.FireModes[candidate].apCost <= actionPoints)
            {
                fireModeIndex = candidate;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// If the way of firing that is selected costs more than the unit has points left, the cheapest one that it can pay for is selected
    /// instead (so that a unit with two points does not stay on a burst that costs three).
    /// </summary>
    public void EnsureAffordableMode()
    {
        if (weapon == null || CanAffordCurrentShot)
            return;

        int best = -1;
        for (int i = 0; i < weapon.FireModes.Count; i++)
        {
            if (weapon.FireModes[i].apCost <= actionPoints && (best < 0 || weapon.FireModes[i].apCost < weapon.FireModes[best].apCost))
                best = i;
        }
        if (best >= 0)
            fireModeIndex = best;
    }

    /// <summary>Takes the action points away if there are enough of them; otherwise does nothing and returns false.</summary>
    public bool TrySpendActionPoints(int amount)
    {
        if (amount < 0 || actionPoints < amount)
            return false;

        actionPoints -= amount;
        return true;
    }

    /// <summary>Where bullets leave the weapon: the end of the barrel (a point in front of the chest if the weapon has no muzzle marker).</summary>
    /// <summary>The object that is the body of the unit (it turns with the unit and carries the weapon).</summary>
    public Transform Model => facing != null ? facing : transform;

    public Vector3 MuzzlePosition
    {
        get
        {
            if (muzzle != null)
                return muzzle.position;
            return transform.position + Vector3.up * 0.95f + FacingDirection * 0.55f;
        }
    }

    /// <summary>Width of the field of view in degrees.</summary>
    public float ViewAngle => viewAngle;

    /// <summary>How far the unit sees, in tiles.</summary>
    public float ViewRange => viewRange;

    /// <summary>The direction the unit looks in, flat on the ground (length 1).</summary>
    public Vector3 FacingDirection
    {
        get
        {
            Vector3 v = (facing != null ? facing : transform).forward;
            v.y = 0f;
            return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
        }
    }

    public Vector2Int Coords => coords;
    public bool IsSelected => isSelected;

    /// <summary>True while the unit is in the dark: the selected unit cannot see it.</summary>
    public bool IsFogged => isFogged;
    public bool IsMoving => stepTarget != null;

    private void Awake()
    {
        actionPoints = maxActionPoints;
        health = maxHealth;

        // the inventory: every unit has one (an UnitInventory on the prefab holds its equipment; without one an empty inventory is made)
        inventory = GetComponent<UnitInventory>();
        if (inventory == null)
            inventory = gameObject.AddComponent<UnitInventory>();
        inventory.Initialize(this);

        if (weapon != null && weapon.ModelPrefab != null)
            ApplyWeaponVisual();
    }

    private UnitInventory inventory;

    /// <summary>What the unit carries: six slots (primary and secondary weapon, armor, vest, extra place, backpack).</summary>
    public UnitInventory Inventory
    {
        get
        {
            if (inventory == null)
            {
                inventory = GetComponent<UnitInventory>();
                if (inventory == null)
                    inventory = gameObject.AddComponent<UnitInventory>();
                inventory.Initialize(this);
            }
            return inventory;
        }
    }

    /// <summary>
    /// Gives the unit a weapon and takes it into the hands: it goes into the first weapon slot of the inventory that it fits (the primary
    /// one if it fits, taking the place of what was there), and becomes the weapon in the hands.
    /// </summary>
    public void SetWeapon(WeaponData newWeapon)
    {
        if (newWeapon == null)
        {
            ApplyWeapon(null);
            return;
        }

        foreach (InventorySlot slot in new[] { InventorySlot.Primary, InventorySlot.Secondary, InventorySlot.Extra })
        {
            if (newWeapon.FitsSlot(slot) && Inventory.Equip(slot, newWeapon))
            {
                Inventory.SelectWeapon(slot);
                return;
            }
        }

        Debug.LogWarning("Unit: " + newWeapon.name + " fits none of the weapon slots of " + name + ", it is held without being in the inventory.", this);
        ApplyWeapon(newWeapon);
    }

    /// <summary>
    /// Puts the weapon into the hands: its numbers, its ways of firing (the first is selected) and, if it has a model, what the soldier holds.
    /// The inventory does this when the weapon in the hands changes; to give a unit a weapon use SetWeapon or the inventory.
    /// </summary>
    public void ApplyWeapon(WeaponData newWeapon)
    {
        weapon = newWeapon;
        ammo = null;
        fireModeIndex = 0;
        ApplyWeaponVisual();
    }

    private Transform weaponSocket;
    private GameObject defaultWeaponModel;
    private Transform defaultMuzzle;
    private Vector3 defaultRightGrip, defaultLeftGrip;
    private bool weaponVisualKnown;
    private GameObject heldModel;

    // What the soldier holds. The rifle that every unit comes with is hidden if the weapon has a model of its own, and that model is
    // put on the weapon socket instead, with a muzzle at the end of its barrel and the targets of the hands at its grips.
    private void ApplyWeaponVisual()
    {
        if (weaponSocket == null)
            weaponSocket = FindDeep(transform, "WeaponSocket");
        if (weaponSocket == null)
            return;

        Transform rightTarget = weaponSocket.Find("RightHandTarget");
        Transform leftTarget = weaponSocket.Find("LeftHandTarget");
        if (!weaponVisualKnown)
        {
            weaponVisualKnown = true;
            defaultMuzzle = muzzle;
            Transform rifle = weaponSocket.Find("AK74");
            defaultWeaponModel = rifle != null ? rifle.gameObject : null;
            defaultRightGrip = rightTarget != null ? rightTarget.localPosition : Vector3.zero;
            defaultLeftGrip = leftTarget != null ? leftTarget.localPosition : Vector3.zero;
        }

        if (heldModel != null)
        {
            heldModel.SetActive(false);                 // Destroy only works at the end of the frame: the old model must not be seen until then
            if (Application.isPlaying) Destroy(heldModel); else DestroyImmediate(heldModel);
            heldModel = null;
        }

        if (weapon != null && weapon.ModelPrefab != null)
        {
            if (defaultWeaponModel != null)
                defaultWeaponModel.SetActive(false);

            GameObject model = Instantiate(weapon.ModelPrefab, weaponSocket);
            model.name = "Model_" + weapon.DisplayName;
            model.transform.localPosition = weapon.ModelOffset;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one * weapon.ModelScale;
            foreach (Collider c in model.GetComponentsInChildren<Collider>())
            {
                if (Application.isPlaying) Destroy(c); else DestroyImmediate(c);
            }
            if (weapon.ModelMaterial != null)
            {
                foreach (Renderer r in model.GetComponentsInChildren<Renderer>())
                {
                    Material[] mats = new Material[r.sharedMaterials.Length];
                    for (int i = 0; i < mats.Length; i++)
                        mats[i] = weapon.ModelMaterial;
                    r.sharedMaterials = mats;
                }
            }

            Transform end = new GameObject("Muzzle").transform;
            end.SetParent(model.transform, false);
            end.localPosition = weapon.MuzzlePoint;
            end.localRotation = Quaternion.LookRotation(Vector3.back, Vector3.up);
            muzzle = end;

            if (rightTarget != null)
                rightTarget.localPosition = weapon.ModelOffset + weapon.RightGrip * weapon.ModelScale;
            if (leftTarget != null)
                leftTarget.localPosition = weapon.ModelOffset + weapon.LeftGrip * weapon.ModelScale;
            heldModel = model;
        }
        else
        {
            if (defaultWeaponModel != null)
                defaultWeaponModel.SetActive(true);
            muzzle = defaultMuzzle;
            if (rightTarget != null)
                rightTarget.localPosition = defaultRightGrip;
            if (leftTarget != null)
                leftTarget.localPosition = defaultLeftGrip;
        }

        bodyRenderers = null;               // the unit looks different now: the list of what is hidden when it is concealed is made again
    }

    private static Transform FindDeep(Transform root, string childName)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == childName)
                return t;
        }
        return null;
    }

    private void OnEnable()
    {
        allUnits.Add(this);
        NotifyOccupancyChanged();
    }

    private void OnDisable()
    {
        allUnits.Remove(this);
        NotifyOccupancyChanged();
    }

    // A unit takes the tile it stands on, the tile it is running to and every tile still ahead on its route.
    // That way two units can never be on one tile, and never plan to be.
    private void AddOccupiedCoords(HashSet<Vector2Int> set)
    {
        if (dead)
            return;                 // a body does not take a tile

        set.Add(currentTile != null ? currentTile.Coords : coords);

        if (stepTarget != null)
            set.Add(stepTarget.Coords);

        foreach (Tile t in waypoints)
        {
            if (t != null)
                set.Add(t.Coords);
        }
    }

    private void Update()
    {
        if (dead)
            return;

        Tick(Time.deltaTime);
    }

    /// <summary>Reduces the unit's health by the given amount (never below 0).</summary>
    /// <summary>Same, and the unit knows which way the hit came (a unit that dies falls that way).</summary>
    public void TakeDamage(int amount, Vector3 hitDirection)
    {
        lastHitDirection = hitDirection;
        TakeDamage(amount);
    }

    public void TakeDamage(int amount)
    {
        if (amount <= 0)
            return;

        // the armor takes some off every hit, but a hit always does something
        amount = Mathf.Max(1, amount - Inventory.DamageReduction);

        SetHealth(health - amount);
    }

    /// <summary>Restores the given amount of health (never above the maximum).</summary>
    public void Heal(int amount)
    {
        if (amount <= 0)
            return;

        SetHealth(health + amount);
    }

    private void SetHealth(int value)
    {
        if (dead)
            return;                 // the dead stay dead

        value = Mathf.Clamp(value, 0, maxHealth);
        if (value == health)
            return;

        health = value;
        HealthChanged?.Invoke(this);

        if (health <= 0)
            Die();
    }

    // The unit has no health left: it stops, falls over and stays lying. It no longer takes a tile, cannot be selected, hit, or sent anywhere.
    private void Die()
    {
        dead = true;

        UnitStance crouching = GetComponent<UnitStance>();
        if (crouching != null)
            crouching.enabled = false;      // the pose stays as it was; the body falls from it

        UnitPeek leaning = GetComponent<UnitPeek>();
        if (leaning != null)
        {
            leaning.Snap();                 // back in place before it falls
            if (Application.isPlaying) Destroy(leaning); else DestroyImmediate(leaning);
        }

        stepTarget = null;
        waypoints.Clear();
        detour.Clear();

        isSelected = false;
        RefreshVisuals();

        foreach (Collider c in GetComponentsInChildren<Collider>())
            c.enabled = false;

        if (hiddenWhileUnseen != null)
        {
            foreach (GameObject go in hiddenWhileUnseen)
            {
                if (go != null)
                    go.SetActive(false);        // no health bar, no field of view
            }
        }

        // the pose is frozen where it is, then the body falls
        foreach (Animator a in GetComponentsInChildren<Animator>())
            a.enabled = false;
        foreach (UnitWeaponIK ik in GetComponentsInChildren<UnitWeaponIK>())
            ik.enabled = false;
        foreach (UnitAnimator ua in GetComponentsInChildren<UnitAnimator>())
            ua.enabled = false;

        Transform model = facing != null ? facing : transform;
        UnitCorpse corpse = GetComponent<UnitCorpse>();
        if (corpse == null)
            corpse = gameObject.AddComponent<UnitCorpse>();
        corpse.Begin(model, lastHitDirection.sqrMagnitude > 1e-6f ? lastHitDirection : -model.forward);

        NotifyOccupancyChanged();
        Died?.Invoke(this);
    }

    // Handy for trying the health bars out: right-click the Unit component header.
    [ContextMenu("Take 10 damage")]
    private void DebugTakeDamage()
    {
        TakeDamage(10);
    }

    [ContextMenu("Heal 10")]
    private void DebugHeal()
    {
        Heal(10);
    }

    /// <summary>Gives the unit all of its action points back (start of a new turn).</summary>
    public void RestoreActionPoints()
    {
        movementCredit = 0;
        actionPoints = maxActionPoints;
    }

    private void Tick(float deltaTime)
    {
        if (stepTarget == null)
            return;

        transform.position = Vector3.MoveTowards(transform.position, stepPosition, moveSpeed * deltaTime);

        if ((transform.position - stepPosition).sqrMagnitude < 0.000001f)
        {
            transform.position = stepPosition;

            // on a ladder: first the foot of the rungs, then the top, then the tile itself
            if (detour.Count > 0)
            {
                stepPosition = detour.Dequeue();
                return;
            }

            currentTile = stepTarget;
            coords = currentTile.Coords;
            NotifyOccupancyChanged();
            AdvanceStep();
        }
    }

    // Starts the next hop of the route and pays for it: a straight hop costs 1 point,
    // a diagonal hop costs 2 (the two orthogonal steps it replaces).
    private void AdvanceStep()
    {
        if (waypoints.Count == 0)
        {
            stepTarget = null;
            return;
        }

        stepTarget = waypoints.Dequeue();
        stepPosition = GetPositionOn(stepTarget);
        detour.Clear();

        int cost = 1;
        if (currentTile != null)
        {
            GridManager grid = stepTarget.GetComponentInParent<GridManager>();
            Ladder ladder = grid != null ? grid.GetLadder(currentTile, stepTarget) : null;
            if (grid != null)
            {
                cost = grid.StepCost(currentTile.Coords, stepTarget.Coords);
            }
            else
            {
                Vector2Int delta = stepTarget.Coords - currentTile.Coords;
                cost = Mathf.Abs(delta.x) + Mathf.Abs(delta.y);
            }

            if (ladder != null)
            {
                // climb: to the foot of the rungs, up (or down) the ladder, then onto the tile
                Vector3 arrival = stepPosition;
                bool up = ladder.roof == stepTarget;
                stepPosition = up ? ladder.climbBottom : ladder.climbTop;
                detour.Enqueue(up ? ladder.climbTop : ladder.climbBottom);
                detour.Enqueue(arrival);
            }
        }

        // paid by the point: each point buys 'speed' tiles, and what is left of the last one stays for the next hop
        while (movementCredit < cost && actionPoints > 0)
        {
            actionPoints--;
            movementCredit += speed;
        }
        movementCredit = Mathf.Max(0, movementCredit - cost);

        // every hop is heard by the other side if it is near (NoiseSystem)
        NoiseSystem.Emit(NoiseKind.Footsteps, this, transform.position);
    }

    /// <summary>
    /// Instantly puts the unit onto the given tile (its feet on the tile's top face). Costs no action points.
    /// Returns false (and does nothing) if another unit is on that tile.
    /// </summary>
    public bool PlaceOn(Tile tile)
    {
        if (tile == null || IsOccupied(tile, this))
            return false;

        waypoints.Clear();
        detour.Clear();
        stepTarget = null;
        currentTile = tile;
        coords = tile.Coords;
        transform.position = GetPositionOn(tile);
        NotifyOccupancyChanged();
        return true;
    }

    /// <summary>
    /// Sends the unit towards the given tile, walking around walls and around other units.
    /// Every tile of the route costs 1 action point at speed 1 (a unit with speed 2 runs two tiles for a point), climbing a ladder (to a roof or back down) costs the ladder cost of
    /// the field (GridManager, 1 by default). A tile that is farther than the unit has points for
    /// (outside the area it can reach this turn) is refused: the unit does not move at all.
    /// Returns false (and does nothing) if the unit is already moving, has no action points, the tile is farther
    /// than its points allow, it is already standing on that tile, the tile is taken by another unit or a building,
    /// or it cannot be reached.
    /// </summary>
    public bool MoveTo(Tile destination)
    {
        // A move that has been started is always finished before a new order is accepted.
        if (IsMoving)
            return false;

        if (dead || destination == null || actionPoints <= 0)
            return false;

        GridManager grid = destination.GetComponentInParent<GridManager>();
        if (grid == null)
            return false;

        if (currentTile == null)
            currentTile = grid.GetTile(coords);
        if (currentTile == null)
            return false;

        // The route goes around other units, and a tile that another unit stands on
        // (or is on its way to) cannot be the destination.
        List<Tile> path = Pathfinder.FindPath(grid, currentTile, destination, this);
        if (path == null)
            return false;

        if (path.Count < 2)
            return false; // already standing there

        int cost = grid.PathCost(path);

        // Outside the reachable area: not enough points for the whole route (the speed says how far a point goes), so no move at all.
        if (PointsForMove(cost) > actionPoints)
            return false;

        // Cut corners where possible. The total cost stays the same.
        List<Tile> route = Pathfinder.SmoothPath(grid, path);

        waypoints.Clear();
        for (int i = 1; i < route.Count; i++)
            waypoints.Enqueue(route[i]);

        AdvanceStep();
        NotifyOccupancyChanged();
        return true;
    }

    // The unit's pivot is at its feet, so it simply stands on the top face of the tile.
    private Vector3 GetPositionOn(Tile tile)
    {
        if (tile.HasRoof)
            return new Vector3(tile.transform.position.x, tile.RoofY, tile.transform.position.z);      // standing on the roof

        Renderer tileRenderer = tile.GetComponent<Renderer>();
        Vector3 tilePos = tile.transform.position;
        float tileTop = tileRenderer != null ? tileRenderer.bounds.max.y : tilePos.y;
        return new Vector3(tilePos.x, tileTop, tilePos.z);
    }

    // ---- how the unit looks: selected (shirt turns yellow) and / or in the dark (everything is darkened) ----
    // This is the only place that writes the colour overrides, so the two effects never overwrite each other.

    private Renderer[] GetVisualRenderers()
    {
        if (visualRenderers == null)
        {
            Transform body = facing != null ? facing : transform;
            var list = new List<Renderer>();
            foreach (Renderer r in body.GetComponentsInChildren<Renderer>(true))
            {
                if (r.GetComponent<UnitVisionDisplay>() == null)   // the vision fan keeps its own colours
                    list.Add(r);
            }

            visualRenderers = list.ToArray();
        }

        return visualRenderers;
    }

    // Is this material of this renderer one of the parts that turn yellow when the unit is selected?
    private bool IsSelectionPart(Renderer rend, int materialIndex)
    {
        if (!isSelected)
            return false;

        bool wholeUnit = selectionRenderers == null || selectionRenderers.Length == 0;
        if (wholeUnit)
            return true;

        if (System.Array.IndexOf(selectionRenderers, rend) < 0)
            return false;

        return selectionMaterialIndices == null || selectionMaterialIndices.Length == 0
            || System.Array.IndexOf(selectionMaterialIndices, materialIndex) >= 0;
    }

    private void RefreshVisuals()
    {
        if (block == null)
            block = new MaterialPropertyBlock();

        foreach (Renderer rend in GetVisualRenderers())
        {
            if (rend == null)
                continue;

            Material[] mats = rend.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                bool selectedPart = IsSelectionPart(rend, i);
                if ((!selectedPart && !isFogged) || mats[i] == null)
                {
                    rend.SetPropertyBlock(null, i);       // the material's own colour
                    continue;
                }

                Color c;
                if (selectedPart)
                    c = selectedColor;
                else if (mats[i].HasProperty("_BaseColor"))
                    c = mats[i].GetColor("_BaseColor");
                else if (mats[i].HasProperty("_Color"))
                    c = mats[i].GetColor("_Color");
                else
                    c = Color.white;

                if (isFogged)
                    c = new Color(c.r * fogFactor, c.g * fogFactor, c.b * fogFactor, c.a);

                rend.GetPropertyBlock(block, i);
                block.SetColor(BaseColorId, c);
                block.SetColor(ColorId, c);
                rend.SetPropertyBlock(block, i);
            }
        }
    }

    public void SetSelected(bool selected)
    {
        isSelected = selected;
        RefreshVisuals();
    }

    /// <summary>
    /// Puts the unit 'in the dark': everything about it is darkened by the factor and its health bar and vision
    /// fan are hidden (the selected unit cannot see it). SetFogged(false, ...) brings it back.
    /// </summary>
    public void SetFogged(bool fogged, float factor)
    {
        if (isFogged == fogged && (!fogged || Mathf.Approximately(fogFactor, factor)))
            return;

        isFogged = fogged;
        fogFactor = factor;
        RefreshVisuals();

        if (hiddenWhileUnseen != null)
        {
            foreach (GameObject go in hiddenWhileUnseen)
            {
                if (go != null)
                    go.SetActive(!fogged && !dead && !concealed);
            }
        }
    }

    /// <summary>
    /// Turns the unit, and with it its field of view, towards a point on the ground. Costs no action points.
    /// Returns false (and does nothing) while the unit is moving, or when the point is right where the unit stands.
    /// </summary>
    public bool LookAt(Vector3 worldPoint)
    {
        if (dead || IsMoving)
            return false;

        Vector3 d = worldPoint - transform.position;
        d.y = 0f;
        if (d.sqrMagnitude < 0.0225f)       // closer than 15 cm: no direction to speak of
            return false;

        UnitAnimator turner = GetComponentInChildren<UnitAnimator>();
        if (turner == null)
            return false;

        turner.LookAt(worldPoint);
        return true;
    }

    /// <summary>
    /// The turn the player orders (right click): like LookAt, but it costs TurnCost action points. Nothing is turned and nothing
    /// is paid when the unit is dead or moving, has too few points left, when the point is right where the unit stands, or when the
    /// unit already looks that way (less than MinTurnAngle degrees to turn). Returns whether the unit turned.
    /// </summary>
    public bool TurnTo(Vector3 worldPoint)
    {
        if (dead || IsMoving || actionPoints < turnCost)
            return false;

        Vector3 d = worldPoint - transform.position;
        d.y = 0f;
        if (d.sqrMagnitude < 0.0225f)
            return false;

        Vector3 now = FacingDirection;
        now.y = 0f;
        if (now.sqrMagnitude > 1e-6f && Vector3.Angle(now, d) < MinTurnAngle)
            return false;

        if (!LookAt(worldPoint))
            return false;

        actionPoints -= turnCost;
        return true;
    }

    /// <summary>True while the unit is hidden: not drawn at all (see EnemyVisibility).</summary>
    public bool IsConcealed => concealed;

    private bool concealed;
    private Renderer[] bodyRenderers;

    /// <summary>
    /// Hides the unit (or shows it again): its body, weapon and shadow are not drawn, and neither are its health bar and vision fan.
    /// Nothing else changes: the unit keeps its tile, bullets still hit it, it keeps moving and acting.
    /// </summary>
    public void SetConcealed(bool value)
    {
        if (value == concealed && bodyRenderers != null)
            return;

        concealed = value;
        if (bodyRenderers == null)
            CacheBodyRenderers();

        foreach (Renderer r in bodyRenderers)
        {
            if (r != null)
                r.enabled = !value;
        }

        // the health bar and the vision fan are switched by whole objects (the fog does it too, see SetFogged)
        if (hiddenWhileUnseen != null)
        {
            foreach (GameObject go in hiddenWhileUnseen)
            {
                if (go != null)
                    go.SetActive(!isFogged && !dead && !concealed);
            }
        }
    }

    // The renderers of the body and weapon: not the ones of the health bar and the fan, which are switched as objects.
    private void CacheBodyRenderers()
    {
        List<Renderer> list = new List<Renderer>();
        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
        {
            if (r.GetComponent<UnitVisionDisplay>() != null)
                continue;

            bool underSwitchedObject = false;
            if (hiddenWhileUnseen != null)
            {
                foreach (GameObject go in hiddenWhileUnseen)
                {
                    if (go != null && r.transform.IsChildOf(go.transform))
                        underSwitchedObject = true;
                }
            }

            if (!underSwitchedObject)
                list.Add(r);
        }
        bodyRenderers = list.ToArray();
    }

    public void ToggleSelected()
    {
        SetSelected(!isSelected);
    }
}
