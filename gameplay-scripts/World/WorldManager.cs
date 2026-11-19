using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-40)]
public class WorldManager : MonoBehaviour
{
    public static WorldManager Instance { get; private set; }

    enum TileType : byte { Grass, Road, Water }

    // Live-tunable obstacle box. Center = sprite anchor + offset, size in world units.
    [System.Serializable]
    public struct BuildingObstacle
    {
        public Vector2 size;
        public Vector2 offset;
    }

    [Header("Chunk")]
    [SerializeField] int   _chunkSize    = 16;
    [SerializeField] float _tileSize     = 1f;
    [SerializeField] int   _activeRadius = 2;       // in chunks (1 = 3x3)
    [SerializeField] int   _tilesPerFrame = 96;

    [Header("Grass background")]
    [Tooltip("Side length of one grass sprite in world units. The chunk gets tiled with grass at this scale.")]
    [SerializeField] int _grassTile = 10;

    [Header("Generation")]
    [SerializeField] int   _seed            = 1270;
    [Tooltip("Smaller values = larger water bodies. 0.025 ~= ~40-tile features.")]
    [SerializeField] float _waterNoiseScale = 0.025f;
    [Tooltip("Higher = rarer water tiles.")]
    [SerializeField, Range(0f, 1f)] float _waterThreshold  = 0.86f;
    [SerializeField] float _waterSlowFactor = 0.55f;

    [Header("Roads — grid")]
    [Tooltip("Grid spacing (in tiles) between major north-south streets. Bigger = more space between roads.")]
    [SerializeField] int  _roadGridX = 60;
    [Tooltip("Grid spacing (in tiles) between major east-west streets.")]
    [SerializeField] int  _roadGridY = 60;
    [Tooltip("Random jitter (in tiles) per major street, so roads aren't perfectly straight.")]
    [SerializeField] int  _roadGridJitter = 3;
    [Tooltip("Half-thickness (in tiles) of a major street. 0 = 1-tile-wide, 1 = 3-tile-wide, etc.")]
    [SerializeField] int  _roadHalfThickness = 1;

    [Header("Roads — visual")]
    [Tooltip("How many world units one path tile sprite spans (3 = each tile renders 3x bigger than _tileSize).")]
    [SerializeField] float _pathTileScale = 3f;
    [Tooltip("Render only every Nth road cell along each axis to avoid overlapping path tiles. Should match _pathTileScale.")]
    [SerializeField] int   _pathRenderStride = 3;

    [Header("Roads — scatter")]
    [SerializeField, Range(0f, 0.05f)] float _scatterRoadChance = 0.0015f;

    [Header("House Placement")]
    [Tooltip("Roll applied to the one candidate per house block. Higher = more blocks get a house.")]
    [SerializeField, Range(0f, 1f)] float _houseChance       = 1;
    [SerializeField, Range(0f, 1f)] float _houseRewardChance = 0.10f;
    [Tooltip("At most one house may spawn per NxN tile block, so they don't pile next to each other.")]
    [SerializeField] int _houseBlockSize = 5;
    [Tooltip("How many tiles away from a road a house anchor may sit. Must be large enough that the obstacle box (default 5x5 = ~3 tiles half-extent) clears the road. Default 6 leaves room for placement variation.")]
    [SerializeField] int _houseRoadDistance = 6;
    [Tooltip("Extra clearance (world units) required between two adjacent houses.")]
    [SerializeField] float _houseSpacingBuffer = 1f;

    [Header("Village Layout")]
    [Tooltip("Half-extent of each village in tiles (Chebyshev). Roads, houses, and the central tower exist only inside this radius around each village center. Outside is grass + decorations. Set <= 0 to disable (legacy infinite procedural).")]
    [SerializeField] int _villageRadius = 22;
    [Tooltip("Distance (in tiles) between village centers on a grid. Player can walk between them through wilderness. Set <= 0 to spawn only a single village at the origin.")]
    [SerializeField] int _villageSpacing = 140;

    [Header("Tower Placement")]
    [Tooltip("Legacy per-intersection tower chance. Ignored when _villageRadius > 0 (one tower per village center is forced instead).")]
    [SerializeField, Range(0f, 1f)] float _towerChance = 0.50f;

    [Header("Outside-Village Decoration")]
    [Tooltip("Per-tile chance to place a destructible tree on grass outside any village. Trees register as obstacles and block enemies/arrows.")]
    [SerializeField, Range(0f, 0.1f)] float _treeChance = 0.008f;
    [Tooltip("Per-tile chance to place a visual-only bush on grass outside any village. Bushes do not block movement.")]
    [SerializeField, Range(0f, 0.2f)] float _bushChance = 0.018f;
    [SerializeField] float _treeHp = 30f;
    [SerializeField] BuildingObstacle _treeObstacle = new BuildingObstacle { size = new Vector2(0.6f, 0.6f), offset = Vector2.zero };
    [SerializeField] Vector2 _treeScale = new Vector2(1.4f, 1.6f);
    [SerializeField] Vector2 _bushScale = new Vector2(0.8f, 0.8f);
    [SerializeField] Color   _treeColor = new Color(0.22f, 0.45f, 0.18f, 1f);
    [SerializeField] Color   _bushColor = new Color(0.30f, 0.55f, 0.25f, 1f);

    [Header("Building Stats")]
    [SerializeField] float _houseHp = 85f;
    [SerializeField] float _towerHp = 300f;
    [SerializeField] float _towerRewardXp = 250f;
    [SerializeField] float _houseRewardXp = 25f;

    [Header("Tower Reward Loop (on tower destroy)")]
    [Tooltip("Blood currency awarded on tower kill. Routed through AuthSession.BloodCoins (same single write point MissionManager uses) so a future backend swap stays one-line.")]
    [SerializeField] int   _towerRewardBlood     = 50;
    [Tooltip("How many bonus XP orbs scatter around the tower on kill (in addition to the single big reward orb). Higher = a satisfying cascade when the magnet pulls them in.")]
    [SerializeField] int   _towerXpBurstCount    = 6;
    [Tooltip("XP value per bonus orb in the burst.")]
    [SerializeField] float _towerXpBurstOrbValue = 30f;
    [Tooltip("World-unit radius the bonus orbs scatter inside.")]
    [SerializeField] float _towerXpBurstRadius   = 1.5f;
    [Tooltip("On tower kill, every loose XP orb inside this radius is instant-collected. One-shot magnet pickup — no new pool, no per-frame magnet state.")]
    [SerializeField] float _towerMagnetRadius    = 8f;

    [Header("Building Collision Overrides")]
    [Tooltip("If on, the obstacle boxes below override the values stored in WorldSetLibrary. Adjust these to align the spatial-hash collision with the actual house silhouettes.")]
    [SerializeField] bool _useCollisionOverrides = true;
    // Restored sensible offsets — zero offsets cause clearance tests to align incorrectly with visual anchors.
    [SerializeField] BuildingObstacle _houseAVertical   = new BuildingObstacle { size = new Vector2(5f, 5f), offset = new Vector2(0, 0) };
    [SerializeField] BuildingObstacle _houseAHorizontal = new BuildingObstacle { size = new Vector2(5f, 5f), offset = new Vector2(0, 0) };
    [SerializeField] BuildingObstacle _houseBVertical   = new BuildingObstacle { size = new Vector2(5f, 5f), offset = new Vector2(0, 0) };
    [SerializeField] BuildingObstacle _houseBHorizontal = new BuildingObstacle { size = new Vector2(5f, 5f), offset = new Vector2(0, 0) };
    [SerializeField] BuildingObstacle _tower            = new BuildingObstacle { size = new Vector2(5f, 5f), offset = Vector2.zero };

