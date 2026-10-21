using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The enemy's turn. TurnManager.EndTurn starts it (BeginPhase); when every enemy unit has acted, PhaseFinished is raised.
///
/// The enemies play by the same rules as the player: they see only what is in the field of view of the unit (position, facing, view
/// angle, range, buildings and walls in the way: GridManager.CanSee), moving costs action points per tile, turning on the spot
/// costs Unit.TurnCost, a shot costs Unit.ShotCost, and a shot is a real bullet that flies from the muzzle to a random point of the
/// ring of spread and is stopped by whatever it meets.
///
/// The enemies act in squads (EnemyGroup: 2 to 4 units that spawn together). A squad acts together, one member after another, and shares
/// what it finds out: a unit of the player that one member sees is known to all of them (and to the squads within 20 tiles), and the
/// ground that one member has looked at this turn is not looked at again by the others.
///
/// A unit that sees somebody of the player (ENGAGE):
///  - picks a target (near, wounded);
///  - looks over the tiles it can reach and still shoot from, and picks the best: with cover from the direction of the enemy (what
///    stands on the tile on that side), a clear way for a bullet (leaning out of cover to the side if that is what it takes: UnitPeek),
///    few other enemies able to see that tile, a good distance, few steps, and room for as many shots as possible;
///  - goes there, turns to the target if it must, leans out of cover, shoots, steps back into cover;
///  - with points left but none for another shot it moves to better cover.
///
/// A unit that does not see anybody but whose squad (or a near squad) knows where somebody was seen lately (HUNT):
///  - goes to a place from which that spot can be seen, preferably with cover, turns towards it and looks; if it looks and nobody is
///    there, the squad drops that sighting.
///
/// A unit with nothing to go on (SEARCH):
///  - looks for good ground: a tile with cover around it (walls, buildings, props) that also has a wide view, near the other members of
///    its squad but not on top of them; it walks there over as many turns as it takes and then stays;
///  - with the points it has left it looks around, each time towards the ground that nobody of the squad has looked at yet this turn.
///
/// What the enemies decided is written to Journal (and to the console if Log Decisions is on).
/// Put it on any object in the scene (one is enough); TurnManager finds it.
/// </summary>
public class EnemyAI : MonoBehaviour
{
    [SerializeField] private GridManager grid;
    [Tooltip("The faction this AI plays.")]
    [SerializeField] private Faction aiFaction = Faction.Enemy;
    [Tooltip("The faction it fights.")]
    [SerializeField] private Faction opponentFaction = Faction.Player;

    [Header("Pace")]
    [Tooltip("Game speed while a unit that the player cannot see is acting (so that the player does not wait for what he cannot see).")]
    [SerializeField, Min(1f)] private float hiddenSpeed = 3f;
    [SerializeField, Min(0f)] private float pauseBetweenUnits = 0.25f;
    [SerializeField, Min(1)] private int maxActionsPerUnit = 10;
    [Tooltip("If one unit takes longer than this (game seconds), its turn is cut short.")]
    [SerializeField, Min(5f)] private float unitTimeLimit = 40f;

    [Header("Attack: what makes a place to shoot from good")]
    [SerializeField] private float coverWeight = 3f;
    [SerializeField] private float shotWeight = 2f;
    [SerializeField] private float stepCost = 0.4f;
    [SerializeField] private float exposureWeight = 0.4f;
    [SerializeField] private float stayBonus = 0.5f;

    [Header("Search: what makes a place to wait in good")]
    [SerializeField] private float searchCoverWeight = 1.2f;
    [SerializeField] private float overviewWeight = 5f;
    [SerializeField] private float searchStepCost = 0.25f;
    [Tooltip("A unit leaves its place only for one that is at least this much better.")]
    [SerializeField] private float searchHysteresis = 1f;
    [Tooltip("How far (in tiles) from where it stands a unit looks for good ground to settle in.")]
    [SerializeField, Min(2)] private int goalRadius = 14;
    [Tooltip("How much each tile of the walk to a place counts against it.")]
    [SerializeField] private float goalDistanceCost = 0.12f;
    [Tooltip("The members of a squad try to stay within this many tiles of each other.")]
    [SerializeField, Min(1)] private int cohesionRadius = 3;

    [Header("Intelligence")]
    [Tooltip("A sighting is used for this many enemy turns after it was made (0 = only in the turn it was made).")]
    [SerializeField, Min(0)] private int alertTurns = 2;

    [SerializeField] private bool logDecisions;

    /// <summary>The faction this AI plays.</summary>
    public Faction AiFaction => aiFaction;

    /// <summary>True while the enemy's turn is going on.</summary>
    public bool IsRunning => phaseActive;

    /// <summary>The number of the enemy turn that is going on (or was the last one).</summary>
    public int PhaseNumber => phaseCounter;

    /// <summary>Raised when every unit of the enemy has acted.</summary>
    public event Action PhaseFinished;

    /// <summary>What the units decided and did (the newest at the end; the oldest are dropped).</summary>
    public readonly List<string> Journal = new List<string>();

    private class Memory
    {
        public bool moveEvaluated;
        public bool hasGoal;
        public Vector2Int goal;
        public readonly HashSet<Vector2Int> seen = new HashSet<Vector2Int>();
    }

    private class AttackPlan
    {
        public Tile tile;
        public float score;
        public int cost;
        public bool needsTurn;
        public int shots;
        public Vector3 lean;
    }

    private readonly Dictionary<Unit, Memory> memories = new Dictionary<Unit, Memory>();
    private readonly List<Unit> pending = new List<Unit>();
    private readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    private readonly HashSet<Vector2Int> scratch = new HashSet<Vector2Int>();

