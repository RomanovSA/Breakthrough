using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Route planning over the tile field.
///
/// For COST purposes a unit cannot move diagonally: it walks from tile to side-adjacent tile
/// and every tile entered costs 1 action point, so the cost of a route is its number of
/// orthogonal steps (a 'Manhattan' distance that respects walls).
///
/// For ANIMATION the unit may still cut a corner diagonally (see SmoothPath), but that
/// shortcut is charged as the two orthogonal steps it replaces, so the cost never changes.
/// </summary>
public static class Pathfinder
{
    private static readonly Vector2Int[] Directions =
    {
        Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down
    };

    /// <summary>
    /// Returns a shortest route from start to goal (both included) in orthogonal steps, or null if the
    /// goal cannot be reached. The cost in action points is path.Count - 1.
    /// If a mover is given, tiles taken by other units are treated as blocked (the mover's own tile is not).
    /// Among equally short routes the one that hugs the straight line from start to goal is chosen,
    /// which gives a staircase for diagonal trips (and lets SmoothPath turn it into a diagonal run).
    /// </summary>
    public static List<Tile> FindPath(GridManager grid, Tile start, Tile goal, Unit mover = null)
    {
        if (grid == null || start == null || goal == null)
            return null;

        Vector2Int size = grid.Size;
        Vector2Int s = start.Coords;
        Vector2Int e = goal.Coords;
        if (!InBounds(s, size) || !InBounds(e, size))
            return null;

        // Distance (in steps) from every tile to the goal.
        // Tiles taken by OTHER units cannot be walked through or stopped on.
        HashSet<Vector2Int> blocked = mover != null ? Unit.GetOccupiedCoords(mover) : null;
        if (blocked != null && blocked.Contains(e))
            return null;

        int[,] dist = BuildDistanceField(grid, e, size, blocked);
        if (dist[s.x, s.y] < 0)
            return null;

        List<Tile> path = new List<Tile> { start };
        Vector2 line = new Vector2(e.x - s.x, e.y - s.y);
        Vector2Int current = s;
        Vector2Int previousDirection = Vector2Int.zero;

        while (current != e)
        {
            int here = dist[current.x, current.y];
            bool found = false;
            Vector2Int bestNext = current;
            Vector2Int bestDirection = Vector2Int.zero;
            float bestDeviation = float.PositiveInfinity;

            foreach (Vector2Int d in Directions)
            {
                Vector2Int n = current + d;
                if (!grid.CanStep(current, n) || dist[n.x, n.y] != here - 1)
                    continue; // not a step along a shortest route

                float deviation = Mathf.Abs((n.x - s.x) * line.y - (n.y - s.y) * line.x);

                bool better;
                if (!found)
                    better = true;
                else if (deviation < bestDeviation - 0.0001f)
                    better = true;
                else if (deviation > bestDeviation + 0.0001f)
                    better = false;
                else
                    better = d != previousDirection && bestDirection == previousDirection; // tie: prefer turning

                if (better)
                {
                    found = true;
                    bestNext = n;
                    bestDirection = d;
                    bestDeviation = deviation;
                }
            }

            if (!found)
                return null; // cannot happen on a consistent grid

            path.Add(grid.GetTile(bestNext));
            previousDirection = bestDirection;
            current = bestNext;
        }

        return path;
    }

    /// <summary>
    /// Turns an orthogonal route into the route the unit actually runs along: wherever the route turns a
    /// corner and a diagonal step is possible (no wall touches that corner), the corner is cut.
    /// The result is a subset of the given tiles (first and last are kept). The cost of a hop between two
    /// consecutive tiles of the result is |dx| + |dy|, so the total cost equals the original number of steps.
    /// </summary>
    public static List<Tile> SmoothPath(GridManager grid, List<Tile> path)
    {
        if (path == null || path.Count == 0)
            return new List<Tile>();

        List<Tile> result = new List<Tile> { path[0] };
        int i = 0;

        while (i < path.Count - 1)
        {
            if (i + 2 < path.Count)
            {
                Vector2Int first = path[i + 1].Coords - path[i].Coords;
                Vector2Int second = path[i + 2].Coords - path[i + 1].Coords;

                if (first != second && grid.CanStep(path[i].Coords, path[i + 2].Coords))
                {
                    result.Add(path[i + 2]);
                    i += 2;
                    continue;
                }
            }

            result.Add(path[i + 1]);
            i++;
        }

        return result;
    }

    /// <summary>
    /// Returns every tile that can be reached from start in at most maxSteps orthogonal steps
    /// (the start tile itself is not included). These are exactly the tiles a unit with maxSteps
    /// action points can walk to.
    /// </summary>
    public static List<Tile> GetReachableTiles(GridManager grid, Tile start, int maxSteps, Unit mover = null)
    {
        List<Tile> result = new List<Tile>();
        if (grid == null || start == null || maxSteps <= 0)
            return result;

        Vector2Int size = grid.Size;
        Vector2Int s = start.Coords;
        if (!InBounds(s, size))
            return result;

        HashSet<Vector2Int> blocked = mover != null ? Unit.GetOccupiedCoords(mover) : null;
        int[,] dist = BuildDistanceField(grid, s, size, blocked);
        for (int x = 0; x < size.x; x++)
        {
            for (int y = 0; y < size.y; y++)
            {
                if (dist[x, y] >= 1 && dist[x, y] <= maxSteps)
                    result.Add(grid.GetTile(new Vector2Int(x, y)));
            }
        }

        return result;
    }

    // Breadth-first search over side-adjacent steps. -1 means 'not reachable'.
    private static int[,] BuildDistanceField(GridManager grid, Vector2Int source, Vector2Int size, HashSet<Vector2Int> blocked)
    {
        int[,] dist = new int[size.x, size.y];
        for (int x = 0; x < size.x; x++)
            for (int y = 0; y < size.y; y++)
                dist[x, y] = -1;

        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        dist[source.x, source.y] = 0;
        queue.Enqueue(source);

        while (queue.Count > 0)
        {
            Vector2Int c = queue.Dequeue();
            foreach (Vector2Int d in Directions)
            {
                Vector2Int n = c + d;
                if (!grid.CanStep(c, n) || dist[n.x, n.y] >= 0 || (blocked != null && blocked.Contains(n)))
                    continue;

                dist[n.x, n.y] = dist[c.x, c.y] + 1;
                queue.Enqueue(n);
            }
        }

        return dist;
    }

    private static bool InBounds(Vector2Int c, Vector2Int size)
    {
        return c.x >= 0 && c.y >= 0 && c.x < size.x && c.y < size.y;
    }
}
