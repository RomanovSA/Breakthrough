using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Spawns units on the tiles of the field every time the field is (re)built.
/// What is spawned is described by a list of groups: a unit prefab, how many of it and, if wanted, another weapon for them than the one
/// the prefab carries (Weapon). The prefab decides the faction (for example one group of player units and one of enemies).
///
/// Where they stand:
///  - The units of the Edge Faction (the player) spawn together, in one cluster at one of the four edges of the field (chosen at random):
///    the first tile is within Edge Depth tiles of the edge, the others are the nearest free tiles around it. All the groups of this faction
///    together make that one cluster.
///  - The units of the Squad Faction (the enemies) spawn in squads of Squad Min to Squad Max units (all the groups of this faction together
///    are split into squads of those sizes, for example 9 = 3 + 3 + 3 or 4 + 3 + 2; the units of the different groups are shuffled, so the
///    weapons are mixed among the squads): the members stand close together (within Squad Spread tiles of the squad's first tile) and
///    the squads stand apart (at least Squad Min Distance tiles). No unit of a squad stands closer than Enemy Min Distance tiles to any unit
///    of the Edge Faction. Each squad is an EnemyGroup, which the members share their intelligence through.
///  - Units of any other faction stand each on its own random tile.
/// The player's group is placed first, whatever its place in the list, because the enemies keep away from it.
/// If the field has no room for the wanted distances, the units are put as far apart as the field allows (a warning is written).
///
/// Right-click the component header and choose "Spawn" to respawn everything by hand in the editor.
/// The spawned units are parented to this object; any previous ones are removed first.
/// </summary>
public class UnitSpawner : MonoBehaviour
{
    [System.Serializable]
    private class SpawnGroup
    {
        [Tooltip("The unit to spawn. Its Faction field decides which side the units belong to.")]
        public Unit prefab;

        [Min(0)] public int count = 1;

        [Tooltip("Another weapon for these units than the one on the prefab (empty = the prefab's own).")]
        public WeaponData weapon;
    }

    private struct Slot
    {
        public Unit prefab;
        public WeaponData weapon;
    }

    [SerializeField] private GridManager grid;
    [SerializeField] private List<SpawnGroup> groups = new List<SpawnGroup>();

    [Header("Player side")]
    [Tooltip("The units of this faction spawn together at one edge of the field.")]
    [SerializeField] private Faction edgeFaction = Faction.Player;
    [Tooltip("The first tile of the cluster lies within this many tiles of the edge.")]
    [SerializeField, Min(0)] private int edgeDepth = 3;
    [Tooltip("No enemy spawns closer than this many tiles to a unit of the player.")]
    [SerializeField, Min(0)] private float enemyMinDistance = 20f;

    [Header("Squads")]
    [Tooltip("The units of this faction spawn in squads.")]
    [SerializeField] private Faction squadFaction = Faction.Enemy;
    [SerializeField, Min(1)] private int squadMin = 2;
    [SerializeField, Min(1)] private int squadMax = 4;
    [Tooltip("The members of a squad stand within this many tiles of its first tile.")]
    [SerializeField, Min(1)] private int squadSpread = 2;
    [Tooltip("Squads stand at least this many tiles apart (if the field has room for it).")]
    [SerializeField, Min(0)] private float squadMinDistance = 8f;

    private bool spawned;

    /// <summary>Raised after all units have been spawned (and placed on their tiles).</summary>
    public event System.Action UnitsSpawned;

    /// <summary>The edge of the field the player's units spawned at last time: 0 west (x small), 1 east, 2 south (y small), 3 north; -1 if none.</summary>
    public int LastEdge { get; private set; } = -1;

    private void OnEnable()
    {
        if (grid == null)
            grid = FindFirstObjectByType<GridManager>();
        if (grid != null)
            grid.GridBuilt += OnGridBuilt;
    }

    private void OnDisable()
    {
        if (grid != null)
            grid.GridBuilt -= OnGridBuilt;
    }

    private void Start()
    {
        // If the field was already built before we started listening, spawn now.
        if (!spawned)
            Spawn();
    }

    private void OnGridBuilt()
    {
        Spawn();
    }