    [Header("Debug")]
    [SerializeField] bool _showCollisionGizmos = true;
    [SerializeField] bool _verboseHouseDebug = false; // toggle to enable logs

    [Header("Sprites (auto-loaded from Resources/WorldSetLibrary)")]
    [SerializeField] WorldSetLibrary _library;

    [Header("Fallback Colors (used only if library sprites missing)")]
    [SerializeField] Color _grassColor = new Color(0.32f, 0.56f, 0.22f);
    [SerializeField] Color _roadColor  = new Color(0.45f, 0.42f, 0.38f);
    [SerializeField] Color _waterColor = new Color(0.18f, 0.42f, 0.72f);
    [SerializeField] Color _houseColor = new Color(0.72f, 0.50f, 0.30f);
    [SerializeField] Color _towerColor = new Color(0.55f, 0.55f, 0.60f);

    Transform           _player;
    SpatialHashManager  _spatial;
    Transform           _grassRoot;
    Transform           _tileRoot;
    Transform           _buildingRoot;

    DynamicPool<SpriteRenderer> _grassPool;
    DynamicPool<SpriteRenderer> _tilePool;
    DynamicPool<SpriteRenderer> _bushPool;
    DynamicPool<Destructible>   _housePool;
    DynamicPool<Destructible>   _towerPool;
    DynamicPool<Destructible>   _treePool;

    Transform _decoRoot;

    readonly Dictionary<Vector2Int, Chunk> _chunks         = new Dictionary<Vector2Int, Chunk>(32);
    readonly List<Vector2Int>              _toUnload       = new List<Vector2Int>(16);
    readonly Queue<Chunk>                  _buildQueue     = new Queue<Chunk>(9);
    readonly HashSet<long>                 _destroyedSites = new HashSet<long>();

    System.Action<Destructible> _onBuildingDefeated;
    Vector2Int _lastPlayerChunk = new Vector2Int(int.MinValue, int.MinValue);
    bool       _buildCoroutineRunning;

    struct HouseCandidate
    {
        public int  wx;
        public int  wy;
        public bool useB;
        public bool useHorizontal;
        public bool eligible;
        public BuildingObstacle obstacle;
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void Init(Transform player, SpatialHashManager spatial)
    {
        _player  = player;
        _spatial = spatial;

        if (_library == null) _library = Resources.Load<WorldSetLibrary>("WorldSetLibrary");

        _grassRoot    = new GameObject("[World_Grass]").transform;
        _tileRoot     = new GameObject("[World_Tiles]").transform;
        _buildingRoot = new GameObject("[World_Buildings]").transform;
        _decoRoot     = new GameObject("[World_Deco]").transform;
        _grassRoot.SetParent(transform, false);
        _tileRoot.SetParent(transform, false);
        _buildingRoot.SetParent(transform, false);
        _decoRoot.SetParent(transform, false);

        // Per-chunk grass count: (chunk / tile) ^ 2 sprites per chunk.
        int grassPerChunk = Mathf.CeilToInt(_chunkSize * _tileSize / _grassTile);
        grassPerChunk *= grassPerChunk;
        _grassPool = new DynamicPool<SpriteRenderer>(CreateGrassBackground, grassPerChunk * 4);
        _tilePool  = new DynamicPool<SpriteRenderer>(CreateTile, _chunkSize * _chunkSize);
        _bushPool  = new DynamicPool<SpriteRenderer>(CreateBush, 64);
        _housePool = new DynamicPool<Destructible>(CreateHouse, 12);
        _towerPool = new DynamicPool<Destructible>(CreateTower, 4);
        _treePool  = new DynamicPool<Destructible>(CreateTree, 32);

        _onBuildingDefeated = OnBuildingDefeated;
    }

    /// <summary>Nearest village center on the spacing grid. When _villageSpacing &lt;= 0,
    /// the only village is at the world origin.</summary>
    Vector2Int NearestVillageCenter(int wx, int wy)
    {
        if (_villageSpacing <= 0) return Vector2Int.zero;
        int gx = Mathf.RoundToInt(wx / (float)_villageSpacing);
        int gy = Mathf.RoundToInt(wy / (float)_villageSpacing);
        return new Vector2Int(gx * _villageSpacing, gy * _villageSpacing);
    }

    // --- Public read-only accessors for other systems (VillageSpawnManager, etc.) -------

    public int   VillageRadius  => _villageRadius;
    public int   VillageSpacing => _villageSpacing;
    public float TileSize       => _tileSize;

    /// <summary>True if the tower at the given tile has been destroyed by the player. Towers
    /// register their tile in _destroyedSites in OnBuildingDefeated.</summary>
    public bool IsTowerDestroyed(int tileX, int tileY)
    {
        return _destroyedSites.Contains(PackKey(tileX, tileY));
    }

    /// <summary>Fills `output` with up to 9 village center tiles forming a 3×3 grid around the
    /// player's nearest village center. Used by the village tower spawner to find which towers
    /// are close enough to be active. Allocation-free — callers reuse a List.</summary>
    public void GetNearbyVillageCenters(Vector2 worldPos, List<Vector2Int> output)
    {
        output.Clear();
        if (_villageRadius <= 0) return;
        if (_villageSpacing <= 0) { output.Add(Vector2Int.zero); return; }

        int wx = Mathf.FloorToInt(worldPos.x / _tileSize);
        int wy = Mathf.FloorToInt(worldPos.y / _tileSize);
        Vector2Int nearest = NearestVillageCenter(wx, wy);
        int gx = nearest.x / _villageSpacing;
        int gy = nearest.y / _villageSpacing;

        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
            output.Add(new Vector2Int((gx + dx) * _villageSpacing, (gy + dy) * _villageSpacing));
    }

    /// <summary>True when (wx,wy) sits inside any village's radius (Chebyshev distance from
    /// the nearest village center). Multi-village mode tiles the world with finite villages
    /// every _villageSpacing tiles. When _villageRadius is non-positive, the world is treated
    /// as fully open and the original infinite-procedural behavior applies.</summary>
    bool IsInsideVillage(int wx, int wy)
    {
        if (_villageRadius <= 0) return true;
        Vector2Int center = NearestVillageCenter(wx, wy);
        int dx = wx - center.x; if (dx < 0) dx = -dx;
        int dy = wy - center.y; if (dy < 0) dy = -dy;
        return dx <= _villageRadius && dy <= _villageRadius;
    }

    void Update()
    {
        if (_player == null) return;
        Vector2Int pc = WorldToChunk(_player.position);
        if (pc == _lastPlayerChunk) return;
        _lastPlayerChunk = pc;
        RefreshActiveChunks(pc);
    }

