using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Spawns units on random, different tiles of the field every time the field is (re)built.
/// What is spawned is described by a list of groups: a unit prefab and how many of it.
/// The prefab decides the faction (for example one group of player units and one of enemies).
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

    private bool spawned;

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

        Tile[] tiles = grid.GetComponentsInChildren<Tile>();
        if (tiles.Length == 0)
        {
            Debug.LogWarning("UnitSpawner: the field has no tiles. Build the field first.", this);
            return result;
        }

        Clear();

        // Shuffle the tiles (Fisher-Yates) and hand them out one by one, so no two units share a tile.
        for (int i = tiles.Length - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            Tile tmp = tiles[i];
            tiles[i] = tiles[j];
            tiles[j] = tmp;
        }

        int nextTile = 0;
        foreach (SpawnGroup group in groups)
        {
            if (group == null || group.prefab == null)
            {
                Debug.LogWarning("UnitSpawner: a spawn group has no prefab.", this);
                continue;
            }

            for (int i = 0; i < group.count; i++)
            {
                if (nextTile >= tiles.Length)
                {
                    Debug.LogWarning("UnitSpawner: there are more units than tiles, the rest was not spawned.", this);
                    spawned = true;
                    return result;
                }

                Unit unit = SpawnUnit(group.prefab);
                unit.name = unit.Faction + "Unit_" + (i + 1);
                if (!unit.PlaceOn(tiles[nextTile++]))
                {
                    // cannot happen with the shuffled list, but never leave two units on one tile
                    Debug.LogWarning("UnitSpawner: the tile was already taken, the unit was not spawned.", this);
                    DestroyImmediate(unit.gameObject);
                    continue;
                }

                result.Add(unit);
            }
        }

        spawned = true;
        return result;
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