    [ContextMenu("Spawn")]
    public List<Unit> Spawn()
    {
        List<Unit> result = new List<Unit>();

        if (grid == null)
            grid = FindFirstObjectByType<GridManager>();

        if (grid == null)
        {
            Debug.LogWarning("UnitSpawner: GridManager is not assigned.", this);
            return result;
        }

        // free tiles of the main, connected part of the field (not under a building or a car, not in a closed-off pocket)
        HashSet<Vector2Int> mainRegion = grid.GetLargestFreeRegion();
        Tile[] tiles = System.Array.FindAll(grid.GetComponentsInChildren<Tile>(), t => mainRegion.Contains(t.Coords) && !t.IsBlocked);
        if (tiles.Length == 0)
        {
            Debug.LogWarning("UnitSpawner: the field has no tiles. Build the field first.", this);
            return result;
        }

        Clear();
        EnemyGroup.Reset();
        LastEdge = -1;

        // Shuffle the tiles (Fisher-Yates) and hand them out one by one, so no two units share a tile.
        for (int i = tiles.Length - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            Tile tmp = tiles[i];
            tiles[i] = tiles[j];
            tiles[j] = tmp;
        }

        Dictionary<Vector2Int, Tile> byCoords = new Dictionary<Vector2Int, Tile>();
        foreach (Tile t in tiles)
            byCoords[t.Coords] = t;

        // every unit to be spawned is a slot: a prefab and a weapon; the slots are sorted by how they are placed
        List<Slot> edgeSlots = new List<Slot>();
        List<Slot> squadSlots = new List<Slot>();
        List<Slot> otherSlots = new List<Slot>();
        foreach (SpawnGroup group in groups)
        {
            if (group == null || group.prefab == null)
            {
                Debug.LogWarning("UnitSpawner: a spawn group has no prefab.", this);
                continue;
            }

            for (int i = 0; i < group.count; i++)
            {
                Slot slot = new Slot { prefab = group.prefab, weapon = group.weapon };
                if (group.prefab.Faction == edgeFaction)
                    edgeSlots.Add(slot);
                else if (group.prefab.Faction == squadFaction)
                    squadSlots.Add(slot);
                else
                    otherSlots.Add(slot);
            }
        }

        HashSet<Vector2Int> used = new HashSet<Vector2Int>();
        List<Vector2Int> squadAnchors = new List<Vector2Int>();
        List<Vector2Int> edgeUnits = new List<Vector2Int>();       // where the units of the Edge Faction stand: the enemies keep away from them
        Dictionary<Faction, int> serials = new Dictionary<Faction, int>();

        // the player's units first: one cluster at one edge
        if (edgeSlots.Count > 0)
        {
            List<Tile> places = ChooseEdgePlaces(edgeSlots.Count, tiles, byCoords, used);
            for (int i = 0; i < places.Count && i < edgeSlots.Count; i++)
            {
                Unit unit = SpawnSlot(edgeSlots[i], places[i], used, serials);
                if (unit == null)
                    continue;
                edgeUnits.Add(places[i].Coords);
                result.Add(unit);
            }
        }

        // the enemy: the units of all the groups are shuffled (so that the weapons are mixed), then split into squads
        if (squadSlots.Count > 0)
        {
            for (int i = squadSlots.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                Slot tmp = squadSlots[i];
                squadSlots[i] = squadSlots[j];
                squadSlots[j] = tmp;
            }

            int next = 0;
            foreach (int size in SquadSizes(squadSlots.Count, squadMin, Mathf.Max(squadMin, squadMax)))
            {
                List<Tile> places = ChooseSquadPlaces(size, tiles, byCoords, used, squadAnchors, edgeUnits);
                List<Unit> members = new List<Unit>();
                for (int i = 0; i < places.Count && next < squadSlots.Count; i++)
                {
                    Unit unit = SpawnSlot(squadSlots[next++], places[i], used, serials);
                    if (unit == null)
                        continue;
                    result.Add(unit);
                    members.Add(unit);
                }

                if (members.Count > 0)
                    new EnemyGroup(members);
                if (places.Count < size)
                    Debug.LogWarning("UnitSpawner: there was room for only " + places.Count + " of " + size + " units of a squad.", this);
            }
        }

        // anybody else: each on its own random tile
        int nextTile = 0;
        foreach (Slot slot in otherSlots)
        {
            while (nextTile < tiles.Length && used.Contains(tiles[nextTile].Coords))
                nextTile++;
            if (nextTile >= tiles.Length)
            {
                Debug.LogWarning("UnitSpawner: there are more units than tiles, the rest was not spawned.", this);
                break;
            }

            Unit unit = SpawnSlot(slot, tiles[nextTile++], used, serials);
            if (unit != null)
                result.Add(unit);
        }

        spawned = true;
        UnitsSpawned?.Invoke();
        return result;
    }