    void RefreshActiveChunks(Vector2Int center)
    {
        _toUnload.Clear();
        foreach (var kv in _chunks)
        {
            Vector2Int d = kv.Key - center;
            if (Mathf.Abs(d.x) > _activeRadius || Mathf.Abs(d.y) > _activeRadius)
                _toUnload.Add(kv.Key);
        }
        for (int i = 0; i < _toUnload.Count; i++)
            UnloadChunk(_toUnload[i]);

        for (int dy = -_activeRadius; dy <= _activeRadius; dy++)
        for (int dx = -_activeRadius; dx <= _activeRadius; dx++)
        {
            var coord = new Vector2Int(center.x + dx, center.y + dy);
            if (_chunks.ContainsKey(coord)) continue;

            var c = new Chunk { Coord = coord, Generating = true };
            _chunks[coord] = c;
            _buildQueue.Enqueue(c);
        }

        if (!_buildCoroutineRunning && _buildQueue.Count > 0)
            StartCoroutine(BuildQueuedChunks());
    }

    IEnumerator BuildQueuedChunks()
    {
        _buildCoroutineRunning = true;
        int budget = _tilesPerFrame;
        while (_buildQueue.Count > 0)
        {
            Chunk c = _buildQueue.Dequeue();
            if (!_chunks.TryGetValue(c.Coord, out var tracked) || tracked != c) continue;

            int baseX = c.Coord.x * _chunkSize;
            int baseY = c.Coord.y * _chunkSize;
            bool cancelled = false;

            SpawnGrassBackgroundsForChunk(c);

            for (int ty = 0; ty < _chunkSize && !cancelled; ty++)
            for (int tx = 0; tx < _chunkSize && !cancelled; tx++)
            {
                int worldTx = baseX + tx;
                int worldTy = baseY + ty;

                TileType type = SampleTile(worldTx, worldTy);

                if (type == TileType.Water)
                {
                    SpawnTile(c, worldTx, worldTy, type);
                }
                else if (type == TileType.Road)
                {
                    // Visual road tiles render at _pathTileScale (e.g. 3 units wide). We only
                    // spawn at every Nth cell to avoid 9x overdraw — gameplay road logic still
                    // covers every road cell for movement purposes.
                    if (ShouldRenderRoadCell(worldTx, worldTy))
                        SpawnTile(c, worldTx, worldTy, type);
                }
                else if (IsInsideVillage(worldTx, worldTy))
                {
                    // Per-tile procedural placement runs only in legacy (non-village) mode.
                    // In village mode, houses are placed deterministically per village in
                    // TrySpawnVillageHousesInChunk so every village reliably has a ring of
                    // houses around its tower (no luck-of-the-hash zero-house villages).
                    if (_villageRadius <= 0)
                        TrySpawnHouse(c, worldTx, worldTy);
                }
                else
                {
                    TrySpawnDecoration(c, worldTx, worldTy);
                }

                if (--budget <= 0)
                {
                    budget = _tilesPerFrame;
                    yield return null;
                    if (!_chunks.TryGetValue(c.Coord, out var still) || still != c)
                    {
                        ReturnChunkContents(c);
                        cancelled = true;
                    }
                }
            }

            if (!cancelled) TrySpawnTowersInChunk(c);
            if (!cancelled) TrySpawnVillageHousesInChunk(c);
            if (!cancelled) c.Generating = false;
        }
        _buildCoroutineRunning = false;
    }

    bool ShouldRenderRoadCell(int wx, int wy)
    {
        int stride = Mathf.Max(1, _pathRenderStride);
        if (stride == 1) return true;
        return FloorMod(wx, stride) == 0 && FloorMod(wy, stride) == 0;
    }

    // --- Generation ---------------------------------------------------------

    TileType SampleTile(int wx, int wy)
    {
        if (IsRoad(wx, wy)) return TileType.Road;

        // No water inside the village — keeps combat space readable around the tower & houses.
        if (IsInsideVillage(wx, wy)) return TileType.Grass;

        float nx = (wx + _seed) * _waterNoiseScale;
        float ny = (wy + _seed * 31) * _waterNoiseScale;
        float n  = Mathf.PerlinNoise(nx, ny);
        return n > _waterThreshold ? TileType.Water : TileType.Grass;
    }

    bool IsRoad(int wx, int wy)
    {
        // Multi-village mode: each village has a local + cross of roads through its center.
        // Roads do not exist outside the village radius — the world feels finite per village.
        if (_villageRadius > 0)
        {
            Vector2Int center = NearestVillageCenter(wx, wy);
            int dx = wx - center.x;
            int dy = wy - center.y;
            int adx = dx < 0 ? -dx : dx;
            int ady = dy < 0 ? -dy : dy;
            if (adx > _villageRadius || ady > _villageRadius) return false;
            if (adx <= _roadHalfThickness) return true;
            if (ady <= _roadHalfThickness) return true;
            return false;
        }

        // Legacy infinite procedural roads.
        int distToVertical   = DistanceToJitteredGridLine(wx, wy, _roadGridX, axisIsX: true);
        if (distToVertical <= _roadHalfThickness) return true;

        int distToHorizontal = DistanceToJitteredGridLine(wx, wy, _roadGridY, axisIsX: false);
        if (distToHorizontal <= _roadHalfThickness) return true;

        if (_scatterRoadChance > 0f && Hash01(wx, wy, _seed + 53) < _scatterRoadChance)
            return true;

        return false;
    }

    int DistanceToJitteredGridLine(int wx, int wy, int gridSpacing, bool axisIsX)
    {
        if (gridSpacing <= 1) return int.MaxValue;

        int along  = axisIsX ? wx : wy;
        int across = axisIsX ? wy : wx;

        int idx = FloorDiv(along, gridSpacing);
        int distA = Mathf.Abs(along - GridLineFor(idx,     gridSpacing, across, axisIsX));
        int distB = Mathf.Abs(along - GridLineFor(idx + 1, gridSpacing, across, axisIsX));
        return distA < distB ? distA : distB;
    }

    int GridLineFor(int gridIndex, int gridSpacing, int across, bool axisIsX)
    {
        int baseLine = gridIndex * gridSpacing;
        if (_roadGridJitter <= 0) return baseLine;

        int   bucket = FloorDiv(across, 12);
        float h      = Hash01(gridIndex, bucket, _seed + (axisIsX ? 71 : 113));
        int   j      = Mathf.RoundToInt((h * 2f - 1f) * _roadGridJitter);
        return baseLine + j;
    }

    // --- House placement ----------------------------------------------------

