using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Enemies are not on the field until one of the player's units sees them.
/// Every frame it works out which tiles are in the field of view of any living unit of the viewing faction (the player): the same
/// field that is drawn as the vision fan - position, facing, view angle, range, buildings and walls in the way. An enemy standing on
/// such a tile is shown; an enemy anywhere else is not drawn at all (no body, no weapon, no shadow, no health bar, no vision fan).
/// It is still there for everything else: it takes its tile, bullets hit it, and it can be shot at again the moment it is seen.
/// While the shot view is on, the enemies the shot can be switched between stay shown (the shooter turns to the target, and that
/// must not make the others blink out).
/// Put it on any object in the scene (one is enough).
/// </summary>
[DefaultExecutionOrder(200)]
public class EnemyVisibility : MonoBehaviour
{
    [SerializeField] private GridManager grid;
    [Tooltip("Whose eyes count: units of this faction see.")]
    [SerializeField] private Faction viewerFaction = Faction.Player;
    [Tooltip("Whose units are hidden until they are seen.")]
    [SerializeField] private Faction hiddenFaction = Faction.Enemy;

    private readonly HashSet<Vector2Int> seenTiles = new HashSet<Vector2Int>();
    private readonly HashSet<Vector2Int> oneUnitTiles = new HashSet<Vector2Int>();
    private ShotCamera shotCamera;

    private void LateUpdate()
    {
        Refresh();
    }

    /// <summary>Shows the enemies that are in view and hides the others (done every frame by itself).</summary>
    public void Refresh()
    {
        if (grid == null)
            grid = FindFirstObjectByType<GridManager>();
        if (grid == null)
            return;
        if (shotCamera == null)
            shotCamera = FindFirstObjectByType<ShotCamera>();

        // everything any living unit of the viewing faction can see
        seenTiles.Clear();
        IReadOnlyList<Unit> units = Unit.All;
        for (int i = 0; i < units.Count; i++)
        {
            Unit viewer = units[i];
            if (viewer == null || viewer.Faction != viewerFaction || !viewer.IsAlive)
                continue;

            grid.ComputeVisibleTiles(viewer.transform.position, viewer.FacingDirection, viewer.ViewAngle, viewer.ViewRange, oneUnitTiles);
            seenTiles.UnionWith(oneUnitTiles);
        }

        bool shooting = shotCamera != null && shotCamera.IsOn;
        for (int i = 0; i < units.Count; i++)
        {
            Unit unit = units[i];
            if (unit == null || unit.Faction != hiddenFaction)
                continue;

            bool visible = seenTiles.Contains(unit.Coords) || (shooting && (shotCamera.Target == unit || shotCamera.IsTarget(unit)));
            unit.SetConcealed(!visible);
        }
    }

    private void OnDisable()
    {
        // without this the enemies would stay hidden
        IReadOnlyList<Unit> units = Unit.All;
        for (int i = 0; i < units.Count; i++)
        {
            if (units[i] != null && units[i].Faction == hiddenFaction)
                units[i].SetConcealed(false);
        }
    }
}
