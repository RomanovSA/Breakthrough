using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A GridSettingsProvider that returns a field of the configured size
/// with a random number of walls on random edges between tiles.
/// A wall is only kept if every tile can still be reached from every other tile.
/// </summary>
public class RandomGridSettingsProvider : GridSettingsProvider
{
    private static readonly Vector2Int[] Neighbours =
    {
        new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1)
    };

    [Header("Field")]
    [SerializeField, Min(1)] private int width = 10;
    [SerializeField, Min(1)] private int height = 10;
    [SerializeField, Min(0.01f)] private float tileSize = 1f;

    [Header("Walls")]
    [SerializeField, Min(0)] private int minWalls = 5;
    [SerializeField, Min(0)] private int maxWalls = 6;

    public override GridSettings GetSettings()
    {
        GridSettings settings = new GridSettings(width, height, tileSize);

        int lo = Mathf.Min(minWalls, maxWalls);
        int hi = Mathf.Max(minWalls, maxWalls);
        settings.walls = GenerateWalls(Random.Range(lo, hi + 1));

        return settings;
    }

    private List<WallEdge> GenerateWalls(int count)
    {
        // Every edge between two side-adjacent tiles is a candidate.
        List<WallEdge> candidates = new List<WallEdge>();
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Vector2Int c = new Vector2Int(x, y);
                if (x + 1 < width)
                    candidates.Add(new WallEdge(c, new Vector2Int(x + 1, y)));
                if (y + 1 < height)
                    candidates.Add(new WallEdge(c, new Vector2Int(x, y + 1)));
            }
        }

        // Shuffle (Fisher-Yates) so walls land on random edges.
        for (int i = candidates.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            WallEdge tmp = candidates[i];
            candidates[i] = candidates[j];
            candidates[j] = tmp;
        }

        List<WallEdge> result = new List<WallEdge>();
        HashSet<(Vector2Int, Vector2Int)> blocked = new HashSet<(Vector2Int, Vector2Int)>();

        foreach (WallEdge edge in candidates)
        {
            if (result.Count >= count)
                break;

            var key = Key(edge.a, edge.b);
            blocked.Add(key);

            if (!AllTilesConnected(blocked))
            {
                blocked.Remove(key); // this wall would cut the field in two
                continue;
            }

            result.Add(edge);
        }

        return result;
    }

    // Same edge, same key, whichever tile is named first.
    private static (Vector2Int, Vector2Int) Key(Vector2Int a, Vector2Int b)
    {
        bool aFirst = a.x < b.x || (a.x == b.x && a.y < b.y);
        return aFirst ? (a, b) : (b, a);
    }

    private bool AllTilesConnected(HashSet<(Vector2Int, Vector2Int)> blocked)
    {
        bool[,] visited = new bool[width, height];
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        queue.Enqueue(Vector2Int.zero);
        visited[0, 0] = true;

        int reached = 0;
        while (queue.Count > 0)
        {
            Vector2Int c = queue.Dequeue();
            reached++;

            foreach (Vector2Int d in Neighbours)
            {
                Vector2Int n = c + d;
                if (n.x < 0 || n.y < 0 || n.x >= width || n.y >= height || visited[n.x, n.y])
                    continue;
                if (blocked.Contains(Key(c, n)))
                    continue;

                visited[n.x, n.y] = true;
                queue.Enqueue(n);
            }
        }

        return reached == width * height;
    }
}