    void TrySpawnHouse(Chunk c, int wx, int wy)
    {
        if (!IsInsideVillage(wx, wy)) return;

        long key = PackKey(wx, wy);
        if (_verboseHouseDebug) Debug.LogFormat("[World] TrySpawnHouse tile={0},{1} blockSize={2}", wx, wy, _houseBlockSize);

        if (_destroyedSites.Contains(key))
        {
            if (_verboseHouseDebug) Debug.LogFormat("[World] TrySpawnHouse rejected: destroyed site {0},{1}", wx, wy);
            return;
        }
        if (_houseBlockSize <= 1)
        {
            if (_verboseHouseDebug) Debug.LogFormat("[World] TrySpawnHouse rejected: _houseBlockSize <= 1 ({0})", _houseBlockSize);
            return;
        }

        int bx = FloorDiv(wx, _houseBlockSize);
        int by = FloorDiv(wy, _houseBlockSize);
        HouseCandidate cand = GetHouseCandidate(bx, by);

        if (_verboseHouseDebug) Debug.LogFormat("[World] Candidate for block {0},{1} -> anchor {2},{3} eligible={4} useHorizontal={5} useB={6}",
            bx, by, cand.wx, cand.wy, cand.eligible, cand.useHorizontal, cand.useB);

        if (!cand.eligible)
        {
            if (_verboseHouseDebug) Debug.LogFormat("[World] TrySpawnHouse rejected: candidate not eligible for block {0},{1}", bx, by);
            return;
        }

        if (cand.wx != wx || cand.wy != wy)
        {
            if (_verboseHouseDebug) Debug.LogFormat("[World] TrySpawnHouse skipping: current tile is not anchor (tile {0},{1} anchor {2},{3})", wx, wy, cand.wx, cand.wy);
            return;
        }

        bool clearance = HasBoxClearance(cand.wx, cand.wy, cand.obstacle.size, cand.obstacle.offset, 0);
        if (!clearance)
        {
            if (_verboseHouseDebug) Debug.LogFormat("[World] TrySpawnHouse rejected: HasBoxClearance failed for anchor {0},{1} size={2} offset={3}", cand.wx, cand.wy, cand.obstacle.size, cand.obstacle.offset);
            return;
        }

        bool overlap = OverlapsNeighborHouse(bx, by, cand);
        if (overlap)
        {
            if (_verboseHouseDebug) Debug.LogFormat("[World] TrySpawnHouse rejected: overlaps neighbor in block {0},{1}", bx, by);
            return;
        }

        bool reward = Hash01(wx, wy, _seed + 29) < _houseRewardChance;
        if (_verboseHouseDebug) Debug.LogFormat("[World] Spawning house at {0},{1} (reward={2})", wx, wy, reward);
        SpawnBuilding(c, wx, wy, Destructible.Kind.House, reward, cand.useB, cand.useHorizontal);
    }

    HouseCandidate GetHouseCandidate(int bx, int by)
    {
        HouseCandidate r = default;
        r.wx = bx * _houseBlockSize + (int)(Hash01(bx, by, _seed + 101) * _houseBlockSize);
        r.wy = by * _houseBlockSize + (int)(Hash01(bx, by, _seed + 103) * _houseBlockSize);

        // Random roll
        if (Hash01(r.wx, r.wy, _seed + 11) >= _houseChance) return r;

        // If anchor is on a road we skip (no house on road)
        if (IsRoad(r.wx, r.wy)) return r;

        // Anchor must sit NORTH of a horizontal road, or EAST/WEST of a vertical road,
        // within _houseRoadDistance tiles. Houses south of horizontal roads are excluded
        // per design spec ("only beside top of horizontal road").
        bool roadToSouth = false;
        bool roadToEast  = false;
        bool roadToWest  = false;
        for (int k = 1; k <= _houseRoadDistance; k++)
        {
            if (!roadToSouth && IsRoad(r.wx, r.wy - k)) roadToSouth = true;
            if (!roadToEast  && IsRoad(r.wx + k, r.wy)) roadToEast  = true;
            if (!roadToWest  && IsRoad(r.wx - k, r.wy)) roadToWest  = true;
            if (roadToSouth && roadToEast && roadToWest) break;
        }
        if (!roadToSouth && !roadToEast && !roadToWest) return r;

        r.useB          = Hash01(r.wx, r.wy, _seed + 41) < 0.5f;
        // Horizontal sprite for a house tucked above (north of) a horizontal road; vertical
        // sprite when standing beside a vertical road.
        r.useHorizontal = roadToSouth;
        r.obstacle      = GetHouseObstacle(r.useB, r.useHorizontal);
        r.eligible      = true;
        return r;
    }

    bool OverlapsNeighborHouse(int bx, int by, HouseCandidate cand)
    {
        Vector2 myCenter = new Vector2(
            (cand.wx + 0.5f) * _tileSize + cand.obstacle.offset.x,
            (cand.wy + 0.5f) * _tileSize + cand.obstacle.offset.y);
        Vector2 myHalf = cand.obstacle.size * 0.5f;
        float buffer = Mathf.Max(0f, _houseSpacingBuffer);

        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            if (dx == 0 && dy == 0) continue;
            HouseCandidate nb = GetHouseCandidate(bx + dx, by + dy);
            if (!nb.eligible) continue;

            Vector2 nbCenter = new Vector2(
                (nb.wx + 0.5f) * _tileSize + nb.obstacle.offset.x,
                (nb.wy + 0.5f) * _tileSize + nb.obstacle.offset.y);
            Vector2 nbHalf = nb.obstacle.size * 0.5f;

            if (Mathf.Abs(myCenter.x - nbCenter.x) < (myHalf.x + nbHalf.x + buffer) &&
                Mathf.Abs(myCenter.y - nbCenter.y) < (myHalf.y + nbHalf.y + buffer))
                return true;
        }
        return false;
    }

    BuildingObstacle GetHouseObstacle(bool useB, bool useHorizontal)
    {
        if (_useCollisionOverrides)
        {
            if (useB && useHorizontal)  return _houseBHorizontal;
            if (useB && !useHorizontal) return _houseBVertical;
            if (!useB && useHorizontal) return _houseAHorizontal;
            return _houseAVertical;
        }
        var v = PickHouseVariant(useB, useHorizontal);
        return new BuildingObstacle
        {
            size   = v != null ? v.obstacleSize   : new Vector2(5f, 5f),
            offset = v != null ? v.obstacleOffset : Vector2.zero,
        };
    }

    BuildingObstacle GetTowerObstacle()
    {
        if (_useCollisionOverrides) return _tower;
        var t = _library != null ? _library.tower : null;
        return new BuildingObstacle
        {
            size   = t != null ? t.obstacleSize   : new Vector2(5f, 5f),
            offset = t != null ? t.obstacleOffset : Vector2.zero,
        };
    }

    // --- Tower placement ----------------------------------------------------

