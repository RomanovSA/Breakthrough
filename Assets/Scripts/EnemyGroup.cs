using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A group (squad) of enemy units: 2 to 4 of them that spawn together and share what they find out.
///
/// What the group knows is a list of sightings: "somebody of the player was seen at this tile in that enemy turn". Any member that sees
/// a unit of the player puts it there at once, and every member can use it (EnemyAI: the others go and look for it). The ground
/// the members have looked at this turn is shared too, so that they do not all look the same way.
///
/// Groups share their sightings with other groups, but only near ones: when two groups have members within ShareDistance tiles of each
/// other, a sighting of one is given to the other at once (one step: the other does not pass it on). A group farther away than that
/// is local: it knows nothing of what the near groups saw.
/// Sightings get old: after a few enemy turns (EnemyAI) they are no longer used, and a sighting that a unit has gone to check and
/// found empty is dropped.
/// Units get their group from UnitSpawner (Unit.Group); a unit without one is a group of one (EnemyAI makes it).
/// </summary>
public class EnemyGroup
{
    /// <summary>"The unit of the player was seen on this tile in that enemy turn."</summary>
    public class Sighting
    {
        public Unit target;
        public Vector2Int tile;
        public int turn;
        public bool fromOtherGroup;
        public bool heard;               // heard, not seen: the place is only roughly right
    }

    /// <summary>How near (in tiles) two groups must be for one to share its sightings with the other.</summary>
    public const float ShareDistance = 20f;

    /// <summary>All the groups of the game (UnitSpawner makes them anew every time it spawns).</summary>
    public static readonly List<EnemyGroup> All = new List<EnemyGroup>();

    private static int nextId = 1;

    private readonly Dictionary<Unit, Sighting> intel = new Dictionary<Unit, Sighting>();

    /// <summary>The tiles the members have looked at in this enemy turn (shared, so the members look in different directions).</summary>
    public readonly HashSet<Vector2Int> SeenThisTurn = new HashSet<Vector2Int>();

    public int Id { get; }

    public readonly List<Unit> Members = new List<Unit>();

    public EnemyGroup(IEnumerable<Unit> members)
    {
        Id = nextId++;
        foreach (Unit m in members)
        {
            if (m == null)
                continue;
            Members.Add(m);
            m.Group = this;
        }
        All.Add(this);
    }

    /// <summary>Forgets all the groups (a new game, a new field).</summary>
    public static void Reset()
    {
        All.Clear();
        nextId = 1;
    }

    public int AliveCount
    {
        get
        {
            int n = 0;
            foreach (Unit m in Members)
            {
                if (m != null && m.IsAlive)
                    n++;
            }
            return n;
        }
    }

    /// <summary>What the group knows now (the sightings; old ones included).</summary>
    public IEnumerable<Sighting> Sightings => intel.Values;

    /// <summary>The sighting of this unit of the player, or null if the group has none.</summary>
    public Sighting SightingOf(Unit target)
    {
        Sighting s;
        return target != null && intel.TryGetValue(target, out s) ? s : null;
    }

    /// <summary>The distance, in tiles, between the nearest members of the two groups (infinity if one has nobody alive).</summary>
    public float DistanceTo(EnemyGroup other, float tileSize)
    {
        float best = float.PositiveInfinity;
        foreach (Unit a in Members)
        {
            if (a == null || !a.IsAlive)
                continue;
            foreach (Unit b in other.Members)
            {
                if (b == null || !b.IsAlive)
                    continue;
                Vector3 d = a.transform.position - b.transform.position;
                d.y = 0f;
                best = Mathf.Min(best, d.magnitude / Mathf.Max(0.01f, tileSize));
            }
        }
        return best;
    }

    /// <summary>
    /// A member has seen (or, with 'heard', only heard) 'target' at 'tile' in enemy turn number 'turn': the group knows it now, and so do the groups within
    /// ShareDistance. Returns the groups that learned something new from it (the list is empty if nobody did); 'changed' tells whether
    /// this group itself learned something new (a new place, or the first time).
    /// </summary>
    public List<EnemyGroup> Report(Unit target, Vector2Int tile, int turn, float tileSize, bool heard, out bool changed)
    {
        List<EnemyGroup> told = new List<EnemyGroup>();
        changed = Learn(target, tile, turn, false, heard);

        foreach (EnemyGroup other in All)
        {
            if (other == this || other.AliveCount == 0)
                continue;
            if (DistanceTo(other, tileSize) <= ShareDistance && other.Learn(target, tile, turn, true, heard))
                told.Add(other);
        }
        return told;
    }

    private bool Learn(Unit target, Vector2Int tile, int turn, bool fromOther, bool heard)
    {
        if (target == null)
            return false;

        Sighting s;
        if (intel.TryGetValue(target, out s))
        {
            if (s.turn > turn)
                return false;                           // it already knows something newer
            if (s.turn == turn && !s.heard && heard)
                return false;                           // it has seen the place in this very turn: a noise is not better than that
            bool same = s.tile == tile && s.turn == turn;
            if (same)
                return false;
            bool newPlace = s.tile != tile;
            s.tile = tile;
            s.turn = turn;
            s.fromOtherGroup = fromOther;
            s.heard = heard;
            return newPlace;
        }

        intel[target] = new Sighting { target = target, tile = tile, turn = turn, fromOtherGroup = fromOther, heard = heard };
        return true;
    }

    /// <summary>
    /// The newest sighting that is not older than maxAge enemy turns, of a unit that is still alive; with the same age the one nearest
    /// to 'from'. Null if the group has none.
    /// </summary>
    public Sighting BestLead(Unit from, int currentTurn, int maxAge)
    {
        Sighting best = null;
        float bestDistance = float.PositiveInfinity;
        foreach (Sighting s in intel.Values)
        {
            if (s.target == null || !s.target.IsAlive || currentTurn - s.turn > maxAge)
                continue;

            float distance = Mathf.Abs(s.tile.x - from.Coords.x) + Mathf.Abs(s.tile.y - from.Coords.y);
            if (best == null || s.turn > best.turn || (s.turn == best.turn && distance < bestDistance))
            {
                best = s;
                bestDistance = distance;
            }
        }
        return best;
    }

    /// <summary>A member has looked at this tile and the unit is not there: the group drops that sighting.</summary>
    public void Disprove(Unit target, Vector2Int tile)
    {
        Sighting s;
        if (target != null && intel.TryGetValue(target, out s) && s.tile == tile)
            intel.Remove(target);
    }

    /// <summary>Drops what is about units that are dead or gone.</summary>
    public void Prune()
    {
        List<Unit> gone = new List<Unit>();
        foreach (KeyValuePair<Unit, Sighting> pair in intel)
        {
            if (pair.Key == null || !pair.Key.IsAlive)
                gone.Add(pair.Key);
        }
        foreach (Unit u in gone)
            intel.Remove(u);
    }

    /// <summary>Forgets every sighting (a new game).</summary>
    public void ForgetAll()
    {
        intel.Clear();
        SeenThisTurn.Clear();
    }
}