    // One unit: made from its prefab, given its weapon (if the group names one), named and put on the tile. Null if the tile was taken.
    private Unit SpawnSlot(Slot slot, Tile place, HashSet<Vector2Int> used, Dictionary<Faction, int> serials)
    {
        Unit unit = SpawnUnit(slot.prefab);
        int serial;
        serials.TryGetValue(unit.Faction, out serial);
        serials[unit.Faction] = ++serial;
        unit.name = unit.Faction + "Unit_" + serial;

        if (slot.weapon != null)
            unit.SetWeapon(slot.weapon);

        if (!unit.PlaceOn(place))
        {
            Debug.LogWarning("UnitSpawner: the tile was already taken, the unit was not spawned.", this);
            DestroyImmediate(unit.gameObject);
            return null;
        }

        used.Add(place.Coords);
        return unit;
    }

    /// <summary>Splits 'count' units into squads of min to max units (for example 9 into 3 + 3 + 3); never leaves a squad of one.</summary>
    public static List<int> SquadSizes(int count, int min, int max)
    {
        List<int> sizes = new List<int>();
        int left = count;
        while (left > 0)
        {
            if (left <= max)
            {
                sizes.Add(left);
                break;
            }

            int size = Random.Range(min, max + 1);
            if (left - size < min)
                size = left - min;
            sizes.Add(size);
            left -= size;
        }
        return sizes;
    }

    // How far a tile is from the given edge of the field (0 west, 1 east, 2 south, 3 north), in tiles.
    private int EdgeDistance(Vector2Int c, int edge)
    {
        Vector2Int size = grid.Size;
        switch (edge)
        {
            case 0: return c.x;
            case 1: return size.x - 1 - c.x;
            case 2: return c.y;
            default: return size.y - 1 - c.y;
        }
    }

    // The tiles for the player's units: one of the four edges at random (another one if that edge has no room), and at that edge a first
    // tile (within Edge Depth of it) with the nearest free tiles around it.
    private List<Tile> ChooseEdgePlaces(int count, Tile[] shuffled, Dictionary<Vector2Int, Tile> byCoords, HashSet<Vector2Int> used)
    {
        List<int> edges = new List<int> { 0, 1, 2, 3 };
        for (int i = edges.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            int tmp = edges[i];
            edges[i] = edges[j];
            edges[j] = tmp;
        }

        foreach (int edge in edges)
        {
            foreach (Tile candidate in shuffled)
            {
                if (used.Contains(candidate.Coords) || EdgeDistance(candidate.Coords, edge) > edgeDepth)
                    continue;

                List<Tile> places = GatherAround(candidate, count, byCoords, used, Mathf.Max(squadSpread, Mathf.CeilToInt(Mathf.Sqrt(count))));
                if (places.Count >= count)
                {
                    LastEdge = edge;
                    return places;
                }
            }
        }

        Debug.LogWarning("UnitSpawner: no edge of the field has room for the player's units; they were put on random tiles.", this);
        List<Tile> any = new List<Tile>();
        foreach (Tile t in shuffled)
        {
            if (!used.Contains(t.Coords))
                any.Add(t);
            if (any.Count >= count)
                break;
        }
        return any;
    }

