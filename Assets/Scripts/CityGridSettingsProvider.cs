using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A GridSettingsProvider that lays out a small, random city: roads, blocks with buildings, parks, a plaza and a
/// landmark tower, street furniture and cars, all built from the city asset packs.
///
/// Everything stands exactly on the tiles:
///  - the field is cut into 7 x 7 'modules' of a whole number of tiles each (7 tiles on a 50 x 50 field), and the
///    models are scaled until one road piece (15 m) is exactly one module, so roads and sidewalks start and end on tile
///    edges; the strip of tiles that is left over at the edge is covered with stretched sidewalk;
///  - a building, a car, the fountain... is stretched a few percent so that its footprint is a whole number of tiles
///    and is put on a tile centre (odd number of tiles) or on a tile edge (even number), so no model sits between tiles;
///  - small things (palms, bushes, bins, traffic poles) are put on the middle of one tile and block that tile;
///  - everything on the map is solid: buildings, cars, the fountain, the bus stop and all the small things;
///  - buildings, cars, the fountain, the bus stop and the bins give cover (how good depends on how tall they are); palms, bushes and poles do not;
///  - the houses have a roof ladder (made in Blender): the tile in front of it is kept free, and the roof can be walked on;
///  - the cover props made in Blender (crates, barrels, containers, barriers, sandbags, rubble, ruined walls, tank traps, wrecks)
///    keep their true size, are put on whole tiles and give cover too; street lamps stand along the sidewalks;
///  - no two things ever share a tile.
///
/// Every new game builds a different city: the roads (the outer ring plus a spine and two side streets in random
/// places, always T junctions), what stands in every block and where the cars are. Untick 'New City Every Time' and
/// set a Seed to get the same city again; the seed of the city that was just built is written to the console.
/// There are no walls and no units in the settings: the layout is only the scenery.
/// </summary>
public class CityGridSettingsProvider : GridSettingsProvider
{
    // The blocks and roads keep their size when the field grows: a module is always about 7 tiles, and the field has as many modules
    // along a side as fit (7 on a 50 x 50 field, 10 on 70 x 70).
    private const int TilesPerModule = 7;
    private const int MinModules = 7;
    private int Modules = MinModules;
    private const float ModelModuleSize = 15f;   // the road and sidewalk pieces are 15 m wide

    /// <summary>A building prefab and which way its front looks.</summary>
    [System.Serializable]
    public class HouseModel
    {
        public GameObject prefab;
        [Tooltip("0 when the front of the model is its +Z side; 180 when it is the -Z side.")]
        public float frontYaw;
    }

    [Header("Field")]
    [SerializeField, Min(MinModules)] private int width = 70;
    [SerializeField, Min(MinModules)] private int height = 70;
    [SerializeField, Min(0.01f)] private float tileSize = 1f;
    [Tooltip("Show the x,y numbers on the tiles (they are hidden under the roads anyway).")]
    [SerializeField] private bool showCoordinates;

    [Header("City")]
    [Tooltip("On: every new game builds a different city. Off: the Seed below always gives the same city.")]
    [SerializeField] private bool newCityEveryTime = true;
    [SerializeField] private int seed = 2026;
    [SerializeField, Range(0f, 1f)] private float parkChance = 0.2f;
    [Tooltip("Chance that a stretch of straight road has a car on it (it can have two).")]
    [SerializeField, Range(0f, 1f)] private float carDensity = 0.5f;

    [Header("Ground")]
    [Tooltip("Two-lane road, runs north-south when not turned.")]
    [SerializeField] private GameObject straightRoad;
    [Tooltip("T junction: open to the north, south and east when not turned.")]
    [SerializeField] private GameObject tJunction;
    [Tooltip("Corner: open to the south and east when not turned.")]
    [SerializeField] private GameObject cornerRoad;
    [Tooltip("Plain sidewalk plate that goes under every building block.")]
    [SerializeField] private GameObject sidewalk;

    [Header("Buildings")]
    [Tooltip("Buildings for the blocks. Front Yaw says how far the model's front is turned away from its +Z side (the Apartment's front is its -Z side, so 180).")]
    [SerializeField] private HouseModel[] houses;
    [Tooltip("Big building that takes a whole 2 x 2 area of modules.")]
    [SerializeField] private GameObject landmark;