    void TrySpawnTowersInChunk(Chunk c)
    {
        int xMin = c.Coord.x * _chunkSize;
        int yMin = c.Coord.y * _chunkSize;
        int xMax = xMin + _chunkSize - 1;
        int yMax = yMin + _chunkSize - 1;

        if (_villageRadius > 0)
        {
            // Multi-village mode: one tower per village center on the spacing grid.
            // Sweep all centers whose tile could fall inside this chunk.
            if (_villageSpacing <= 0)
            {
                TrySpawnTowerAt(c, 0, 0, xMin, xMax, yMin, yMax);
                return;
            }

            int gxMin = Mathf.FloorToInt(xMin / (float)_villageSpacing) - 1;
            int gxMax = Mathf.CeilToInt (xMax / (float)_villageSpacing) + 1;
            int gyMin = Mathf.FloorToInt(yMin / (float)_villageSpacing) - 1;
            int gyMax = Mathf.CeilToInt (yMax / (float)_villageSpacing) + 1;
            for (int gy = gyMin; gy <= gyMax; gy++)
            for (int gx = gxMin; gx <= gxMax; gx++)
            {
                TrySpawnTowerAt(c, gx * _villageSpacing, gy * _villageSpacing, xMin, xMax, yMin, yMax);
            }
            return;
        }

        // Legacy infinite-procedural mode: each (gxIdx, gyIdx) pair has one canonical
        // intersection center, rolled against _towerChance.
        int slack = Mathf.Max(0, _roadGridJitter);
        int legacyGxMin = FloorDiv(xMin - slack, _roadGridX) - 1;
        int legacyGxMax = FloorDiv(xMax + slack, _roadGridX) + 1;
        int legacyGyMin = FloorDiv(yMin - slack, _roadGridY) - 1;
        int legacyGyMax = FloorDiv(yMax + slack, _roadGridY) + 1;

        for (int gy = legacyGyMin; gy <= legacyGyMax; gy++)
        for (int gx = legacyGxMin; gx <= legacyGxMax; gx++)
        {
            int ix = GridLineFor(gx, _roadGridX, gy * _roadGridY, axisIsX: true);
            int iy = GridLineFor(gy, _roadGridY, gx * _roadGridX, axisIsX: false);
            if (ix < xMin || ix > xMax || iy < yMin || iy > yMax) continue;

            long key = PackKey(ix, iy);
            if (_destroyedSites.Contains(key)) continue;
            if (Hash01(ix, iy, _seed + 7) >= _towerChance) continue;

            SpawnBuilding(c, ix, iy, Destructible.Kind.Tower, false);
        }
    }

    void TrySpawnTowerAt(Chunk c, int cx, int cy, int xMin, int xMax, int yMin, int yMax)
    {
        if (cx < xMin || cx > xMax || cy < yMin || cy > yMax) return;
        long key = PackKey(cx, cy);
        if (_destroyedSites.Contains(key)) return;
        SpawnBuilding(c, cx, cy, Destructible.Kind.Tower, false);
    }

    /// <summary>Fixed slot offsets (relative to a village center) where houses are guaranteed
    /// to spawn. 6 slots = a reliable ring of houses around the tower. Each entry is
    /// (dx, dy, useB, useHorizontal). Distances are chosen so the 5x5 obstacle box clears
    /// the road tiles (which sit at |d| <= _roadHalfThickness).</summary>
    static readonly System.ValueTuple<int, int, bool, bool>[] _villageHouseSlots = new[]
    {
        // Inner ring — diagonal quadrants of the tower.
        ( 8,  8, false, false),
        (-8,  8, true,  false),
        ( 8, -8, true,  false),
        (-8, -8, false, false),
        // Outer pair tucked north of the horizontal road.
        ( 14,  4, false, true),
        (-14,  4, true,  true),
    };

    /// <summary>Forces a deterministic ring of houses around every village center whose slot
    /// could land inside this chunk. This is what guarantees "every village has at least 4-6
    /// houses" — the random per-block sweep can't be relied on (some hash combos fail clearance
    /// against the road and produce zero-house villages).</summary>
    void TrySpawnVillageHousesInChunk(Chunk c)
    {
        if (_villageRadius <= 0) return;

        int xMin = c.Coord.x * _chunkSize;
        int yMin = c.Coord.y * _chunkSize;
        int xMax = xMin + _chunkSize - 1;
        int yMax = yMin + _chunkSize - 1;

        if (_villageSpacing <= 0)
        {
            SpawnVillageHousesAt(c, 0, 0, xMin, xMax, yMin, yMax);
            return;
        }

        int spacing = _villageSpacing;
        // Slot offset magnitudes are <= ~14, so widening the village-center sweep by that
        // catches houses whose slot lands inside this chunk even when the center is outside.
        const int slotPad = 16;
        int gxMin = Mathf.FloorToInt((xMin - slotPad) / (float)spacing) - 1;
        int gxMax = Mathf.CeilToInt ((xMax + slotPad) / (float)spacing) + 1;
        int gyMin = Mathf.FloorToInt((yMin - slotPad) / (float)spacing) - 1;
        int gyMax = Mathf.CeilToInt ((yMax + slotPad) / (float)spacing) + 1;
        for (int gy = gyMin; gy <= gyMax; gy++)
        for (int gx = gxMin; gx <= gxMax; gx++)
        {
            SpawnVillageHousesAt(c, gx * spacing, gy * spacing, xMin, xMax, yMin, yMax);
        }
    }

    void SpawnVillageHousesAt(Chunk c, int cx, int cy, int xMin, int xMax, int yMin, int yMax)
    {
        for (int i = 0; i < _villageHouseSlots.Length; i++)
        {
            var slot = _villageHouseSlots[i];
            int hx = cx + slot.Item1;
            int hy = cy + slot.Item2;
            if (hx < xMin || hx > xMax || hy < yMin || hy > yMax) continue;

            long key = PackKey(hx, hy);
            if (_destroyedSites.Contains(key)) continue;
            if (IsRoad(hx, hy)) continue;

            // Per-village deterministic flip of variant A/B + reward, so neighboring villages
            // don't all look identical but every village still has a stable layout.
            float vh = Hash01(cx, cy, _seed + 911);
            bool useB         = ((i + (vh > 0.5f ? 1 : 0)) & 1) == 0 ? slot.Item3 : !slot.Item3;
            bool reward       = Hash01(hx, hy, _seed + 29) < _houseRewardChance;
            bool useHorizontal = slot.Item4;

            SpawnBuilding(c, hx, hy, Destructible.Kind.House, reward, useB, useHorizontal);
        }
    }

    /// <summary>Outside-village decoration: rolls per-tile for a tree, then a bush. Never both.</summary>
    void TrySpawnDecoration(Chunk c, int wx, int wy)
    {
        long key = PackKey(wx, wy);
        if (_destroyedSites.Contains(key)) return;

        float roll = Hash01(wx, wy, _seed + 211);
        if (roll < _treeChance)
        {
            SpawnTree(c, wx, wy);
        }
        else if (roll < _treeChance + _bushChance)
        {
            SpawnBush(c, wx, wy);
        }
    }

    // Verifies the building's actual obstacle box (centered at spawn + offset, size 'size')
    // does not overlap any road tile, plus a small extra buffer on every side.
    bool HasBoxClearance(int wx, int wy, Vector2 size, Vector2 offset, int extraBuffer)
    {
        float cx = (wx + 0.5f) * _tileSize + offset.x;
        float cy = (wy + 0.5f) * _tileSize + offset.y;
        float halfX = size.x * 0.5f;
        float halfY = size.y * 0.5f;

        int xMin = Mathf.FloorToInt((cx - halfX) / _tileSize) - extraBuffer;
        int xMax = Mathf.CeilToInt ((cx + halfX) / _tileSize) + extraBuffer;
        int yMin = Mathf.FloorToInt((cy - halfY) / _tileSize) - extraBuffer;
        int yMax = Mathf.CeilToInt ((cy + halfY) / _tileSize) + extraBuffer;

        for (int ty = yMin; ty <= yMax; ty++)
        for (int tx = xMin; tx <= xMax; tx++)
            if (IsRoad(tx, ty)) return false;
        return true;
    }

    // --- Spawning -----------------------------------------------------------