    // The tiles for one squad: a first tile far from the other squads and from the player's units, and the nearest free tiles around it.
    private List<Tile> ChooseSquadPlaces(int size, Tile[] shuffled, Dictionary<Vector2Int, Tile> byCoords, HashSet<Vector2Int> used, List<Vector2Int> anchors, List<Vector2Int> keepAway)
    {
        Tile anchor = null;
        List<Tile> best = null;
        float bestScore = float.NegativeInfinity;                // for the fallback: how near the wishes it came (higher is better)

        int tries = 0;
        foreach (Tile candidate in shuffled)
        {
            if (used.Contains(candidate.Coords))
                continue;
            if (++tries > 1500)
                break;

            // how far is it from the nearest other squad?
            float spacing = float.PositiveInfinity;
            foreach (Vector2Int a in anchors)
                spacing = Mathf.Min(spacing, Vector2Int.Distance(a, candidate.Coords));

            List<Tile> places = GatherAround(candidate, size, byCoords, used, squadSpread);
            if (places.Count < size)
                continue;                                   // too cramped here

            // how far is the nearest of its units from the nearest unit of the player?
            float away = float.PositiveInfinity;
            foreach (Tile p in places)
                foreach (Vector2Int e in keepAway)
                    away = Mathf.Min(away, Vector2Int.Distance(p.Coords, e));

            if (away >= enemyMinDistance && spacing >= squadMinDistance)
            {
                anchor = candidate;
                best = places;
                break;
            }

            // the best that can be had: the distance from the player counts more than the spacing between squads
            float score = Mathf.Min(away, enemyMinDistance) * 2f + Mathf.Min(spacing, squadMinDistance);
            if (score > bestScore)
            {
                bestScore = score;
                anchor = candidate;
                best = places;
            }
        }

        if (best != null && keepAway.Count > 0)
        {
            float nearest = float.PositiveInfinity;
            foreach (Tile p in best)
                foreach (Vector2Int e in keepAway)
                    nearest = Mathf.Min(nearest, Vector2Int.Distance(p.Coords, e));
            if (nearest < enemyMinDistance)
                Debug.LogWarning("UnitSpawner: the field has no room to keep a squad " + enemyMinDistance + " tiles from the player's units; the nearest is " + nearest.ToString("0.0") + " tiles away.", this);
        }

        if (best == null)
        {
            // no room for a whole squad: whatever free tiles there are
            best = new List<Tile>();
            foreach (Tile t in shuffled)
            {
                if (!used.Contains(t.Coords))
                    best.Add(t);
                if (best.Count >= size)
                    break;
            }
            if (best.Count > 0)
                anchor = best[0];
        }

        if (anchor != null)
            anchors.Add(anchor.Coords);
        return best;
    }

    // The tile itself and the nearest free tiles around it (within the given spread, a little more if need be).
    private List<Tile> GatherAround(Tile centre, int size, Dictionary<Vector2Int, Tile> byCoords, HashSet<Vector2Int> used, int baseSpread)
    {
        for (int spread = baseSpread; spread <= baseSpread + 3; spread++)
        {
            List<Tile> near = new List<Tile>();
            for (int dx = -spread; dx <= spread; dx++)
            {
                for (int dy = -spread; dy <= spread; dy++)
                {
                    if (dx == 0 && dy == 0)
                        continue;
                    if (Mathf.Abs(dx) + Mathf.Abs(dy) > spread + 1)
                        continue;

                    Tile t;
                    if (byCoords.TryGetValue(centre.Coords + new Vector2Int(dx, dy), out t) && !used.Contains(t.Coords))
                        near.Add(t);
                }
            }

            if (near.Count + 1 < size)
                continue;

            // the nearest first, with the ones at the same distance in random order
            // (each tile gets its random number once: a comparison that draws new random numbers every time can make Sort fail)
            Dictionary<Tile, float> key = new Dictionary<Tile, float>();
            foreach (Tile t in near)
                key[t] = Vector2Int.Distance(t.Coords, centre.Coords) + Random.value * 0.4f;
            near.Sort((a, b) => key[a].CompareTo(key[b]));

            List<Tile> result = new List<Tile> { centre };
            for (int i = 0; i < near.Count && result.Count < size; i++)
                result.Add(near[i]);
            return result;
        }
        return new List<Tile> { centre };
    }

    [ContextMenu("Clear")]
    public void Clear()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            GameObject child = transform.GetChild(i).gameObject;
            if (Application.isPlaying)
            {
                child.SetActive(false); // frees its tile right away; Destroy only happens at the end of the frame
                Destroy(child);
            }
            else
            {
                DestroyImmediate(child);
            }
        }
    }

    private Unit SpawnUnit(Unit prefab)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
            return (Unit)PrefabUtility.InstantiatePrefab(prefab, transform);
#endif
        return Instantiate(prefab, transform);
    }
}