    [Header("Plaza and parks")]
    [SerializeField] private GameObject fountain;
    [SerializeField] private GameObject[] trees;
    [Tooltip("Size of the palms compared with the rest of the city (the palm model is taller than the buildings).")]
    [SerializeField, Range(0.2f, 1f)] private float treeScale = 0.45f;
    [SerializeField] private GameObject[] bushes;

    [Header("Streets")]
    [SerializeField] private GameObject[] trashCans;
    [SerializeField] private GameObject trafficPole;
    [SerializeField] private GameObject busStop;
    [SerializeField] private GameObject[] cars;

    [Header("Cover props (made in Blender)")]
    [Tooltip("Scattered in the blocks between the buildings: crates, barrels, dumpsters, rubble, ruined walls, tank traps, sandbags, barriers.")]
    [SerializeField] private GameObject[] blockProps;
    [Tooltip("Big full-cover container, put in up to two blocks.")]
    [SerializeField] private GameObject container;
    [Tooltip("Put across a straight stretch of road in a line of three (cars cannot pass, the sidewalks stay open).")]
    [SerializeField] private GameObject[] roadblocks;
    [Tooltip("Burnt-out cars that replace some of the normal ones on the road.")]
    [SerializeField] private GameObject[] wrecks;
    [Tooltip("Street lamps along the sidewalks. Its arm should point to -x.")]
    [SerializeField] private GameObject streetLamp;
    [Tooltip("How many cover props the blocks get: 0 none, 1 about two per block.")]
    [SerializeField, Range(0f, 1f)] private float coverDensity = 0.6f;

    // Directions, used as indices: 0 north (+Z / +y), 1 east (+X / +x), 2 south, 3 west.
    private static readonly Vector2Int[] Steps = { new Vector2Int(0, 1), new Vector2Int(1, 0), new Vector2Int(0, -1), new Vector2Int(-1, 0) };

    private readonly Dictionary<GameObject, Bounds> nativeBounds = new Dictionary<GameObject, Bounds>();

    /// <summary>The seed of the city that was built last.</summary>
    public int LastSeed { get; private set; }

    public override GridSettings GetSettings()
    {
        GridSettings settings = new GridSettings(width, height, tileSize);
        settings.showCoordinates = showCoordinates;

        Modules = Mathf.Max(MinModules, Mathf.Min(width, height) / TilesPerModule);
        int module = Mathf.Max(1, Mathf.Min(width, height) / Modules);          // tiles per module (a whole number)
        float scale = module * tileSize / ModelModuleSize;                       // metres of the models -> world units

        // the road and sidewalk models are raised a little (see BuildCity); the tile marks float just above them
        settings.overlayHeight = 0.1f * scale + 0.004f + 0.012f;
        settings.objects = BuildCity(module, scale);
        return settings;
    }

