using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>What made a noise.</summary>
public enum NoiseKind
{
    /// <summary>A unit walking (every step it takes): heard within FootstepRadius tiles.</summary>
    Footsteps,

    /// <summary>A shot: heard within ShotRadius tiles.</summary>
    Shot
}

/// <summary>One noise: what it was, who made it, where, and on which tile.</summary>
public struct NoiseEvent
{
    public NoiseKind kind;
    public Unit source;
    public Vector3 position;
    public Vector2Int tile;
    public float radiusInTiles;
}

/// <summary>
/// Noise. Units make noise when they walk (a hop from tile to tile) and when they shoot, and the units of the other side that are near
/// enough hear it: footsteps within 10 tiles, shots within 20. Walls and buildings do not stop sound.
/// Emit tells everybody who is listening (the event Heard) who heard it: NoiseMarkers puts a mark on the map for the player, EnemyAI
/// gives the enemy squads a lead to go and look at. Units of the same side do not 'hear' each other.
/// </summary>
public static class NoiseSystem
{
    /// <summary>How far (in tiles) footsteps are heard.</summary>
    public const float FootstepRadius = 10f;

    /// <summary>How far (in tiles) a shot is heard.</summary>
    public const float ShotRadius = 20f;

    /// <summary>Raised for every noise that somebody of the other side heard: the noise and those who heard it.</summary>
    public static event Action<NoiseEvent, IReadOnlyList<Unit>> Heard;

    /// <summary>How many noises there have been since the game started, by kind (for checking).</summary>
    public static readonly int[] Emitted = new int[2];

    private static readonly List<Unit> listeners = new List<Unit>();
    private static GridManager grid;

    public static float RadiusOf(NoiseKind kind)
    {
        return kind == NoiseKind.Shot ? ShotRadius : FootstepRadius;
    }

    /// <summary>A noise is made by 'source' at 'position'.</summary>
    public static void Emit(NoiseKind kind, Unit source, Vector3 position)
    {
        if (source == null || !source.IsAlive)
            return;

        Emitted[(int)kind]++;

        if (grid == null)
            grid = UnityEngine.Object.FindFirstObjectByType<GridManager>();
        float tileSize = grid != null ? grid.TileSize : 1f;
        float radius = RadiusOf(kind);

        listeners.Clear();
        foreach (Unit u in Unit.All)
        {
            if (u == null || u == source || !u.IsAlive || u.Faction == source.Faction)
                continue;

            Vector3 d = u.transform.position - position;
            d.y = 0f;
            if (d.magnitude / tileSize <= radius)
                listeners.Add(u);
        }

        if (listeners.Count == 0)
            return;

        NoiseEvent e = new NoiseEvent { kind = kind, source = source, position = position, tile = source.Coords, radiusInTiles = radius };
        Heard?.Invoke(e, listeners);
    }

    /// <summary>
    /// A tile near 'tile' (within 'uncertainty' tiles, one that is not blocked): where a listener thinks the sound came from, because
    /// sound does not tell the exact place. With uncertainty 0 it is the tile itself.
    /// </summary>
    public static Vector2Int FuzzyTile(GridManager field, Vector2Int tile, int uncertainty)
    {
        if (uncertainty <= 0 || field == null)
            return tile;

        List<Vector2Int> options = new List<Vector2Int>();
        for (int dx = -uncertainty; dx <= uncertainty; dx++)
        {
            for (int dy = -uncertainty; dy <= uncertainty; dy++)
            {
                Vector2Int c = tile + new Vector2Int(dx, dy);
                Tile t = field.GetTile(c);
                if (t != null && !t.IsBlocked)
                    options.Add(c);
            }
        }
        return options.Count > 0 ? options[UnityEngine.Random.Range(0, options.Count)] : tile;
    }
}
