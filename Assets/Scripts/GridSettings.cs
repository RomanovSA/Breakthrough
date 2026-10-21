using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A wall standing on the edge between two side-adjacent tiles (given by their coordinates).
/// </summary>
[Serializable]
public struct WallEdge
{
    public Vector2Int a;
    public Vector2Int b;

    public WallEdge(Vector2Int a, Vector2Int b)
    {
        this.a = a;
        this.b = b;
    }
}

/// <summary>
/// Something that stands on the field: a prefab, where it stands (in tiles, tile (0, 0) is at 0, 0 and the
/// numbers may be fractions), which way it faces (degrees around the vertical axis) and how big it is.
/// </summary>
[Serializable]
public struct GridObject
{
    public GameObject prefab;
    public Vector2 position;
    public float yaw;
    public float scale;

    [Tooltip("How far above the top of the tile the object stands, in world units.")]
    public float height;

    [Tooltip("A solid object (a building, a car): the tiles under it can not be walked on and it is clickable. Everything else is ignored by mouse clicks.")]
    public bool solid;

    [Tooltip("The tiles under it also block the view (for tall things like buildings; a car can be seen over).")]
    public bool blocksSight;

    [Tooltip("Extra stretch along the model's own x and z axes (0 or 1 = none). Used to make a footprint cover whole tiles.")]
    public float stretchX;
    public float stretchZ;

    [Tooltip("A small solid thing (a palm, a bin, a pole): it blocks only the tile it stands on, however wide it is, and mouse clicks go through it to that tile.")]
    public bool singleTile;

    [Tooltip("A unit standing next to it is in cover. Full or half depends on how tall it is (see the cover heights on the GridManager).")]
    public bool providesCover;

    [Tooltip("Health of this thing (a bullet takes some away). 0 = the default of 100.")]
    public int health;

    [Tooltip("Off: it takes hits but never breaks (a house with a roof ladder).")]
    public bool breakable;

    public GridObject(GameObject prefab, Vector2 position, float yaw, float scale, float height = 0f, bool solid = false, bool blocksSight = false, float stretchX = 1f, float stretchZ = 1f, bool singleTile = false, bool providesCover = false, int health = 0, bool breakable = true)
    {
        this.prefab = prefab;
        this.position = position;
        this.yaw = yaw;
        this.scale = scale;
        this.height = height;
        this.solid = solid;
        this.blocksSight = blocksSight;
        this.stretchX = stretchX;
        this.stretchZ = stretchZ;
        this.singleTile = singleTile;
        this.providesCover = providesCover;
        this.health = health;
        this.breakable = breakable;
    }
}

/// <summary>
/// Everything GridManager needs to build a field: its size and where the objects on it stand.
/// This is plain data, so any class can create one and hand it to GridManager.Build.
/// </summary>
[Serializable]
public class GridSettings
{
    [Min(1)] public int width = 10;
    [Min(1)] public int height = 10;
    [Min(0.01f)] public float tileSize = 1f;
    public List<WallEdge> walls = new List<WallEdge>();
    public List<GridObject> objects = new List<GridObject>();

    [Tooltip("Show the x,y numbers on the tiles.")]
    public bool showCoordinates = true;

    [Tooltip("How far above the top of the tiles the tile marks (reachable, hover, shade) are drawn, in world units. Raise it when the ground is covered by models.")]
    public float overlayHeight = 0.02f;

    public GridSettings()
    {
    }

    public GridSettings(int width, int height, float tileSize = 1f)
    {
        this.width = width;
        this.height = height;
        this.tileSize = tileSize;
    }
}