    private List<GridObject> BuildCity(int module, float scale)
    {
        List<GridObject> objects = new List<GridObject>();
        HashSet<Vector2Int> taken = new HashSet<Vector2Int>();

        LastSeed = newCityEveryTime ? System.Environment.TickCount : seed;
        System.Random rng = new System.Random(LastSeed);
        Debug.Log("City layout: seed " + LastSeed + (newCityEveryTime ? " (new city every time; to get this one again untick 'New City Every Time' on CityLayout and type this seed)" : " (fixed)"));

        bool[,] road = BuildRoadMap(rng);
        bool[,] used = new bool[Modules, Modules];
        List<Vector3Int> straightRoads = new List<Vector3Int>();       // x, y of every straight stretch of road, z = 1 when it runs north-south

        // where the city starts: whole tiles, so every module edge is a tile edge (tile i is centred on i, its edge is at i - 0.5)
        int cityTiles = Modules * module;
        int startX = (width - cityTiles) / 2;
        int startY = (height - cityTiles) / 2;
        float left = startX - 0.5f;
        float bottom = startY - 0.5f;

        // The road pieces have their sidewalks at height 0 and the asphalt 0.1 m (of 15) lower, so they are lifted until the
        // asphalt is level with the top of the tiles. Everything that stands on a sidewalk is lifted the same amount,
        // cars (which drive on the asphalt) only a hair, so nothing is buried in the tiles or fights with them.
        float groundLift = 0.1f * scale + 0.004f;
        float asphaltLift = 0.004f;

        Vector2 Center(int mx, int my) => new Vector2(left + (mx + 0.5f) * module, bottom + (my + 0.5f) * module);
        GameObject Pick(GameObject[] pool) => pool != null && pool.Length > 0 ? pool[rng.Next(pool.Length)] : null;
        float Range(float min, float max) => min + (float)rng.NextDouble() * (max - min);
        // most vehicles are ordinary cars, now and then a burnt-out wreck
        GameObject PickVehicle() => wrecks != null && wrecks.Length > 0 && rng.NextDouble() < 0.18 ? Pick(wrecks) : Pick(cars);
        bool IsRoad(int mx, int my) => mx >= 0 && my >= 0 && mx < Modules && my < Modules && road[mx, my];
        Vector2 Dir(int d) => new Vector2(Steps[d].x, Steps[d].y);

        // a ground piece (road, plate): sits exactly on its module, optionally stretched to fill a strip
        void AddGround(GameObject prefab, Vector2 position, float yaw, float stretchX, float stretchZ)
        {
            if (prefab != null)
                objects.Add(new GridObject(prefab, position, yaw, scale, groundLift, false, false, stretchX, stretchZ));
        }

        // a small thing on the middle of ONE tile: solid (nobody can walk onto that tile) and nothing else may be on it
        bool PlaceProp(GameObject prefab, Vector2 wanted, float yaw, bool cover = false)
        {
            if (prefab == null)
                return false;

            Vector2Int tile = new Vector2Int(Mathf.RoundToInt(wanted.x), Mathf.RoundToInt(wanted.y));
            if (tile.x < 0 || tile.y < 0 || tile.x >= width || tile.y >= height || taken.Contains(tile))
                return false;

            taken.Add(tile);
            objects.Add(new GridObject(prefab, new Vector2(tile.x, tile.y), yaw, scale * SizeFactor(prefab), groundLift, true, false, 1f, 1f, true, cover, HealthFor(prefab), !IsSturdy(prefab)));
            return true;
        }

        // a bigger thing: its footprint is rounded to a whole number of tiles (the model is stretched a few percent),
        // and it is centred on a tile (odd count) or on a tile edge (even count) so it covers exactly those tiles.
        // needsLadder: the model has a roof ladder (a LadderFoot marker); the ground tile in front of the ladder must be free
        // too (and is kept free), otherwise the house is not placed this way.
        bool PlaceFootprint(GameObject prefab, Vector2 wanted, float yaw, float lift, bool solid, bool blocksSight, bool cover = true, bool needsLadder = false)
        {
            if (prefab == null)
                return false;

            Bounds nb = NativeBounds(prefab);
            float toTiles = scale / tileSize;                                    // model metres -> tiles
            bool turned = Mathf.Abs(Mathf.RoundToInt(yaw / 90f)) % 2 == 1;       // turned 90 / 270: the model's x runs along world z
            float w = (turned ? nb.size.z : nb.size.x) * toTiles;                // footprint along world x, in tiles
            float d = (turned ? nb.size.x : nb.size.z) * toTiles;                // footprint along world z
            int wi = Mathf.Max(1, Mathf.RoundToInt(w));
            int di = Mathf.Max(1, Mathf.RoundToInt(d));

            float cx = wi % 2 == 1 ? Mathf.Round(wanted.x) : Mathf.Floor(wanted.x) + 0.5f;
            float cy = di % 2 == 1 ? Mathf.Round(wanted.y) : Mathf.Floor(wanted.y) + 0.5f;

            int x0 = Mathf.RoundToInt(cx - (wi - 1) * 0.5f);
            int y0 = Mathf.RoundToInt(cy - (di - 1) * 0.5f);
            if (x0 < 0 || y0 < 0 || x0 + wi > width || y0 + di > height)
                return false;
            for (int x = x0; x < x0 + wi; x++)
                for (int y = y0; y < y0 + di; y++)
                    if (taken.Contains(new Vector2Int(x, y)))
                        return false;

            // stretch along the model's own axes so that the footprint becomes exactly wi x di tiles
            float stretchX = turned ? di / d : wi / w;
            float stretchZ = turned ? wi / w : di / d;

            // the model's middle is not at its pivot: move the pivot so the MIDDLE lands on the chosen centre
            Vector3 centreOffset = Quaternion.Euler(0f, yaw, 0f) * new Vector3(nb.center.x * toTiles * stretchX, 0f, nb.center.z * toTiles * stretchZ);
            Vector2 pivot = new Vector2(cx - centreOffset.x, cy - centreOffset.z);

            // the ground tile in front of the ladder: it must exist, be free and lie outside the house
            bool reserveFoot = false;
            Vector2Int footTile = Vector2Int.zero;
            if (needsLadder)
            {
                Transform foot = FindLadderFoot(prefab);
                if (foot != null)
                {
                    Vector3 local = prefab.transform.InverseTransformPoint(foot.position);
                    Vector3 offset = Quaternion.Euler(0f, yaw, 0f) * new Vector3(local.x * toTiles * stretchX, 0f, local.z * toTiles * stretchZ);
                    footTile = new Vector2Int(Mathf.RoundToInt(pivot.x + offset.x), Mathf.RoundToInt(pivot.y + offset.z));
                    bool insideField = footTile.x >= 0 && footTile.y >= 0 && footTile.x < width && footTile.y < height;
                    bool insideHouse = footTile.x >= x0 && footTile.x < x0 + wi && footTile.y >= y0 && footTile.y < y0 + di;
                    if (!insideField || insideHouse || taken.Contains(footTile))
                        return false;
                    reserveFoot = true;
                }
            }

            for (int x = x0; x < x0 + wi; x++)
                for (int y = y0; y < y0 + di; y++)
                    taken.Add(new Vector2Int(x, y));
            if (reserveFoot)
                taken.Add(footTile);

            objects.Add(new GridObject(prefab, pivot, yaw, scale, lift, solid, blocksSight, stretchX, stretchZ, false, cover, HealthFor(prefab), !IsSturdy(prefab)));
            return true;
        }

        Transform FindLadderFoot(GameObject prefab)
        {
            foreach (Transform t in prefab.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("LadderFoot"))
                    return t;
            return null;
        }

        // A model at its true size (nothing is stretched): it is put on a tile (odd number of tiles along each side) or on a
        // tile edge (even number) and claims the tiles it covers. Thin things (a barrier) claim one tile.
        bool PlaceModel(GameObject prefab, Vector2 wanted, float yaw, bool cover, float lift)
        {
            if (prefab == null)
                return false;

            Bounds nb = NativeBounds(prefab);
            float toTiles = scale / tileSize;
            bool turned = Mathf.Abs(Mathf.RoundToInt(yaw / 90f)) % 2 == 1;
            float w = (turned ? nb.size.z : nb.size.x) * toTiles;
            float d = (turned ? nb.size.x : nb.size.z) * toTiles;
            int wi = Mathf.Max(1, Mathf.RoundToInt(w));
            int di = Mathf.Max(1, Mathf.RoundToInt(d));

            float cx = wi % 2 == 1 ? Mathf.Round(wanted.x) : Mathf.Floor(wanted.x) + 0.5f;
            float cy = di % 2 == 1 ? Mathf.Round(wanted.y) : Mathf.Floor(wanted.y) + 0.5f;
            int x0 = Mathf.RoundToInt(cx - (wi - 1) * 0.5f);
            int y0 = Mathf.RoundToInt(cy - (di - 1) * 0.5f);
            if (x0 < 0 || y0 < 0 || x0 + wi > width || y0 + di > height)
                return false;
            for (int x = x0; x < x0 + wi; x++)
                for (int y = y0; y < y0 + di; y++)
                    if (taken.Contains(new Vector2Int(x, y)))
                        return false;
            for (int x = x0; x < x0 + wi; x++)
                for (int y = y0; y < y0 + di; y++)
                    taken.Add(new Vector2Int(x, y));

            Vector3 centreOffset = Quaternion.Euler(0f, yaw, 0f) * new Vector3(nb.center.x * toTiles, 0f, nb.center.z * toTiles);
            Vector2 pivot = new Vector2(cx - centreOffset.x, cy - centreOffset.z);
            bool tall = nb.size.y * scale >= 1.3f;                 // taller than a unit: nobody can see over it
            objects.Add(new GridObject(prefab, pivot, yaw, scale, lift, true, tall, 1f, 1f, false, cover, HealthFor(prefab), !IsSturdy(prefab)));
            return true;
        }

        // The turn that makes the long side of a model run along x (alongX) or along z.
        float AlignYaw(GameObject prefab, bool alongX)
        {
            Bounds nb = NativeBounds(prefab);
            bool longIsX = nb.size.x >= nb.size.z;
            return longIsX == alongX ? 0f : 90f;
        }

        // ---- roads and ground ----
        for (int my = 0; my < Modules; my++)
        {
            for (int mx = 0; mx < Modules; mx++)
            {
                Vector2 c = Center(mx, my);

                if (!road[mx, my])
                {
                    AddGround(sidewalk, c, 0f, 1f, 1f);
                    continue;
                }

                bool[] open = new bool[4];
                int count = 0;
                for (int d = 0; d < 4; d++)
                {
                    open[d] = IsRoad(mx + Steps[d].x, my + Steps[d].y);
                    if (open[d]) count++;
                }

                GameObject piece;
                float yaw;
                ChooseRoadPiece(open, count, out piece, out yaw);
                AddGround(piece, c, yaw, 1f, 1f);

                if (count >= 3 || (count == 2 && !((open[0] && open[2]) || (open[1] && open[3]))))
                {
                    // a junction or a corner: a traffic pole near each corner of the square
                    float o = Mathf.Max(1f, Mathf.Round(module * 0.5f - 0.5f));
                    PlaceProp(trafficPole, c + new Vector2(o, o), 225f);
                    PlaceProp(trafficPole, c + new Vector2(-o, o), 135f);
                    PlaceProp(trafficPole, c + new Vector2(o, -o), 315f);
                    PlaceProp(trafficPole, c + new Vector2(-o, -o), 45f);
                }
                else
                {
                    // a straight stretch: maybe cars, each in the middle of one of the two lanes (one tile left or right of the road's middle)
                    bool alongZ = yaw < 45f;                     // straight pieces are turned 0 or 90 degrees
                    straightRoads.Add(new Vector3Int(mx, my, alongZ ? 1 : 0));
                    int carsHere = (rng.NextDouble() < carDensity ? 1 : 0) + (rng.NextDouble() < carDensity * 0.4f ? 1 : 0);
                    for (int i = 0; i < carsHere; i++)
                    {
                        bool firstLane = rng.Next(2) == 0;
                        float along = Range(-0.3f, 0.3f) * module;
                        if (alongZ)
                            PlaceFootprint(PickVehicle(), c + new Vector2(firstLane ? 1f : -1f, along), firstLane ? 0f : 180f, asphaltLift, true, false);       // east lane drives north, west lane south
                        else
                            PlaceFootprint(PickVehicle(), c + new Vector2(along, firstLane ? -1f : 1f), firstLane ? 90f : 270f, asphaltLift, true, false);      // south lane drives east, north lane west
                    }
                }
            }
        }

        // ---- a roadblock or two: three barriers in a row across a straight stretch of road (the sidewalks stay open) ----
        if (roadblocks != null && roadblocks.Length > 0)
        {
            List<Vector3Int> candidates = new List<Vector3Int>(straightRoads);
            int blockCount = rng.Next(0, 3);
            for (int i = 0; i < blockCount && candidates.Count > 0; i++)
            {
                int pick = rng.Next(candidates.Count);
                Vector3Int s = candidates[pick];
                candidates.RemoveAt(pick);

                bool northSouth = s.z == 1;
                Vector2 c = Center(s.x, s.y);
                GameObject piece = Pick(roadblocks);
                float yaw = AlignYaw(piece, northSouth);              // lies across the road: along x on a north-south road
                for (int k = -1; k <= 1; k++)
                    PlaceModel(piece, northSouth ? c + new Vector2(k, 0f) : c + new Vector2(0f, k), yaw, true, asphaltLift);
            }
        }

        // ---- street lamps on the sidewalks next to straight stretches of road ----
        if (streetLamp != null)
        {
            float edge = Mathf.Max(2f, Mathf.Round(module * 0.5f - 0.5f));       // a tile near the outer edge of the sidewalk
            foreach (Vector3Int s in straightRoads)
            {
                if (rng.NextDouble() > 0.35)
                    continue;

                bool northSouth = s.z == 1;
                Vector2 c = Center(s.x, s.y);
                int side = rng.Next(2) == 0 ? 1 : -1;
                float along = Mathf.Round(Range(-2f, 2f));
                // the arm of the model points to -x: turned so that it always reaches over the road
                if (northSouth)
                    PlaceProp(streetLamp, c + new Vector2(side * edge, along), side > 0 ? 0f : 180f);
                else
                    PlaceProp(streetLamp, c + new Vector2(along, side * edge), side > 0 ? 270f : 90f);
            }
        }

        // ---- the strip of tiles left over at the edge (the city is a whole number of modules, the field is not) gets sidewalk too ----
        int rightMargin = width - startX - cityTiles;
        int topMargin = height - startY - cityTiles;
        float modulePlate = module;                               // a sidewalk plate is exactly one module wide
        if (startX > 0)
            AddGround(sidewalk, new Vector2(-0.5f + startX * 0.5f, (height - 1) * 0.5f), 0f, startX / modulePlate, height / modulePlate);
        if (rightMargin > 0)
            AddGround(sidewalk, new Vector2(left + cityTiles + rightMargin * 0.5f, (height - 1) * 0.5f), 0f, rightMargin / modulePlate, height / modulePlate);
        if (startY > 0)
            AddGround(sidewalk, new Vector2(left + cityTiles * 0.5f, -0.5f + startY * 0.5f), 0f, cityTiles / modulePlate, startY / modulePlate);
        if (topMargin > 0)
            AddGround(sidewalk, new Vector2(left + cityTiles * 0.5f, bottom + cityTiles + topMargin * 0.5f), 0f, cityTiles / modulePlate, topMargin / modulePlate);

        // ---- the landmark takes a 2 x 2 area of free modules, somewhere at random ----
        if (landmark != null)
        {
            List<Vector2Int> spots = new List<Vector2Int>();
            for (int my = 1; my < Modules - 2; my++)
                for (int mx = 1; mx < Modules - 2; mx++)
                    if (!road[mx, my] && !road[mx + 1, my] && !road[mx, my + 1] && !road[mx + 1, my + 1])
                        spots.Add(new Vector2Int(mx, my));

            if (spots.Count > 0)
            {
                Vector2Int s = spots[rng.Next(spots.Count)];
                Vector2 middle = (Center(s.x, s.y) + Center(s.x + 1, s.y + 1)) * 0.5f;
                if (PlaceFootprint(landmark, middle, 0f, groundLift, true, true))
                {
                    used[s.x, s.y] = used[s.x + 1, s.y] = used[s.x, s.y + 1] = used[s.x + 1, s.y + 1] = true;
                }
            }
        }

        // ---- the plaza: one free module chosen at random ----
        if (fountain != null)
        {
            List<Vector2Int> free = new List<Vector2Int>();
            for (int my = 0; my < Modules; my++)
                for (int mx = 0; mx < Modules; mx++)
                    if (!road[mx, my] && !used[mx, my]) free.Add(new Vector2Int(mx, my));

            if (free.Count > 0)
            {
                Vector2Int p = free[rng.Next(free.Count)];
                Vector2 c = Center(p.x, p.y);
                PlaceFootprint(fountain, c, 0f, groundLift, true, false);
                float o = Mathf.Max(1f, Mathf.Round(module * 0.5f - 1f));         // a palm in every corner of the plaza
                PlaceProp(Pick(trees), c + new Vector2(o, o), Range(0f, 360f));
                PlaceProp(Pick(trees), c + new Vector2(-o, o), Range(0f, 360f));
                PlaceProp(Pick(trees), c + new Vector2(o, -o), Range(0f, 360f));
                PlaceProp(Pick(trees), c + new Vector2(-o, -o), Range(0f, 360f));
                used[p.x, p.y] = true;
            }
        }

        // ---- everything else in the blocks ----
        bool busStopPlaced = false;
        float strip = Mathf.Round(module * 0.5f + 0.9f);       // from a module's middle to the middle of the road's sidewalk next to it
        for (int my = 0; my < Modules; my++)
        {
            for (int mx = 0; mx < Modules; mx++)
            {
                if (road[mx, my] || used[mx, my])
                    continue;

                Vector2 c = Center(mx, my);
                List<int> roadsAround = new List<int>();
                for (int d = 0; d < 4; d++)
                    if (IsRoad(mx + Steps[d].x, my + Steps[d].y)) roadsAround.Add(d);

                if (rng.NextDouble() < parkChance)
                {
                    // a park: two palms and a few bushes, each on its own tile
                    for (int i = 0; i < 2; i++)
                        for (int attempt = 0; attempt < 4 && !PlaceProp(Pick(trees), c + new Vector2(Range(-0.3f, 0.3f), Range(-0.3f, 0.3f)) * module, Range(0f, 360f)); attempt++) { }
                    for (int i = 0; i < 4; i++)
                        for (int attempt = 0; attempt < 4 && !PlaceProp(Pick(bushes), c + new Vector2(Range(-0.4f, 0.4f), Range(-0.4f, 0.4f)) * module, Range(0f, 360f)); attempt++) { }
                }
                else if (houses != null && houses.Length > 0)
                {
                    // a house facing the road (the front of the models is their +Z side unless Front Yaw says otherwise)
                    HouseModel model = houses[rng.Next(houses.Length)];
                    float yaw = roadsAround.Count > 0 ? roadsAround[rng.Next(roadsAround.Count)] * 90f : rng.Next(4) * 90f;
                    bool placed = false;
                    for (int turn = 0; turn < 4 && !placed; turn++)       // the tile in front of the roof ladder must be free: if it is not, try the other turns
                        placed = PlaceFootprint(model.prefab, c, yaw + model.frontYaw + turn * 90f, groundLift, true, true, true, true);
                    if (!placed)
                        PlaceFootprint(model.prefab, c, yaw + model.frontYaw, groundLift, true, true);     // no room for the ladder: a house without roof access
                }

                // trash cans on the sidewalk in front of the block
                foreach (int d in roadsAround)
                {
                    if (rng.NextDouble() < 0.55)
                    {
                        Vector2 side = new Vector2(Steps[d].y, Steps[d].x);          // along the road
                        PlaceProp(Pick(trashCans), c + Dir(d) * strip + side * Range(-0.35f, 0.35f) * module, Range(0f, 360f), true);
                    }
                }

                // cover props between the buildings: crates, barrels, rubble, ruined walls...
                if (blockProps != null && blockProps.Length > 0)
                {
                    int props = Mathf.RoundToInt(coverDensity * rng.Next(0, 4));
                    for (int k = 0; k < props; k++)
                    {
                        GameObject prop = Pick(blockProps);
                        float yaw = rng.Next(4) * 90f;
                        for (int attempt = 0; attempt < 8; attempt++)
                            if (PlaceModel(prop, c + new Vector2(Range(-0.42f, 0.42f), Range(-0.42f, 0.42f)) * module, yaw, true, groundLift))
                                break;
                    }
                }

                // one bus stop on the sidewalk of a road to the south of a block
                if (!busStopPlaced && busStop != null && roadsAround.Contains(2))
                {
                    if (PlaceFootprint(busStop, c + new Vector2(0f, -strip), 0f, groundLift, true, false))
                        busStopPlaced = true;
                }
            }
        }

        // ---- up to two containers (three tiles long, full cover) in free spots of the blocks ----
        if (container != null)
        {
            List<Vector2Int> freeBlocks = new List<Vector2Int>();
            for (int my = 0; my < Modules; my++)
                for (int mx = 0; mx < Modules; mx++)
                    if (!road[mx, my] && !used[mx, my]) freeBlocks.Add(new Vector2Int(mx, my));

            int containers = rng.Next(0, 3);
            for (int i = 0; i < containers && freeBlocks.Count > 0; i++)
            {
                Vector2Int m = freeBlocks[rng.Next(freeBlocks.Count)];
                Vector2 c = Center(m.x, m.y);
                float yaw = AlignYaw(container, rng.Next(2) == 0) + (rng.Next(2) == 0 ? 0f : 180f);
                for (int attempt = 0; attempt < 10; attempt++)
                    if (PlaceModel(container, c + new Vector2(Range(-0.3f, 0.3f), Range(-0.3f, 0.3f)) * module, yaw, true, groundLift))
                        break;
            }
        }

        return objects;
    }