    private bool phaseActive;
    private int phaseCounter;
    private Unit current;
    private float now;
    private float unitStart;
    private float waitTimer;
    private Func<bool> waitUntil;
    private bool stepDone;
    private float groundTop = float.NaN;
    private IsometricCamera iso;

    [Header("Hearing")]
    [Tooltip("What an enemy hears is only roughly where it was: footsteps are taken to be up to this many tiles from the true place (a shot is exact).")]
    [SerializeField, Min(0)] private int heardUncertainty = 1;

    private void OnEnable()
    {
        NoiseSystem.Heard += OnNoise;
    }

    private void OnDisable()
    {
        NoiseSystem.Heard -= OnNoise;
        if (phaseActive)
            Finish(false);
    }

    // Units of the player walk and shoot; the enemies within hearing range (NoiseSystem) get a lead: the squad (and the squads within 20 tiles)
    // will go and look at that place.
    private void OnNoise(NoiseEvent e, IReadOnlyList<Unit> listeners)
    {
        if (e.source == null)
            return;
        if (grid == null)
            grid = FindFirstObjectByType<GridManager>();
        if (grid == null)
            return;

        List<EnemyGroup> done = new List<EnemyGroup>();
        for (int i = 0; i < listeners.Count; i++)
        {
            Unit l = listeners[i];
            if (l == null || l.Faction != aiFaction || !l.IsAlive)
                continue;
            if (l.Group == null)
                new EnemyGroup(new[] { l });
            if (done.Contains(l.Group))
                continue;
            done.Add(l.Group);

            Vector2Int tile = NoiseSystem.FuzzyTile(grid, e.tile, e.kind == NoiseKind.Shot ? 0 : heardUncertainty);
            bool changed;
            List<EnemyGroup> told = l.Group.Report(e.source, tile, phaseCounter, grid.TileSize, true, out changed);
            if (!changed && told.Count == 0)
                continue;

            string others = "";
            foreach (EnemyGroup t in told)
                others += (others.Length > 0 ? ", " : "") + "G" + t.Id;
            Note(Tag(l) + " heard " + (e.kind == NoiseKind.Shot ? "a shot" : "footsteps") + " near " + tile + (told.Count > 0 ? ": passed on to the squads " + others : ""));
        }
    }

