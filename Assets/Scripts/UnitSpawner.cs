using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Spawns units on random, different tiles of the field every time the field is (re)built.
/// What is spawned is described by a list of groups: a unit prefab and how many of it.
/// The prefab decides the faction (for example one group of player units and one of enemies).
///
/// Units of the Squad Faction (the enemies) do not spawn one by one but in squads of Squad Min to Squad Max units (the count of the
/// group is split into squads of those sizes, for example 10 = 4 + 3 + 3): the members stand close together (within Squad Spread
/// tiles of the squad's first tile) and the squads stand apart (at least Squad Min Distance tiles). Each squad is an EnemyGroup, which
/// the members share their intelligence through. The other factions spawn as before, each unit on its own random tile.
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
    }

    [SerializeField] private GridManager grid;
    [SerializeField] private List<SpawnGroup> groups = new List<SpawnGroup>();

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

        HashSet<Vector2Int> used = new HashSet<Vector2Int>();
        List<Vector2Int> squadAnchors = new List<Vector2Int>();
        int nextTile = 0;

        foreach (SpawnGroup group in groups)
        {
            if (group == null || group.prefab == null)
            {
                Debug.LogWarning("UnitSpawner: a spawn group has no prefab.", this);
                continue;
            }

            int serial = 0;
            if (group.prefab.Faction == squadFaction)
            {
                // squads
                foreach (int size in SquadSizes(group.count, squadMin, Mathf.Max(squadMin, squadMax)))
                {
                    List<Tile> places = ChooseSquadPlaces(size, tiles, byCoords, used, squadAnchors);
                    List<Unit> members = new List<Unit>();
                    foreach (Tile place in places)
                    {
                        Unit unit = SpawnUnit(group.prefab);
                        unit.name = unit.Faction + "Unit_" + (++serial);
                        if (!unit.PlaceOn(place))
                        {
                            Debug.LogWarning("UnitSpawner: the tile was already taken, the unit was not spawned.", this);
                            DestroyImmediate(unit.gameObject);
                            continue;
                        }

                        used.Add(place.Coords);
                        result.Add(unit);
                        members.Add(unit);
                    }

                    if (members.Count > 0)
                        new EnemyGroup(members);
                    if (places.Count < size)
                        Debug.LogWarning("UnitSpawner: there was room for only " + places.Count + " of " + size + " units of a squad.", this);
                }
                continue;
            }

            for (int i = 0; i < group.count; i++)
            {
                while (nextTile < tiles.Length && used.Contains(tiles[nextTile].Coords))
                    nextTile++;
                if (nextTile >= tiles.Length)
                {
                    Debug.LogWarning("UnitSpawner: there are more units than tiles, the rest was not spawned.", this);
                    spawned = true;
                    UnitsSpawned?.Invoke();
                    return result;
                }

                Unit unit = SpawnUnit(group.prefab);
                unit.name = unit.Faction + "Unit_" + (++serial);
                Tile tile = tiles[nextTile++];
                if (!unit.PlaceOn(tile))
                {
                    // cannot happen with the shuffled list, but never leave two units on one tile
                    Debug.LogWarning("UnitSpawner: the tile was already taken, the unit was not spawned.", this);
                    DestroyImmediate(unit.gameObject);
                    continue;
                }

                used.Add(tile.Coords);
                result.Add(unit);
            }
        }

        spawned = true;
        UnitsSpawned?.Invoke();
        return result;
    }

    /// <summary>Splits 'count' units into squads of min to max units (for example 10 into 4 + 3 + 3); never leaves a squad of one.</summary>
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

    // The tiles for one squad: a first tile far from the other squads, and the nearest free tiles around it.
    private List<Tile> ChooseSquadPlaces(int size, Tile[] shuffled, Dictionary<Vector2Int, Tile> byCoords, HashSet<Vector2Int> used, List<Vector2Int> anchors)
    {
        Tile anchor = null;
        List<Tile> best = null;
        float bestSpacing = -1f;

        int tries = 0;
        foreach (Tile candidate in shuffled)
        {
            if (used.Contains(candidate.Coords))
                continue;
            if (++tries > 400)
                break;

            // how far is it from the nearest other squad?
            float spacing = float.PositiveInfinity;
            foreach (Vector2Int a in anchors)
                spacing = Mathf.Min(spacing, Vector2Int.Distance(a, candidate.Coords));

            List<Tile> places = GatherAround(candidate, size, byCoords, used);
            if (places.Count < size)
                continue;                                   // too cramped here

            if (spacing >= squadMinDistance)
            {
                anchor = candidate;
                best = places;
                break;
            }

            if (spacing > bestSpacing)
            {
                bestSpacing = spacing;
                anchor = candidate;
                best = places;
            }
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

    // The tile itself and the nearest free tiles around it (within the squad's spread, a little more if need be).
    private List<Tile> GatherAround(Tile centre, int size, Dictionary<Vector2Int, Tile> byCoords, HashSet<Vector2Int> used)
    {
        for (int spread = squadSpread; spread <= squadSpread + 3; spread++)
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