    // Picks the road piece (and how to turn it) for a module from the sides that are road.
    private void ChooseRoadPiece(bool[] open, int count, out GameObject piece, out float yaw)
    {
        bool n = open[0], e = open[1], s = open[2], w = open[3];

        if (count >= 3)
        {
            // T junction: the side that is NOT road decides the turn (unturned it is open N, S and E, closed to the west)
            piece = tJunction;
            yaw = !w ? 0f : !n ? 90f : !e ? 180f : !s ? 270f : 0f;
            return;
        }

        if (count == 2 && !(n && s) && !(e && w))
        {
            // corner (unturned it connects south and east)
            piece = cornerRoad;
            yaw = (s && e) ? 0f : (w && s) ? 90f : (n && w) ? 180f : 270f;
            return;
        }

        // straight (also used for a dead end)
        piece = straightRoad;
        yaw = (n || s || !(e || w)) ? 0f : 90f;
    }

    // true = road. The outer ring always; inside it one north-south street (a 'spine') for every 5 modules of the field's width (one on the
    // classic 7 x 7 field, two on 10 x 10), at least 3 modules apart, and between every two neighbouring north-south streets one east-west side
    // street, in rows 2 to last-2. The side streets on the two sides of one north-south street are never in the same row (that would make a
    // four-way crossing, which the road pieces can not do).
    private bool[,] BuildRoadMap(System.Random rng)
    {
        bool[,] road = new bool[Modules, Modules];
        int last = Modules - 1;

        for (int my = 0; my < Modules; my++)
            for (int mx = 0; mx < Modules; mx++)
                if (mx == 0 || my == 0 || mx == last || my == last)
                    road[mx, my] = true;

        int spines = Mathf.Max(1, Modules / 5);
        List<int> verticals = new List<int> { 0 };
        int previous = -1;
        for (int i = 0; i < spines; i++)
        {
            int lo = i == 0 ? 2 : previous + 3;
            int hi = last - 2 - (spines - 1 - i) * 3;
            if (hi < lo)
                break;

            int column = lo + rng.Next(hi - lo + 1);
            verticals.Add(column);
            previous = column;
            for (int my = 1; my < last; my++)
                road[column, my] = true;
        }
        verticals.Add(last);

        int previousRow = -1;
        for (int i = 0; i + 1 < verticals.Count; i++)
        {
            int rowLo = 2, rowHi = last - 2;
            int row;
            do { row = rowLo + rng.Next(rowHi - rowLo + 1); } while (row == previousRow && rowHi > rowLo);
            previousRow = row;

            for (int mx = verticals[i] + 1; mx < verticals[i + 1]; mx++)
                road[mx, row] = true;
        }

        return road;
    }

