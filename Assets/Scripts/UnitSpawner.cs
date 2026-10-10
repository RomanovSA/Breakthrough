using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Spawns a Unit on a random tile of the field. It spawns every time the field is (re)built,
/// so a new field always gets a unit. Right-click the component header and choose "Spawn"
/// to respawn it by hand in the editor.
/// The spawned unit is parented to this object; any previous unit is removed first.
/// </summary>
public class UnitSpawner : MonoBehaviour
{
    [SerializeField] private GridManager grid;
    [SerializeField] private Unit unitPrefab;

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
    public Unit Spawn()
    {
        if (grid == null)
            grid = FindFirstObjectByType<GridManager>();

        if (grid == null || unitPrefab == null)
        {
            Debug.LogWarning("UnitSpawner: GridManager or Unit Prefab is not assigned.", this);
            return null;
        }

        Tile[] tiles = grid.GetComponentsInChildren<Tile>();
        if (tiles.Length == 0)
        {
            Debug.LogWarning("UnitSpawner: the field has no tiles. Build the field first.", this);
            return null;
        }

        Clear();

        Tile tile = tiles[Random.Range(0, tiles.Length)];
        Unit unit = SpawnUnit();
        unit.name = "Unit";
        unit.PlaceOn(tile);
        spawned = true;
        return unit;
    }

    [ContextMenu("Clear")]
    public void Clear()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            GameObject child = transform.GetChild(i).gameObject;
            if (Application.isPlaying)
                Destroy(child);
            else
                DestroyImmediate(child);
        }
    }

    private Unit SpawnUnit()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
            return (Unit)PrefabUtility.InstantiatePrefab(unitPrefab, transform);
#endif
        return Instantiate(unitPrefab, transform);
    }
}
