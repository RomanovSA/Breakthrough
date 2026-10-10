using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A* path search over the tile field. Walls stand between tiles and cannot be crossed.
/// Units may move in 8 directions; the rules for what counts as a legal step live in GridManager.CanStep.
/// </summary>
public static class Pathfinder
{
    private const float DiagonalCost = 1.41421356f;

    private static readonly Vector2Int[] Directions =
    {
        new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1),
        new Vector2Int(1, 1), new Vector2Int(1, -1), new Vector2Int(-1, 1), new Vector2Int(-1, -1)
    };

    /// <summary>
    /// Returns the tiles from start to goal (both included), or null if the goal cannot be reached.
    /// </summary>
    public static List<Tile> FindPath(GridManager grid, Tile start, Tile goal)
    {
        if (grid == null || start == null || goal == null)
            return null;

        Vector2Int size = grid.Size;
        Vector2Int s = start.Coords;
        Vector2Int e = goal.Coords;
        if (!InBounds(s, size) || !InBounds(e, size))
            return null;

        float[,] g = new float[size.x, size.y];
        float[,] f = new float[size.x, size.y];
        bool[,] closed = new bool[size.x, size.y];
        Vector2Int[,] parent = new Vector2Int[size.x, size.y];

        for (int x = 0; x < size.x; x++)
        {
            for (int y = 0; y < size.y; y++)
            {
                g[x, y] = float.PositiveInfinity;
                f[x, y] = float.PositiveInfinity;
            }
        }

        List<Vector2Int> open = new List<Vector2Int>();
        g[s.x, s.y] = 0f;
        f[s.x, s.y] = Heuristic(s, e);
        open.Add(s);

        while (open.Count > 0)
        {
            int bestIndex = 0;
            for (int i = 1; i < open.Count; i++)
            {
                if (f[open[i].x, open[i].y] < f[open[bestIndex].x, open[bestIndex].y])
                    bestIndex = i;
            }

            Vector2Int current = open[bestIndex];
            open.RemoveAt(bestIndex);

            if (current == e)
                return Reconstruct(grid, parent, s, e);

            closed[current.x, current.y] = true;

            foreach (Vector2Int d in Directions)
            {
                Vector2Int n = current + d;
                if (!grid.CanStep(current, n) || closed[n.x, n.y])
                    continue;

                bool diagonal = d.x != 0 && d.y != 0;
                float tentative = g[current.x, current.y] + (diagonal ? DiagonalCost : 1f);
                if (tentative >= g[n.x, n.y])
                    continue;

                parent[n.x, n.y] = current;
                g[n.x, n.y] = tentative;
                f[n.x, n.y] = tentative + Heuristic(n, e);

                if (!open.Contains(n))
                    open.Add(n);
            }
        }

        return null;
    }

    private static List<Tile> Reconstruct(GridManager grid, Vector2Int[,] parent, Vector2Int start, Vector2Int end)
    {
        List<Tile> path = new List<Tile>();
        Vector2Int c = end;
        while (c != start)
        {
            path.Add(grid.GetTile(c));
            c = parent[c.x, c.y];
        }

        path.Add(grid.GetTile(start));
        path.Reverse();
        return path;
    }

    private static bool InBounds(Vector2Int c, Vector2Int size)
    {
        return c.x >= 0 && c.y >= 0 && c.x < size.x && c.y < size.y;
    }

    // Octile distance: exact cost on an empty 8-connected grid.
    private static float Heuristic(Vector2Int a, Vector2Int b)
    {
        int dx = Mathf.Abs(a.x - b.x);
        int dy = Mathf.Abs(a.y - b.y);
        return (dx + dy) + (DiagonalCost - 2f) * Mathf.Min(dx, dy);
    }
}