    // The size and middle of a prefab's meshes in the prefab's own space (before any scaling or turning).
    private Bounds NativeBounds(GameObject prefab)
    {
        Bounds cached;
        if (nativeBounds.TryGetValue(prefab, out cached))
            return cached;

        Matrix4x4 toRoot = prefab.transform.worldToLocalMatrix;
        bool any = false;
        Bounds result = new Bounds(Vector3.zero, Vector3.one);

        foreach (MeshFilter mf in prefab.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null || mf.name.StartsWith("Ladder"))
                continue;                                   // the roof ladder is not part of the footprint

            Matrix4x4 m = toRoot * mf.transform.localToWorldMatrix;
            Bounds mb = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                Vector3 p = m.MultiplyPoint3x4(corner);
                if (!any) { result = new Bounds(p, Vector3.zero); any = true; }
                else result.Encapsulate(p);
            }
        }

        nativeBounds[prefab] = result;
        return result;
    }

    // Houses take hits and stay standing (they have roofs people can be on).
    private static bool IsSturdy(GameObject prefab)
    {
        string n = prefab.name;
        return n.StartsWith("House") || n.StartsWith("Building") || n.StartsWith("store");
    }

    // How much damage a thing takes before it breaks (a bullet does 30): a bush one hit, a car two, a container nine.
    private static int HealthFor(GameObject prefab)
    {
        string n = prefab.name;
        if (IsSturdy(prefab)) return 400;
        if (n.Contains("Container")) return 250;
        if (n.Contains("TankTrap")) return 200;
        if (n.Contains("Fountain")) return 150;
        if (n.Contains("ConcreteBarrier")) return 150;
        if (n.Contains("RuinedWall")) return 120;
        if (n.Contains("CrateStack")) return 80;
        if (n.Contains("Bus_Stop")) return 80;
        if (n.Contains("Dumpster")) return 70;
        if (n.Contains("Sandbags")) return 60;
        if (n.Contains("Rubble")) return 50;
        if (n.Contains("WreckedCar")) return 40;
        if (n.Contains("Crate")) return 40;
        if (n.Contains("StreetLamp") || n.Contains("traffic_light")) return 40;
        if (n.Contains("Palm")) return 30;
        if (n.Contains("Barrels") || n.Contains("Trash")) return 25;
        if (n.Contains("Bush")) return 15;
        if (n.StartsWith("Car_") || n == "Van" || n.StartsWith("Futuristic") || n.StartsWith("Vehicles_")) return 60;
        return 100;
    }

    private float SizeFactor(GameObject prefab)
    {
        return trees != null && System.Array.IndexOf(trees, prefab) >= 0 ? treeScale : 1f;
    }
}