    void SpawnGrassBackgroundsForChunk(Chunk c)
    {
        int chunkUnits = _chunkSize * (int)_tileSize;
        int tilesPerSide = Mathf.CeilToInt(chunkUnits / (float)_grassTile);

        float originX = c.Coord.x * chunkUnits;
        float originY = c.Coord.y * chunkUnits;

        Sprite grassSprite = _library != null ? _library.grass : null;

        for (int gy = 0; gy < tilesPerSide; gy++)
        for (int gx = 0; gx < tilesPerSide; gx++)
        {
            float cx = originX + (gx + 0.5f) * _grassTile;
            float cy = originY + (gy + 0.5f) * _grassTile;

            var sr = _grassPool.Get(new Vector3(cx, cy, 0f), Quaternion.identity);
            sr.transform.SetParent(_grassRoot, true);

            if (grassSprite != null)
            {
                float spriteUnits = grassSprite.bounds.size.x;
                float s = spriteUnits > 0.0001f ? _grassTile / spriteUnits : 1f;
                sr.transform.localScale = new Vector3(s, s, 1f);
                sr.sprite = grassSprite;
                sr.color  = Color.white;
            }
            else
            {
                sr.sprite = PlaceholderSprites.GetSquare(32);
                sr.color  = _grassColor;
                sr.transform.localScale = new Vector3(_grassTile, _grassTile, 1f);
            }
            sr.sortingOrder = -12;
            c.Grass.Add(sr);
        }
    }

    void SpawnTile(Chunk c, int wx, int wy, TileType type)
    {
        Vector3 pos = TileCenter(wx, wy);
        var sr = _tilePool.Get(pos, Quaternion.identity);
        sr.transform.SetParent(_tileRoot, true);
        sr.color = Color.white;

        Sprite tileSprite = null;
        // World-space side length the tile should cover after scaling.
        float renderUnits = _tileSize;
        switch (type)
        {
            case TileType.Road:
                tileSprite = PickRoadSprite(wx, wy);
                if (tileSprite == null) { tileSprite = PlaceholderSprites.GetSquare(32); sr.color = _roadColor; }
                renderUnits = Mathf.Max(_tileSize, _pathTileScale);
                sr.sortingOrder = -8;
                break;
            case TileType.Water:
                tileSprite = _library != null ? _library.water : null;
                if (tileSprite == null) { tileSprite = PlaceholderSprites.GetSquare(32); sr.color = _waterColor; }
                sr.sortingOrder = -9;
                break;
            default:
                tileSprite = PlaceholderSprites.GetSquare(32);
                sr.color   = _grassColor;
                sr.sortingOrder = -10;
                break;
        }

        if (tileSprite != null)
        {
            float spriteUnits = tileSprite.bounds.size.x;
            if (spriteUnits > 0.0001f)
            {
                float s = renderUnits / spriteUnits;
                sr.transform.localScale = new Vector3(s, s, 1f);
            }
            else
            {
                sr.transform.localScale = new Vector3(renderUnits, renderUnits, 1f);
            }
        }
        else
        {
            sr.transform.localScale = new Vector3(renderUnits, renderUnits, 1f);
        }
        sr.sprite = tileSprite;
        c.Tiles.Add(sr);
    }

    Sprite PickRoadSprite(int wx, int wy)
    {
        var pathTiles    = _library != null ? _library.pathTiles : null;
        var roadFallback = _library != null ? _library.fullPath : null;

        if (pathTiles == null || pathTiles.Length == 0)
        {
            if (roadFallback == null && pathTiles != null)
                for (int i = 0; i < pathTiles.Length; i++)
                    if (pathTiles[i] != null) return pathTiles[i];
            return roadFallback;
        }

        // Deterministic per-tile variation so the path looks varied but stable across reloads.
        uint h = (uint)Mathf.FloorToInt(Hash01(wx, wy, _seed + 89) * 1000003);
        int idx = (int)(h % (uint)pathTiles.Length);

        for (int probe = 0; probe < pathTiles.Length; probe++)
        {
            int i = (idx + probe) % pathTiles.Length;
            if (pathTiles[i] != null) return pathTiles[i];
        }
        return roadFallback;
    }

    void SpawnBuilding(Chunk c, int wx, int wy, Destructible.Kind kind, bool reward,
                       bool useB = false, bool useHorizontal = false)
    {
        Vector3 pos = TileCenter(wx, wy);
        Destructible d;
        if (kind == Destructible.Kind.House)
        {
            d = _housePool.Get(pos, Quaternion.identity);
            d.transform.SetParent(_buildingRoot, true);

            var variant = PickHouseVariant(useB, useHorizontal);
            BuildingObstacle obs = GetHouseObstacle(useB, useHorizontal);
            Vector2 scale     = variant != null ? variant.scale : new Vector2(10f, 10f);
            Sprite  intact    = variant != null ? variant.sprite : null;
            Sprite  destroyed = _library != null ? _library.houseDestroyed : null;

            d.Setup(kind, _houseHp, obs.size, obs.offset, scale, _houseColor,
                    reward, 5, _onBuildingDefeated, intact, destroyed);
        }
        else
        {
            d = _towerPool.Get(pos, Quaternion.identity);
            d.transform.SetParent(_buildingRoot, true);

            var tower = _library != null ? _library.tower : null;
            BuildingObstacle obs = GetTowerObstacle();
            Vector2 scale  = tower != null ? tower.scale  : new Vector2(13.1f, 13.1f);

            // Prefer the SkillArt tower sprites when assigned (per Goal-of-the-Day brief —
            // tower visually changes between intact and destroyed states). Fall back to the
            // existing WorldSet library when the registry is missing or empty.
            Sprite intact    = (SkillArt.Instance != null && SkillArt.Instance.towerNormal    != null)
                ? SkillArt.Instance.towerNormal
                : (tower != null ? tower.sprite : null);
            Sprite destroyed = (SkillArt.Instance != null && SkillArt.Instance.towerDestroyed != null)
                ? SkillArt.Instance.towerDestroyed
                : null;

            d.Setup(kind, _towerHp, obs.size, obs.offset, scale, _towerColor,
                    true, 6, _onBuildingDefeated, intact, destroyed);
        }
        c.Destructibles.Add(d);
    }

    void SpawnTree(Chunk c, int wx, int wy)
    {
        Vector3 pos = TileCenter(wx, wy);
        var d = _treePool.Get(pos, Quaternion.identity);
        d.transform.SetParent(_buildingRoot, true);

        // Per Goal-of-the-Day brief: trees use individually-sliced sprites (Trees_0/1/2 etc.).
        // Pick one per tile using the same deterministic hash pattern SpawnBush uses, so the
        // same tile always shows the same tree across reloads / chunk evictions.
        Sprite intact = null;
        if (_library != null)
        {
            Sprite[] variants = _library.trees;
            if (variants != null && variants.Length > 0)
            {
                uint h = (uint)Mathf.FloorToInt(Hash01(wx, wy, _seed + 991) * 1000003);
                intact = variants[(int)(h % (uint)variants.Length)];
            }
            if (intact == null) intact = _library.tree;       // legacy single-sprite fallback
        }
        d.Setup(Destructible.Kind.Tree, _treeHp, _treeObstacle.size, _treeObstacle.offset,
                _treeScale, _treeColor, hasReward: false, sortingOrder: 4,
                onDefeated: _onBuildingDefeated, intactSprite: intact, destroyedSprite: null);
        c.Destructibles.Add(d);
    }