    private void Update()
    {
        Pump(Time.deltaTime);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // The turn: a queue of units, each one a routine that waits for its moves and shots to finish. Pump advances the routines;
    // it is called every frame (and can be called by hand with any time step).
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>Starts the enemy's turn. Does nothing if it is already going on.</summary>
    public void BeginPhase()
    {
        if (phaseActive)
            return;
        if (grid == null)
            grid = FindFirstObjectByType<GridManager>();

        phaseCounter++;

        // every unit belongs to a squad (a unit that was not put in one is a squad of one)
        List<Unit> alive = new List<Unit>();
        foreach (Unit u in Unit.All)
        {
            if (u != null && u.Faction == aiFaction && u.IsAlive)
            {
                if (u.Group == null)
                    new EnemyGroup(new[] { u });
                alive.Add(u);
            }
        }

        foreach (EnemyGroup g in EnemyGroup.All)
        {
            g.Prune();
            g.SeenThisTurn.Clear();
        }

        // squad by squad (the members one after another, so that what one finds out the next one can use), the squads that are nearest to the
        // enemy first
        pending.Clear();
        List<EnemyGroup> order = new List<EnemyGroup>();
        foreach (Unit u in alive)
        {
            if (!order.Contains(u.Group))
                order.Add(u.Group);
        }
        order.Sort((a, b) => NearestOpponentDistance(a).CompareTo(NearestOpponentDistance(b)));
        foreach (EnemyGroup g in order)
        {
            List<Unit> members = new List<Unit>();
            foreach (Unit u in alive)
            {
                if (u.Group == g)
                    members.Add(u);
            }
            members.Sort((a, b) => NearestOpponentDistance(a).CompareTo(NearestOpponentDistance(b)));
            pending.AddRange(members);
        }

        stack.Clear();
        waitTimer = 0f;
        waitUntil = null;
        current = null;
        phaseActive = true;
        Note("--- enemy turn " + phaseCounter + " begins: " + pending.Count + " units in " + order.Count + " squads");
        StartNext();
    }

    /// <summary>Moves the turn along by 'dt' game seconds (done every frame by itself).</summary>
    public void Pump(float dt)
    {
        if (!phaseActive)
            return;

        now += dt;

        // a unit that takes too long is cut short
        if (current != null && now - unitStart > unitTimeLimit)
        {
            Note(current.name + ": took too long, its turn is cut short");
            AbortCurrent();
        }

        if (waitTimer > 0f)
        {
            waitTimer -= dt;
            if (waitTimer > 0f)
                return;
            waitTimer = 0f;
        }
        if (waitUntil != null)
        {
            if (!waitUntil())
                return;
            waitUntil = null;
        }

        for (int guard = 0; guard < 400; guard++)
        {
            if (stack.Count == 0)
            {
                if (!StartNext())
                    return;                     // the turn is over
                continue;
            }

            IEnumerator top = stack.Peek();
            bool more;
            try
            {
                more = top.MoveNext();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                AbortCurrent();
                continue;
            }

            if (!more)
            {
                stack.Pop();
                continue;
            }

            object y = top.Current;
            IEnumerator inner = y as IEnumerator;
            if (inner != null)
            {
                stack.Push(inner);
                continue;
            }
            if (y is float)
            {
                waitTimer = (float)y;
                return;
            }
            Func<bool> condition = y as Func<bool>;
            if (condition != null)
            {
                if (condition())
                    continue;
                waitUntil = condition;
                return;
            }
            return;                             // null: continue next time
        }
    }

    private bool StartNext()
    {
        while (pending.Count > 0)
        {
            Unit u = pending[0];
            pending.RemoveAt(0);
            if (u == null || !u.IsAlive)
                continue;

            current = u;
            unitStart = now;
            stack.Clear();
            stack.Push(RunUnit(u));
            return true;
        }

        Finish(true);
        return false;
    }

    private void AbortCurrent()
    {
        stack.Clear();
        waitUntil = null;
        waitTimer = 0f;
        if (current != null)
            TidyUp(current, null);
    }

    private void Finish(bool raise)
    {
        phaseActive = false;
        current = null;
        stack.Clear();
        waitUntil = null;
        waitTimer = 0f;
        Time.timeScale = 1f;
        Note("--- enemy turn " + phaseCounter + " is over");
        if (raise)
            PhaseFinished?.Invoke();
    }

    // The game runs faster while a unit that the player cannot see is acting.
    private void ApplyPace(Unit u)
    {
        Time.timeScale = u != null && u.IsConcealed ? hiddenSpeed : 1f;
    }

    private void Glide(Vector3 point)
    {
        if (iso == null)
            iso = FindFirstObjectByType<IsometricCamera>();
        if (iso != null && iso.enabled)
            iso.GlideTo(point);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // One unit
    // ---------------------------------------------------------------------------------------------------------------

    private IEnumerator RunUnit(Unit u)
    {
        Memory m = MemoryOf(u);
        m.moveEvaluated = false;
        AddCurrentView(u, m);

        ApplyPace(u);
        if (!u.IsConcealed)
            Glide(u.transform.position);
        yield return 0.2f;

        for (int guard = 0; u != null && u.IsAlive && u.ActionPoints > 0 && guard < maxActionsPerUnit; guard++)
        {
            ApplyPace(u);
            List<Unit> visible = VisibleOpponents(u);
            stepDone = false;

            // what it sees, the whole squad (and the squads near it) knows
            foreach (Unit seen in visible)
                ReportSighting(u, seen);

            if (visible.Count > 0)
            {
                yield return Engage(u, visible);
            }
            else
            {
                EnemyGroup.Sighting lead = u.Group != null ? u.Group.BestLead(u, phaseCounter, alertTurns) : null;
                if (lead != null)
                    yield return Hunt(u, lead);
                else
                    yield return Search(u, m);
            }

            if (!stepDone)
                break;                          // nothing useful left to do
            yield return 0.1f;
        }

        if (u != null)
            Note(Tag(u) + " is done at " + u.Coords + " (points left " + u.ActionPoints + ")");
        yield return pauseBetweenUnits;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Intelligence
    // ---------------------------------------------------------------------------------------------------------------

    // The unit has seen 'target': the squad knows it now, and the squads within 20 tiles are told.
    private void ReportSighting(Unit u, Unit target)
    {
        EnemyGroup g = u.Group;
        if (g == null)
            return;

        bool changed;
        List<EnemyGroup> told = g.Report(target, target.Coords, phaseCounter, grid.TileSize, false, out changed);
        if (!changed && told.Count == 0)
            return;

        string others = "";
        foreach (EnemyGroup t in told)
            others += (others.Length > 0 ? ", " : "") + "G" + t.Id;
        Note(Tag(u) + " has seen " + target.name + " at " + target.Coords + (told.Count > 0 ? ": passed on to the squads " + others : ""));
    }

    // Somebody of the squad (or of a near squad) saw a unit of the player at 'lead.tile' lately; this unit does not see anybody: go and
    // look. First from where it is (turn if it is facing elsewhere), then from a better place that has a view of that tile.
    private IEnumerator Hunt(Unit u, EnemyGroup.Sighting lead)
    {
        Tile here = grid.GetTile(u.Coords);
        Tile spot = grid.GetTile(lead.tile);
        if (here == null || spot == null)
            yield break;

        float tileSize = grid.TileSize;
        Vector3 believed = FeetAt(spot) + Vector3.up * 0.55f;

        // can it see that tile right now? Then the sighting is wrong (nobody is there any more): the squad drops it.
        grid.ComputeVisibleTiles(u.transform.position, u.FacingDirection, u.ViewAngle, u.ViewRange, scratch);
        if (scratch.Contains(lead.tile))
        {
            u.Group.Disprove(lead.target, lead.tile);
            Note(Tag(u) + " looks at " + lead.tile + ": " + lead.target.name + " is not there any more");
            stepDone = true;
            yield break;
        }

        float distance = Vector3.Distance(FeetAt(here), FeetAt(spot)) ;
        bool inRange = distance <= u.ViewRange * tileSize;
        if (inRange && HasLine(FeetAt(here), FeetAt(spot)))
        {
            // the line is clear: only the way it faces is wrong
            if (u.TurnTo(believed))
            {
                stepDone = true;
                Note(Tag(u) + " turns to look at " + lead.tile + ", where " + lead.target.name + (lead.heard ? " was heard" : " was seen"));
                yield return WaitFacing(u, believed - u.transform.position, 1.5f);
            }
            yield break;
        }

        // otherwise: to a place that sees it
        Tile vantage = PlanVantage(u, spot, believed);
        if (vantage != null && vantage != here && u.MoveTo(vantage))
        {
            stepDone = true;
            Note(Tag(u) + " goes " + here.Coords + " -> " + vantage.Coords + " to look at " + lead.tile + ", where " + lead.target.name + (lead.heard ? " was heard" : " was seen"));
            Unit mover = u;
            yield return (Func<bool>)(() => mover == null || !mover.IsMoving);
            yield return 0.1f;
        }
    }

    // A tile within reach from which the spot can be seen (in range, nothing in the way), the one with the best cover from the spot and
    // the fewest steps; if there is none, the reachable tile that gets the unit nearest to the spot.
    private Tile PlanVantage(Unit u, Tile spot, Vector3 believed)
    {
        Tile here = grid.GetTile(u.Coords);
        if (here == null)
            return null;

        float tileSize = grid.TileSize;
        int budget = u.ActionPoints >= 2 ? u.ActionPoints - 1 : u.ActionPoints;      // keep a point for turning
        if (budget < 1)
            return null;

        Tile bestSeeing = null, bestNearer = null;
        float seeingScore = float.NegativeInfinity, nearerScore = float.NegativeInfinity;
        foreach (Tile t in Pathfinder.GetReachableTiles(grid, here, budget, u))
        {
            if (t == null)
                continue;

            int cost = Manhattan(here.Coords, t.Coords);
            float cover = CoverAgainst(t, believed);
            Vector3 feet = FeetAt(t);
            float distance = Vector3.Distance(feet, FeetAt(spot));

            if (distance <= u.ViewRange * tileSize && HasLine(feet, FeetAt(spot)))
            {
                float score = cover * coverWeight - cost * stepCost + UnityEngine.Random.value * 0.2f;
                if (score > seeingScore)
                {
                    seeingScore = score;
                    bestSeeing = t;
                }
            }

            float closer = -Manhattan(t.Coords, spot.Coords) + cover * 0.5f + UnityEngine.Random.value * 0.2f;
            if (closer > nearerScore)
            {
                nearerScore = closer;
                bestNearer = t;
            }
        }

        return bestSeeing != null ? bestSeeing : bestNearer;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Attack
    // ---------------------------------------------------------------------------------------------------------------

    private IEnumerator Engage(Unit u, List<Unit> visible)
    {
        Unit target = ChooseTarget(u, visible);
        if (target == null)
            yield break;

        if (!u.CanAffordShot)
        {
            yield return TakeCover(u, visible);
            yield break;
        }

        ChooseFireMode(u, target);                          // single shot or burst, whichever should do more
        AttackPlan plan = PlanAttack(u, target, visible);
        if (plan == null)
        {
            // there is no place to shoot from within reach: at least get into better cover
            Note(Tag(u) + " sees " + target.name + " but has no way to shoot at it from here or nearby");
            yield return TakeCover(u, visible);
            yield break;
        }

        Tile here = grid.GetTile(u.Coords);
        if (plan.tile != here)
        {
            Note(Tag(u) + " goes from " + u.Coords + " to " + plan.tile.Coords + " to shoot at " + target.name + " (cover " + AverageCover(plan.tile, visible).ToString("0.0") + ", score " + plan.score.ToString("0.0") + ")");
            if (u.MoveTo(plan.tile))
            {
                stepDone = true;
                Unit mover = u;
                yield return (Func<bool>)(() => mover == null || !mover.IsMoving);
                yield return 0.1f;
            }
        }

        // it has to see the target to shoot at it: turn if the way of walking has left it looking elsewhere
        if (u == null || !u.IsAlive || target == null || !target.IsAlive)
            yield break;
        if (!grid.CanSee(u, target))
        {
            if (u.TurnTo(target.transform.position))
            {
                stepDone = true;
                Note(Tag(u) + " turns to " + target.name);
                yield return WaitFacing(u, target.transform.position - u.transform.position, 1.5f);
            }
        }

        if (u != null)
            u.EnsureAffordableMode();
        if (grid.CanSee(u, target) && u.CanAffordCurrentShot)
        {
            stepDone = true;
            yield return FireAt(u, target);
        }
    }

    // The best tile to shoot at the target from (this one included), or null if there is none.
    private AttackPlan PlanAttack(Unit u, Unit target, List<Unit> visible)
    {
        Tile here = grid.GetTile(u.Coords);
        if (here == null)
            return null;

        int ap = u.ActionPoints;
        int shotCost = Mathf.Max(1, u.ShotCost);
        int turnCost = u.TurnCost;
        float tileSize = grid.TileSize;
        Vector3 chest = ChestOf(target);
        float muzzleHeight = Mathf.Max(0.6f, u.MuzzlePosition.y - u.transform.position.y);

        List<Tile> candidates = new List<Tile> { here };
        int budget = ap - shotCost;
        if (budget >= 1)
            candidates.AddRange(Pathfinder.GetReachableTiles(grid, here, budget, u));

        AttackPlan best = null;
        foreach (Tile t in candidates)
        {
            if (t == null)
                continue;

            bool isHere = t == here;
            int cost = isHere ? 0 : Manhattan(here.Coords, t.Coords);
            Vector3 feet = FeetAt(t);

            Vector3 toTarget = target.transform.position - feet;
            toTarget.y = 0f;
            float distance = toTarget.magnitude;
            if (distance > u.ViewRange * tileSize)
                continue;                                       // it would lose sight of it
            if (!HasLine(feet, target.transform.position))
                continue;                                       // a wall or a building between

            // arriving it looks the way it walked: must it turn to see the target?
            bool needsTurn = false;
            if (!isHere)
            {
                Vector2Int step = LastStep(here.Coords, t.Coords);
                Vector3 looking = new Vector3(step.x, 0f, step.y);
                needsTurn = Vector3.Angle(looking, toTarget) > u.ViewAngle * 0.5f - 1f;
            }

            int left = ap - cost - (needsTurn ? turnCost : 0);
            int shots = left / shotCost;
            if (shots < 1)
                continue;

            // can a bullet leave from there (leaning out of cover if need be)?
            Vector3 lean;
            if (!UnitPeek.CanClear(feet, muzzleHeight, u, target, chest, out lean))
                continue;

            float cover = AverageCover(t, visible);
            float exposure = 0f;
            foreach (Unit other in visible)
            {
                if (other == target)
                    continue;
                if ((other.transform.position - feet).magnitude <= other.ViewRange * tileSize && HasLine(feet, other.transform.position))
                    exposure += 1f;
            }

            float distanceInTiles = distance / tileSize;
            float rangePenalty = distanceInTiles > 11f ? (distanceInTiles - 11f) * 0.3f : distanceInTiles < 2.5f ? (2.5f - distanceInTiles) : 0f;

            float score = cover * coverWeight + shots * shotWeight - cost * stepCost - exposure * exposureWeight - rangePenalty
                          - (lean.sqrMagnitude > 1e-6f ? 0.3f : 0f) + (isHere ? stayBonus : 0f) + UnityEngine.Random.value * 0.2f;

            if (best == null || score > best.score)
                best = new AttackPlan { tile = t, score = score, cost = cost, needsTurn = needsTurn, shots = shots, lean = lean };
        }

        return best;
    }

    // A weapon with more than one way of firing: the unit picks the one that should do the most damage this turn against this target. For each
    // mode it works out the chance of a bullet hitting (a soldier covers roughly 0.18 square metres of the ring, whose size comes from the
    // distance and the mode's spread) and how many times it could fire the mode with the points it has; a burst wins up close, where the
    // bullets mostly land, and a single shot at a distance, where a burst only wastes bullets and points.
    private void ChooseFireMode(Unit u, Unit target)
    {
        WeaponData w = u.Weapon;
        if (w == null || w.FireModes.Count < 2)
            return;

        int ap = u.ActionPoints;
        float distance = Vector3.Distance(u.transform.position, target.transform.position);
        float ammoSpread = u.Ammo != null ? u.Ammo.SpreadMultiplier : 1f;
        int bestIndex = -1;
        float bestValue = -1f;
        for (int i = 0; i < w.FireModes.Count; i++)
        {
            FireModeData m = w.FireModes[i];
            if (m.apCost > ap || m.apCost <= 0)
                continue;

            float radius = Mathf.Max(0.14f, distance * Mathf.Tan(Mathf.Deg2Rad * w.SpreadDegrees * m.spreadMultiplier * ammoSpread));
            float chance = Mathf.Min(0.9f, 0.18f / (Mathf.PI * radius * radius));
            float value = (ap / m.apCost) * m.shots * chance;           // expected hits this turn if it keeps firing this mode

            if (bestIndex < 0 || value > bestValue + 1e-4f)
            {
                bestIndex = i;
                bestValue = value;
            }
        }

        if (bestIndex >= 0)
            u.SetFireMode(bestIndex);
    }

    // With points left but none for another shot (or nowhere to shoot from): get into better cover from the enemy.
    private IEnumerator TakeCover(Unit u, List<Unit> visible)
    {
        Tile here = grid.GetTile(u.Coords);
        if (here == null || u.ActionPoints < 1)
            yield break;

        float tileSize = grid.TileSize;
        float hereScore = CoverScore(here, 0, true, visible, tileSize);
        Tile best = null;
        float bestScore = hereScore;

        foreach (Tile t in Pathfinder.GetReachableTiles(grid, here, u.ActionPoints, u))
        {
            if (t == null)
                continue;
            float score = CoverScore(t, Manhattan(here.Coords, t.Coords), false, visible, tileSize);
            if (score > bestScore)
            {
                bestScore = score;
                best = t;
            }
        }

        if (best != null && bestScore >= hereScore + 1f && u.MoveTo(best))
        {
            stepDone = true;
            Note(Tag(u) + " moves from " + here.Coords + " to better cover at " + best.Coords);
            Unit mover = u;
            yield return (Func<bool>)(() => mover == null || !mover.IsMoving);
            yield return 0.1f;
        }
    }

    private float CoverScore(Tile t, int cost, bool isHere, List<Unit> visible, float tileSize)
    {
        Vector3 feet = FeetAt(t);
        float exposure = 0f;
        foreach (Unit other in visible)
        {
            if ((other.transform.position - feet).magnitude <= other.ViewRange * tileSize && HasLine(feet, other.transform.position))
                exposure += 1f;
        }
        return AverageCover(t, visible) * coverWeight - cost * stepCost - exposure * exposureWeight + (isHere ? stayBonus : 0f);
    }

    private IEnumerator FireAt(Unit u, Unit target)
    {
        UnitAnimator animator = u.GetComponentInChildren<UnitAnimator>();
        Glide(target.transform.position);                       // show where the shot goes

        // lean out of cover if the line is blocked right in front
        Vector3 chest = ChestOf(target);
        float muzzleHeight = Mathf.Max(0.6f, u.MuzzlePosition.y - u.transform.position.y);
        Vector3 lean;
        UnitPeek peek = u.GetComponent<UnitPeek>();
        if (UnitPeek.CanClear(u.transform.position, muzzleHeight, u, target, chest, out lean) && lean.sqrMagnitude > 1e-6f)
        {
            if (peek == null)
                peek = u.gameObject.AddComponent<UnitPeek>();
            peek.PeekTo(lean);
        }

        if (animator != null)
            animator.AimAt(target.transform.position);

        float start = now;
        UnitPeek leaning = peek;
        yield return (Func<bool>)(() => now - start > 0.45f && (leaning == null || (leaning.Offset - leaning.TargetOffset).sqrMagnitude < 1e-4f));

        if (u == null || !u.IsAlive || target == null || !target.IsAlive || !u.TrySpendActionPoints(u.ShotCost))
        {
            TidyUp(u, target);
            yield break;
        }

        // the exact shape of the target for the bullet
        UnitHitbox hitbox = target.GetComponent<UnitHitbox>();
        if (hitbox == null)
            hitbox = target.gameObject.AddComponent<UnitHitbox>();
        hitbox.Build();
        Physics.SyncTransforms();

        // the bullets of the shot, one after another (one for a single shot, three for a burst), each to its own random point of the ring
        int healthBefore = target.Health;
        FireModeData mode = u.FireMode;
        int bullets = u.ShotsPerFire;
        Note(Tag(u) + " fires at " + target.name + " from " + u.Coords + (bullets > 1 ? " (" + mode.displayName + " x" + bullets + ")" : "") + " (points left " + u.ActionPoints + ")");
        for (int i = 0; i < bullets && u != null && u.IsAlive && target != null && target.IsAlive; i++)
        {
            if (animator != null)
                animator.PlayShoot();

            Vector3 muzzle = u.MuzzlePosition;
            Vector3 aim = RandomAimPoint(muzzle, chest, SpreadRing.SpreadRadius(u, target));
            Bullet.Fire(u, target, muzzle, aim, u.WeaponDamage, u.BulletSpeed);
            if (i < bullets - 1)
                yield return u.ShotInterval;
        }

        yield return 0.05f;
        float fired = now;
        yield return (Func<bool>)(() => !Bullet.AnyInFlight || now - fired > 3f);      // a bullet that hit nothing dies by itself
        yield return 0.5f;

        Note("   -> " + Bullet.LastResult + (target != null ? ", " + target.name + " health " + healthBefore + " -> " + target.Health : ""));
        TidyUp(u, target);
        yield return (Func<bool>)(() => leaning == null || u == null || leaning.Offset.sqrMagnitude < 1e-4f);
    }

    // After a shot (or if a shot is cut short): the rifle down, back out of the lean, the target's exact body shape taken away.
    private void TidyUp(Unit shooter, Unit target)
    {
        if (shooter != null)
        {
            UnitAnimator animator = shooter.GetComponentInChildren<UnitAnimator>();
            if (animator != null)
                animator.StopAiming();
            UnitPeek peek = shooter.GetComponent<UnitPeek>();
            if (peek != null)
                peek.Return();
        }

        if (target != null)
        {
            RemoveHitbox(target);
        }
        else
        {
            foreach (Unit o in Unit.All)
            {
                if (o != null && o.Faction == opponentFaction)
                    RemoveHitbox(o);
            }
        }
    }

    private static void RemoveHitbox(Unit unit)
    {
        UnitHitbox hitbox = unit.GetComponent<UnitHitbox>();
        if (hitbox == null)
            return;
        hitbox.Clear();
        if (Application.isPlaying) Destroy(hitbox); else DestroyImmediate(hitbox);
    }

    private static Vector3 RandomAimPoint(Vector3 muzzle, Vector3 chest, float radius)
    {
        Vector3 direction = (chest - muzzle).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, direction);
        if (right.sqrMagnitude < 1e-6f)
            right = Vector3.right;
        right.Normalize();
        Vector3 up = Vector3.Cross(direction, right);

        float r = radius * Mathf.Sqrt(UnityEngine.Random.value);
        float a = UnityEngine.Random.value * Mathf.PI * 2f;
        return chest + right * (r * Mathf.Cos(a)) + up * (r * Mathf.Sin(a));
    }

    private Unit ChooseTarget(Unit u, List<Unit> visible)
    {
        Unit best = null;
        float bestScore = float.NegativeInfinity;
        foreach (Unit t in visible)
        {
            float distance = Vector3.Distance(u.transform.position, t.transform.position);
            float score = -distance + (t.MaxHealth - t.Health) * 0.03f;     // near, and wounded ones first
            if (score > bestScore)
            {
                bestScore = score;
                best = t;
            }
        }
        return best;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Search
    // ---------------------------------------------------------------------------------------------------------------

    private IEnumerator Search(Unit u, Memory m)
    {
        // first, once a turn: walk towards good ground (a place with cover around it and a wide view, chosen from a wide area and
        // kept until it is reached), as far as the points allow while keeping one for looking around
        if (!m.moveEvaluated && u.ActionPoints >= 2)
        {
            m.moveEvaluated = true;
            Tile goal = m.hasGoal ? grid.GetTile(m.goal) : null;
            if (goal == null || u.Coords == m.goal)
            {
                goal = ChooseGoal(u, m);
                m.goal = goal != null ? goal.Coords : u.Coords;
                m.hasGoal = goal != null;
            }

            if (goal != null && goal.Coords != u.Coords)
            {
                Tile next = NextStepToward(u, goal);
                if (next == null)
                {
                    m.hasGoal = false;                  // no way there any more
                }
                else
                {
                    Vector2Int from = u.Coords;
                    if (u.MoveTo(next))
                    {
                        stepDone = true;
                        Note(Tag(u) + " heads for better ground at " + goal.Coords + " (cover around " + CoverAround(goal) + ", view " + Overview(goal).ToString("0.00") + "): " + from + " -> " + next.Coords);
                        Unit mover = u;
                        yield return (Func<bool>)(() => mover == null || !mover.IsMoving);
                        yield return 0.1f;
                        AddCurrentView(u, m);
                        yield break;
                    }
                }
            }
        }
        m.moveEvaluated = true;

        // then look around with what is left
        if (u.ActionPoints >= u.TurnCost)
        {
            Vector3 direction;
            if (ChooseLookDirection(u, m, out direction))
            {
                if (u.TurnTo(u.transform.position + direction * 5f))
                {
                    stepDone = true;
                    Note(Tag(u) + " looks around: turns to " + Mathf.RoundToInt(Vector3.SignedAngle(Vector3.forward, direction, Vector3.up)) + " deg");
                    yield return WaitFacing(u, direction, 1.5f);
                    AddCurrentView(u, m);
                }
            }
        }
    }

    // Good ground to settle in, chosen from everything within goalRadius tiles: cover around it, a wide view, near the rest of the squad
    // but not crowded, and not too far to walk. The unit's own tile is kept unless another is clearly better (searchHysteresis).
    private Tile ChooseGoal(Unit u, Memory m)
    {
        Tile here = grid.GetTile(u.Coords);
        if (here == null)
            return null;

        Tile best = here;
        float bestScore = SpotScore(here, 0, u) + searchHysteresis;
        foreach (Tile t in grid.Tiles)
        {
            if (t == null || t == here || t.IsBlocked)
                continue;
            int distance = Manhattan(here.Coords, t.Coords);
            if (distance > goalRadius || Unit.IsOccupied(t, u))
                continue;

            float score = SpotScore(t, 0, u) - distance * goalDistanceCost;
            if (score <= bestScore)
                continue;

            List<Tile> path = Pathfinder.FindPath(grid, here, t, u);
            if (path == null)
                continue;                               // it cannot be reached

            best = t;
            bestScore = score;
        }
        return best;
    }

    // The farthest tile along the way to the goal that the unit can reach this turn, keeping one point for looking around.
    private Tile NextStepToward(Unit u, Tile goal)
    {
        Tile here = grid.GetTile(u.Coords);
        if (here == null)
            return null;

        List<Tile> path = Pathfinder.FindPath(grid, here, goal, u);
        if (path == null || path.Count < 2)
            return null;

        int budget = u.ActionPoints - 1;
        Tile reach = null;
        for (int i = 1; i < path.Count; i++)
        {
            if (grid.PathCost(path.GetRange(0, i + 1)) > budget)
                break;
            reach = path[i];
        }
        return reach;
    }

    private float SpotScore(Tile t, int cost, Unit u)
    {
        float score = Mathf.Min(4, CoverAround(t)) * searchCoverWeight + Overview(t) * overviewWeight - cost * searchStepCost;

        // not on top of the others: the members of the squad may stand near each other, others must keep a little farther away
        foreach (Unit other in Unit.All)
        {
            if (other == null || other == u || other.Faction != aiFaction || !other.IsAlive)
                continue;
            int d = Manhattan(other.Coords, t.Coords);
            bool mate = u.Group != null && other.Group == u.Group;
            if (d <= (mate ? 1 : 2))
                score -= 1f;
        }

        // nor on the places the others are heading for
        foreach (KeyValuePair<Unit, Memory> other in memories)
        {
            if (other.Key == null || other.Key == u || !other.Key.IsAlive || !other.Value.hasGoal)
                continue;
            bool mate = u.Group != null && other.Key.Group == u.Group;
            if (Manhattan(other.Value.goal, t.Coords) <= (mate ? 1 : 2))
                score -= 1f;
        }

        // stay with the squad
        if (u.Group != null)
        {
            Vector2 centre = Vector2.zero;
            int n = 0;
            foreach (Unit mate in u.Group.Members)
            {
                if (mate == null || mate == u || !mate.IsAlive)
                    continue;
                Memory mm;
                Vector2Int at = memories.TryGetValue(mate, out mm) && mm.hasGoal ? mm.goal : mate.Coords;
                centre += new Vector2(at.x, at.y);
                n++;
            }
            if (n > 0)
            {
                centre /= n;
                float apart = Mathf.Abs(t.Coords.x - centre.x) + Mathf.Abs(t.Coords.y - centre.y);
                score -= Mathf.Max(0f, apart - cohesionRadius) * 0.9f;
            }
        }

        return score;
    }

    // Where to look next: the direction (of twelve) that shows the most ground that nobody of the squad has looked at yet this turn; a
    // small turn is preferred.
    private bool ChooseLookDirection(Unit u, Memory m, out Vector3 direction)
    {
        direction = Vector3.forward;
        Vector3 facing = u.FacingDirection;
        facing.y = 0f;
        HashSet<Vector2Int> looked = SeenSet(u, m);

        float bestScore = 0f;
        bool found = false;
        for (int k = 0; k < 12; k++)
        {
            Vector3 d = Quaternion.Euler(0f, k * 30f, 0f) * Vector3.forward;
            grid.ComputeVisibleTiles(u.transform.position, d, u.ViewAngle, u.ViewRange, scratch);
            int fresh = 0;
            foreach (Vector2Int c in scratch)
            {
                if (!looked.Contains(c))
                    fresh++;
            }

            float turn = Vector3.Angle(facing, d);
            float score = fresh - turn * 0.03f;
            if (fresh >= 4 && turn >= Unit.MinTurnAngle + 1f && (!found || score > bestScore))
            {
                bestScore = score;
                direction = d;
                found = true;
            }
        }
        return found;
    }

    // The tiles looked at this turn: the squad's, shared by all its members.
    private HashSet<Vector2Int> SeenSet(Unit u, Memory m)
    {
        return u.Group != null ? u.Group.SeenThisTurn : m.seen;
    }

    private void AddCurrentView(Unit u, Memory m)
    {
        grid.ComputeVisibleTiles(u.transform.position, u.FacingDirection, u.ViewAngle, u.ViewRange, scratch);
        SeenSet(u, m).UnionWith(scratch);
    }

    private IEnumerator WaitFacing(Unit u, Vector3 direction, float timeout)
    {
        float start = now;
        direction.y = 0f;
        while (u != null && Vector3.Angle(u.FacingDirection, direction) > 4f && now - start < timeout)
            yield return null;
        yield return 0.1f;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Looking at the ground
    // ---------------------------------------------------------------------------------------------------------------

    private List<Unit> VisibleOpponents(Unit u)
    {
        // the field of view is worked out once, then every opponent's tile is looked up in it
        List<Unit> result = new List<Unit>();
        grid.ComputeVisibleTiles(u.transform.position, u.FacingDirection, u.ViewAngle, u.ViewRange, scratch);
        foreach (Unit o in Unit.All)
        {
            if (o != null && o.Faction == opponentFaction && o.IsAlive && scratch.Contains(o.Coords))
                result.Add(o);
        }
        return result;
    }

    private float NearestOpponentDistance(Unit u)
    {
        float best = float.PositiveInfinity;
        foreach (Unit o in Unit.All)
        {
            if (o == null || o.Faction != opponentFaction || !o.IsAlive)
                continue;
            best = Mathf.Min(best, (o.transform.position - u.transform.position).sqrMagnitude);
        }
        return best;
    }

    private float NearestOpponentDistance(EnemyGroup g)
    {
        float best = float.PositiveInfinity;
        foreach (Unit m in g.Members)
        {
            if (m != null && m.IsAlive)
                best = Mathf.Min(best, NearestOpponentDistance(m));
        }
        return best;
    }

    private static float CoverValue(CoverType c)
    {
        return c == CoverType.Full ? 2f : c == CoverType.Half ? 1f : 0f;
    }

    // Cover of a tile against a point: what stands on the tile on that side (a wall on the edge, a tall thing on the next tile).
    // 0 none, 1 half, 2 full; when the enemy is off to the side, the second side counts a bit too.
    private float CoverAgainst(Tile t, Vector3 from)
    {
        Vector3 d = from - t.transform.position;
        d.y = 0f;
        if (d.sqrMagnitude < 0.01f)
            return 0f;

        float ax = Mathf.Abs(d.x), az = Mathf.Abs(d.z);
        int primary = ax >= az ? (d.x > 0f ? 1 : 3) : (d.z > 0f ? 0 : 2);        // 0 north (+z), 1 east (+x), 2 south, 3 west
        int secondary = ax >= az ? (d.z > 0f ? 0 : 2) : (d.x > 0f ? 1 : 3);
        float minor = Mathf.Min(ax, az) / Mathf.Max(ax, az);

        float value = CoverValue(grid.GetCover(t.Coords, primary));
        if (minor > 0.5f)
            value = Mathf.Max(value, 0.6f * CoverValue(grid.GetCover(t.Coords, secondary)));
        return value;
    }

    private float AverageCover(Tile t, List<Unit> threats)
    {
        if (threats == null || threats.Count == 0)
            return 0f;
        float sum = 0f;
        foreach (Unit o in threats)
            sum += CoverAgainst(t, o.transform.position);
        return sum / threats.Count;
    }

    // All the cover around a tile, in all four directions (0..8).
    private int CoverAround(Tile t)
    {
        int sum = 0;
        for (int d = 0; d < 4; d++)
            sum += (int)CoverValue(grid.GetCover(t.Coords, d));
        return sum;
    }

    // How far one sees from a tile in eight directions (walls and buildings count): 0 (boxed in) to 1 (open).
    private float Overview(Tile t)
    {
        Vector3 origin = FeetAt(t) + Vector3.up * 1f;
        float sum = 0f;
        for (int k = 0; k < 8; k++)
        {
            Vector3 d = Quaternion.Euler(0f, k * 45f, 0f) * Vector3.forward;
            sum += Mathf.Min(10f, grid.VisibleDistance(origin, d, 10f * grid.TileSize) / grid.TileSize) / 10f;
        }
        return sum / 8f;
    }

    // Is there a clear line of sight (no wall, no tall building) between two places on the ground?
    private bool HasLine(Vector3 fromFeet, Vector3 toFeet)
    {
        Vector3 d = toFeet - fromFeet;
        d.y = 0f;
        float distance = d.magnitude;
        if (distance < 0.1f)
            return true;
        return grid.VisibleDistance(fromFeet + Vector3.up * 0.5f, d, distance) >= distance - 0.05f;
    }

    private Vector3 FeetAt(Tile t)
    {
        if (float.IsNaN(groundTop))
        {
            Tile origin = grid.GetTile(Vector2Int.zero);
            Renderer r = origin != null ? origin.GetComponent<Renderer>() : null;
            groundTop = r != null ? r.bounds.max.y : 0.5f;
        }

        Vector3 p = t.transform.position;
        return new Vector3(p.x, t.HasRoof ? t.RoofY : groundTop, p.z);
    }

    private static Vector3 ChestOf(Unit target)
    {
        return target.transform.position + Vector3.up * (0.55f - target.StanceChestDrop);
    }

    private static int Manhattan(Vector2Int a, Vector2Int b)
    {
        return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
    }

    private static Vector2Int LastStep(Vector2Int from, Vector2Int to)
    {
        Vector2Int d = to - from;
        return Mathf.Abs(d.x) >= Mathf.Abs(d.y) ? new Vector2Int(Math.Sign(d.x), 0) : new Vector2Int(0, Math.Sign(d.y));
    }

    private Memory MemoryOf(Unit u)
    {
        Memory m;
        if (!memories.TryGetValue(u, out m))
        {
            m = new Memory();
            memories[u] = m;
        }
        return m;
    }

    // "G2 EnemyUnit_3": the squad and the unit, for the journal.
    private static string Tag(Unit u)
    {
        return (u.Group != null ? "G" + u.Group.Id + " " : "") + u.name;
    }

    private void Note(string text)
    {
        Journal.Add(text);
        if (Journal.Count > 600)
            Journal.RemoveRange(0, 150);
        if (logDecisions)
            Debug.Log("[EnemyAI] " + text);
    }
}
