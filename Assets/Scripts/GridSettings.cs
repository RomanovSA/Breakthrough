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