    void SpawnBush(Chunk c, int wx, int wy)
    {
        Vector3 pos = TileCenter(wx, wy);
        var sr = _bushPool.Get(pos, Quaternion.identity);
        sr.transform.SetParent(_decoRoot, true);

        Sprite[] bushes = _library != null ? _library.bushes : null;
        Sprite chosen = null;
        if (bushes != null && bushes.Length > 0)
        {
            uint h = (uint)Mathf.FloorToInt(Hash01(wx, wy, _seed + 233) * 1000003);
            chosen = bushes[(int)(h % (uint)bushes.Length)];
        }
        if (chosen == null) chosen = PlaceholderSprites.GetCircle(64);

        sr.sprite        = chosen;
        sr.color         = chosen != null && _library != null && _library.bushes != null && _library.bushes.Length > 0
            ? Color.white  // real sprite — don't tint
            : _bushColor;
        sr.transform.localScale = new Vector3(_bushScale.x, _bushScale.y, 1f);
        sr.sortingOrder  = 3;
        c.Bushes.Add(sr);
    }

    WorldSetLibrary.HouseVariant PickHouseVariant(bool useB, bool useHorizontal)
    {
        if (_library == null) return null;

        var primary = useB
            ? (useHorizontal ? _library.houseBHorizontal : _library.houseBVertical)
            : (useHorizontal ? _library.houseAHorizontal : _library.houseAVertical);
        if (primary != null && primary.sprite != null) return primary;

        // Fallbacks if a sprite is missing: try the other orientation, then the other variant.
        var altOrient = useB
            ? (useHorizontal ? _library.houseBVertical : _library.houseBHorizontal)
            : (useHorizontal ? _library.houseAVertical : _library.houseAHorizontal);
        if (altOrient != null && altOrient.sprite != null) return altOrient;

        var altVariant = useB
            ? (useHorizontal ? _library.houseAHorizontal : _library.houseAVertical)
            : (useHorizontal ? _library.houseBHorizontal : _library.houseBVertical);
        if (altVariant != null && altVariant.sprite != null) return altVariant;

        return primary;
    }

    void OnBuildingDefeated(Destructible d)
    {
        // Use the spawn anchor (transform.position) so the destroyed-site key matches the
        // tile we picked at spawn time, not the obstacle box center.
        Vector2Int tile = WorldToTile(d.SpriteAnchor);
        _destroyedSites.Add(PackKey(tile.x, tile.y));

        if (d.BuildingKind == Destructible.Kind.Tower)
        {
            // Brief: tower destruction plays an explosion VFX (6-frame Effect_Explotion sprite
            // from SkillArt). Pooled to satisfy the no-Instantiate-during-combat rule.
            PlayExplosionVfx(d.SpriteAnchor);
            AwardTowerRewards(d.SpriteAnchor);
        }
        else if (d.HasReward && XpManager.Instance != null)
        {
            XpManager.Instance.SpawnOrb(d.SpriteAnchor, _houseRewardXp);
        }
    }

    DynamicPool<ExplosionVfx> _explosionPool;
    Transform                 _explosionRoot;

    void EnsureExplosionPool()
    {
        if (_explosionPool != null) return;
        _explosionRoot = new GameObject("[ExplosionPool]").transform;
        _explosionPool = new DynamicPool<ExplosionVfx>(CreateExplosion, 4);
    }

    ExplosionVfx CreateExplosion()
    {
        var go = new GameObject("Explosion");
        go.transform.SetParent(_explosionRoot, true);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sortingOrder = 5000;
        var vfx = go.AddComponent<ExplosionVfx>();
        vfx.Configure(_explosionPool, sr);
        return vfx;
    }

    void PlayExplosionVfx(Vector2 worldPos)
    {
        Sprite[] frames = SkillArt.Instance != null ? SkillArt.Instance.effectExplosion : null;
        if (frames == null || frames.Length == 0) return;       // no art assigned — skip silently
        EnsureExplosionPool();
        var fx = _explosionPool.Get(worldPos, Quaternion.identity);
        fx.Play(worldPos, frames, fps: 18f, visualScale: 2.2f);
    }

    /// <summary>
    /// Tower kill payoff. Layered so the player feels the survival-then-reward arc:
    ///   1. Big single XP orb (the headline drop).
    ///   2. Scattered XP burst — small orbs around the anchor for visual cascade.
    ///   3. Blood currency through the same AuthSession.BloodCoins single-write the mission
    ///      system uses (backend swap = one line in this method).
    ///   4. One-shot magnet collect: every loose orb in radius (including the burst we just
    ///      spawned) is instant-collected so the XP bar visibly jumps. No new pool, no
    ///      per-frame magnet state.
    /// </summary>
    void AwardTowerRewards(Vector2 anchor)
    {
        var xp = XpManager.Instance;
        if (xp != null)
        {
            xp.SpawnOrb(anchor, _towerRewardXp);

            int burst = Mathf.Max(0, _towerXpBurstCount);
            for (int i = 0; i < burst; i++)
            {
                Vector2 jitter = Random.insideUnitCircle * _towerXpBurstRadius;
                xp.SpawnOrb(anchor + jitter, _towerXpBurstOrbValue);
            }

            if (_towerMagnetRadius > 0f)
                xp.MagnetCollectInRadius(anchor, _towerMagnetRadius);
        }

        // Per Goal-of-the-Day brief: Blood is ONLY rewarded from mission completion. Tower
        // destruction now counts toward the DestroyTowers / DestroyBuildings missions (via
        // Destructible.OnAnyDefeated -> MissionManager), and those grant Blood on completion.
        // The per-tower Blood credit that used to live here would double-credit when the
        // DestroyTowers mission completed, so it has been removed. _towerRewardBlood stays
        // serialized for backwards-compatibility but is intentionally unused.
    }

    void UnloadChunk(Vector2Int coord)
    {
        if (!_chunks.TryGetValue(coord, out var c)) return;
        ReturnChunkContents(c);
        _chunks.Remove(coord);
    }

    void ReturnChunkContents(Chunk c)
    {
        for (int i = 0; i < c.Grass.Count; i++)
            _grassPool.Return(c.Grass[i]);
        c.Grass.Clear();

        for (int i = 0; i < c.Tiles.Count; i++)
            _tilePool.Return(c.Tiles[i]);
        c.Tiles.Clear();

        for (int i = 0; i < c.Bushes.Count; i++)
            _bushPool.Return(c.Bushes[i]);
        c.Bushes.Clear();

        for (int i = 0; i < c.Destructibles.Count; i++)
        {
            var d = c.Destructibles[i];
            if (d == null) continue;
            switch (d.BuildingKind)
            {
                case Destructible.Kind.House: _housePool.Return(d); break;
                case Destructible.Kind.Tower: _towerPool.Return(d); break;
                case Destructible.Kind.Tree:  _treePool.Return(d);  break;
            }
        }
        c.Destructibles.Clear();
    }

    // --- Factories ----------------------------------------------------------

    SpriteRenderer CreateGrassBackground()
    {
        var go = new GameObject("Grass");
        go.transform.SetParent(_grassRoot, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = (_library != null && _library.grass != null) ? _library.grass : PlaceholderSprites.GetSquare(32);
        sr.sortingOrder = -12;
        return sr;
    }

    SpriteRenderer CreateTile()
    {
        var go = new GameObject("Tile");
        go.transform.SetParent(_tileRoot, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = PlaceholderSprites.GetSquare(32);
        return sr;
    }

    Destructible CreateHouse() => CreateBuilding("House", _houseColor, 5);
    Destructible CreateTower() => CreateBuilding("Tower", _towerColor, 6);
    Destructible CreateTree()  => CreateBuilding("Tree",  _treeColor, 4);

    Destructible CreateBuilding(string name, Color color, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(_buildingRoot, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite        = PlaceholderSprites.GetSquare(32);
        sr.color         = color;
        sr.sortingOrder  = order;
        return go.AddComponent<Destructible>();
    }

    SpriteRenderer CreateBush()
    {
        var go = new GameObject("Bush");
        go.transform.SetParent(_decoRoot, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite       = PlaceholderSprites.GetCircle(64);
        sr.color        = _bushColor;
        sr.sortingOrder = 3;
        return sr;
    }

    // --- Public queries -----------------------------------------------------

    public bool IsWaterAt(Vector2 worldPos)
    {
        int tx = Mathf.FloorToInt(worldPos.x / _tileSize);
        int ty = Mathf.FloorToInt(worldPos.y / _tileSize);
        return SampleTile(tx, ty) == TileType.Water;
    }

    public float GetMoveSpeedMultiplier(Vector2 worldPos)
    {
        return IsWaterAt(worldPos) ? _waterSlowFactor : 1f;
    }

    // --- Player spawn ------------------------------------------------------

    /// <summary>
    /// Returns a wilderness world position safe to drop the player on at boot. Aims roughly
    /// midway between the origin village and the next village along the X axis, then jitters
    /// outward in a golden-angle spiral until it finds a tile that isn't water, road, tree,
    /// bush, or inside any village's house ring. The village is therefore something the
    /// player has to walk to, not their starting view.
    /// </summary>
    public Vector2 FindSafeSpawnPoint()
    {
        int spacing    = _villageSpacing > 0 ? _villageSpacing : 80;
        int radius     = Mathf.Max(1, _villageRadius);
        // Outside the origin village's radius and roughly halfway to the next center on +X.
        int targetDist = Mathf.Max(radius * 2, spacing / 2 - radius - 4);

        for (int i = 0; i < 64; i++)
        {
            float angle   = i * 137.5077f * Mathf.Deg2Rad;
            float spiralR = targetDist + i * 0.7f;
            float cx      = Mathf.Cos(angle) * spiralR;
            float cy      = Mathf.Sin(angle) * spiralR;
            int wx        = Mathf.FloorToInt(cx / _tileSize);
            int wy        = Mathf.FloorToInt(cy / _tileSize);
            if (IsSafeSpawnTile(wx, wy))
                return new Vector2((wx + 0.5f) * _tileSize, (wy + 0.5f) * _tileSize);
        }

        // Fallback: place at the original aim even if not perfectly clear (extremely unlikely
        // to be reached given how sparse decoration is).
        int fwx = Mathf.FloorToInt(targetDist / _tileSize);
        return new Vector2((fwx + 0.5f) * _tileSize, 0.5f * _tileSize);
    }

    bool IsSafeSpawnTile(int wx, int wy)
    {
        if (IsInsideVillage(wx, wy)) return false;     // village interior is off-limits
        if (IsRoad(wx, wy)) return false;              // sanity (roads only inside village)
        if (SampleTile(wx, wy) == TileType.Water) return false;

        // Avoid landing on a procedural tree/bush slot — the wilderness sweep would have
        // dropped one here. _treeChance + _bushChance is the union of decoration rolls used
        // by TrySpawnDecoration.
        float roll = Hash01(wx, wy, _seed + 211);
        if (roll < _treeChance + _bushChance) return false;

        return true;
    }

    // --- Loading-time preload ---------------------------------------------

    /// <summary>
    /// Forces the active ring of chunks around the player to be generated synchronously
    /// (well — frame-sliced) and yields until the build queue is empty. Used by
    /// LoadingManager so the world is ready before gameplay begins. Temporarily raises
    /// the per-frame tile budget so loading completes quickly.
    /// </summary>
    public IEnumerator PreloadAroundPlayer()
    {
        if (_player == null) yield break;

        int savedBudget = _tilesPerFrame;
        _tilesPerFrame = Mathf.Max(savedBudget, 384);

        Vector2Int pc = WorldToChunk(_player.position);
        if (_lastPlayerChunk != pc || _chunks.Count == 0)
        {
            _lastPlayerChunk = pc;
            RefreshActiveChunks(pc);
        }

        while (_buildCoroutineRunning || _buildQueue.Count > 0)
            yield return null;

        _tilesPerFrame = savedBudget;
    }

    // --- Helpers ------------------------------------------------------------

    Vector2Int WorldToChunk(Vector3 world)
    {
        int tx = Mathf.FloorToInt(world.x / _tileSize);
        int ty = Mathf.FloorToInt(world.y / _tileSize);
        return new Vector2Int(FloorDiv(tx, _chunkSize), FloorDiv(ty, _chunkSize));
    }

    Vector2Int WorldToTile(Vector3 world)
    {
        return new Vector2Int(
            Mathf.FloorToInt(world.x / _tileSize),
            Mathf.FloorToInt(world.y / _tileSize));
    }

    Vector3 TileCenter(int wx, int wy)
    {
        return new Vector3((wx + 0.5f) * _tileSize, (wy + 0.5f) * _tileSize, 0f);
    }

    static int FloorDiv(int a, int b) => (a >= 0 ? a : a - b + 1) / b;
    static int FloorMod(int a, int b)
    {
        int m = a % b;
        return m < 0 ? m + b : m;
    }
    static long PackKey(int x, int y)
    {
        unchecked { return ((long)(uint)x << 32) | (uint)y; }
    }

    static float Hash01(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393);
            h = (h + (uint)(y * 668265263)) ^ (uint)seed;
            h = (h ^ (h >> 13)) * 1274126177;
            h = h ^ (h >> 16);
            return (h & 0xFFFFFF) / (float)0x1000000;
        }
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (!_showCollisionGizmos || _chunks == null) return;

        Gizmos.color = new Color(1f, 0.35f, 0.35f, 0.85f);
        foreach (var kv in _chunks)
        {
            var c = kv.Value;
            for (int i = 0; i < c.Destructibles.Count; i++)
            {
                var d = c.Destructibles[i];
                if (d == null || !d.gameObject.activeInHierarchy || d.IsDead) continue;
                Vector2 pos  = d.Position;
                Vector2 size = d.ObstacleHalfExtents * 2f;
                Gizmos.DrawWireCube(new Vector3(pos.x, pos.y, 0f), new Vector3(size.x, size.y, 0.01f));
            }
        }
    }
#endif
}
