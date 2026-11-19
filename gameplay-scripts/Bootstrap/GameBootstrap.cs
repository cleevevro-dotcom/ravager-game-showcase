using System.Collections;
using UnityEngine;

public class GameBootstrap : MonoBehaviour
{
    [SerializeField] PlayerStats        _playerStats;
    [SerializeField] Transform          _playerTransform;
    [SerializeField] SpatialHashManager _spatialHashManager;
    [SerializeField] EnemyManager       _enemyManager;
    [SerializeField] WaveManager        _waveManager;
    [SerializeField] XpManager          _xpManager;
    [SerializeField] LevelUpManager     _levelUpManager;
    [SerializeField] HUDManager         _hudManager;
    [SerializeField] DeathScreen        _deathScreen;

    [Header("Skills (auto-created if not set)")]
    [SerializeField] SkillManager  _skillManager;
    [SerializeField] SkillHudPanel _skillHud;

    [Header("Boss (auto-created if not set)")]
    [SerializeField] BossEncounter _bossEncounter;

    [Header("Missions (auto-created if not set)")]
    [SerializeField] MissionManager _missionManager;

    [Header("Input (auto-created if not set)")]
    [SerializeField] InputDriver    _inputDriver;
    [SerializeField] MobileControls _mobileControls;

    [Header("World (auto-created if not set)")]
    [SerializeField] WorldManager _worldManager;
    [SerializeField] VillageSpawnManager _villageSpawnManager;

    [Header("Loading (auto-created if not set)")]
    [SerializeField] LoadingManager _loadingManager;

    [Header("Editable HUD root + pause + audio + status (auto-created if not set)")]
    [SerializeField] GameScreenHub     _gameScreenHub;
    [SerializeField] PauseController   _pauseController;
    [SerializeField] AudioManager      _audioManager;
    [SerializeField] StatusBanner      _statusBanner;
    [SerializeField] PlatformBootstrap _platformBootstrap;
    [SerializeField] GlobalFontApplier _globalFontApplier;
    [SerializeField] GameOverManager   _gameOverManager;

    [Header("Dark Vision overlay (auto-created if not set)")]
    [SerializeField] DarkVisionOverlay _darkVisionOverlay;

    [Header("Beast selection (visible in Hierarchy — auto-created if not set)")]
    [Tooltip("Owns the player's selected beast. Backed by PlayerPrefs; synced with shop/profile when online.")]
    [SerializeField] BeastManager     _beastManager;
    [Tooltip("Editor / testing override. When _forceSelectedBeast is true, overrides the BeastManager selection at run start.")]
    [SerializeField] CharacterManager _characterManager;

    void Awake()
    {
        // Bring up the loading manager + UI in Awake so the dark panel covers
        // the gameplay scene from frame 1, before any player/world rendering.
        EnsureLoadingManager();
        // The HUD root + audio + pause systems must exist before any HUD-binding
        // script runs (Mission/Skill/HUD all parent into GameCanvas), so spin them
        // up alongside the loading panel.
        EnsurePlatformBootstrap();
        EnsureGameScreenHub();
        EnsureAudioManager();
        EnsurePauseController();
        EnsureStatusBanner();
        EnsureGlobalFontApplier();
        EnsureGameOverManager();
    }

    void EnsureGameOverManager()
    {
        if (_gameOverManager != null) return;
        var existing = FindObjectOfType<GameOverManager>(includeInactive: true);
        if (existing != null) { _gameOverManager = existing; return; }
        // Build on a separate root so its overlay canvas isn't tied to [GameUI]
        // (which gets destroyed on Pause -> Exit). Stays alive until the player
        // restarts the scene.
        var go = new GameObject("[GameOverManager]");
        go.transform.SetParent(transform, false);
        _gameOverManager = go.AddComponent<GameOverManager>();
    }

    void EnsureGlobalFontApplier()
    {
        if (_globalFontApplier != null) return;
        if (GlobalFontApplier.Instance != null) { _globalFontApplier = GlobalFontApplier.Instance; return; }
        var existing = FindObjectOfType<GlobalFontApplier>(includeInactive: true);
        if (existing != null) { _globalFontApplier = existing; return; }
        var go = new GameObject("[GlobalFontApplier]");
        go.transform.SetParent(transform, false);
        _globalFontApplier = go.AddComponent<GlobalFontApplier>();
    }

    void Start()
    {
        _loadingManager.BeginLoad(this);
    }

    // --- Load stages (called from LoadingManager) --------------------------

    public void Step_EnsureSystems()
    {
        EnsureInput();
        ConfigurePlayerAttack();
        EnsureSkillManager();
        EnsureSkillHud();
        EnsureWorldManager();
        EnsureVillageSpawnManager();
        EnsureBossEncounter();
        EnsureMissionManager();
        EnsureDarkVisionOverlay();
        // Beast selection managers must exist before Step_WireGameplay calls
        // BeastApplier.ApplyAtRunStart — both their Awakes (PlayerPrefs read for BeastManager,
        // singleton hookup for CharacterManager) need to have run by then.
        EnsureCharacterManager();
        EnsureBeastManager();
    }

    public void Step_PrewarmPools()
    {
        // Each manager's Init() builds its pools. World pools (grass / tile / house /
        // tower) are created here so the village preload step can use them.
        _xpManager.Init(_playerTransform, _playerStats);
        _enemyManager.Init(_playerTransform, _playerStats, _spatialHashManager);
        _worldManager.Init(_playerTransform, _spatialHashManager);

        // Relocate the player into a wilderness spawn so the village is something they
        // discover by exploring, not their starting view. Snap the camera too — otherwise
        // CameraFollow would lerp across the world for a second after the loading panel hides.
        if (_playerTransform != null && _worldManager != null)
        {
            Vector2 spawn = _worldManager.FindSafeSpawnPoint();
            _playerTransform.position = new Vector3(spawn.x, spawn.y, _playerTransform.position.z);

            var cam = Camera.main;
            if (cam != null)
            {
                var follow = cam.GetComponent<CameraFollow>();
                if (follow != null) follow.Snap();
                else cam.transform.position = new Vector3(spawn.x, spawn.y, cam.transform.position.z);
            }
        }
    }

    public IEnumerator Step_PreloadVillage()
    {
        if (_worldManager == null) yield break;
        yield return _worldManager.PreloadAroundPlayer();
    }

    public void Step_WireGameplay()
    {
        _waveManager.Init(_enemyManager);
        _skillManager.Init(_playerStats, _playerTransform, _spatialHashManager);
        _levelUpManager.Init(_playerStats);
        _hudManager.Init(_playerStats, _waveManager, _enemyManager);
        if (_deathScreen != null) _deathScreen.Init(_playerStats, _waveManager);
        _bossEncounter.Init(_playerTransform, _playerStats, _spatialHashManager, _enemyManager);
        if (_villageSpawnManager != null)
            _villageSpawnManager.Init(_playerTransform, _playerStats, _enemyManager);
        if (_missionManager != null)
            _missionManager.Init(_playerStats);
        if (_darkVisionOverlay != null)
            _darkVisionOverlay.Init(_playerTransform, _playerStats, Camera.main);

        // Apply the selected beast LAST so it sees a fully-initialized SkillManager (BeastApplier
        // reapplies Physical-skill stacks to the active attack). CharacterManager's force override
        // is consulted inside ApplyAtRunStart so editor/testing flags take priority cleanly.
        BeastApplier.ApplyAtRunStart(_beastManager);
    }

    // --- Auto-creation helpers --------------------------------------------

    /// <summary>Ensures PlayerClawAttack + a ClawEffect child exist on the player.</summary>
    void ConfigurePlayerAttack()
    {
        if (_playerTransform == null) return;
        var playerGO = _playerTransform.gameObject;

        var claw = playerGO.GetComponent<PlayerClawAttack>();
        if (claw == null) claw = playerGO.AddComponent<PlayerClawAttack>();

        // Hit feedback: knockback + throttled camera shake when the player takes damage.
        // PlayerStats.TakeDamage(amount, fromPos) routes here automatically.
        if (playerGO.GetComponent<PlayerHitFeedback>() == null)
            playerGO.AddComponent<PlayerHitFeedback>();

        // Ensure a ClawEffect child exists and is wired
        var effectField = typeof(PlayerClawAttack).GetField("_clawEffect",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        ClawEffect effect = effectField?.GetValue(claw) as ClawEffect;
        if (effect == null)
        {
            var eGO = new GameObject("ClawEffect");
            eGO.transform.SetParent(playerGO.transform, false);
            var sr = eGO.AddComponent<SpriteRenderer>();
            sr.sprite       = PlaceholderSprites.GetRing(64, 0.18f);
            sr.color        = new Color(1f, 0.9f, 0.7f, 0.9f);
            sr.sortingOrder = 31000; // always above Y-sorted characters (max ~20000+Y*10)
            effect = eGO.AddComponent<ClawEffect>();
            eGO.SetActive(false);
            effectField?.SetValue(claw, effect);
        }
    }

    void EnsureLoadingManager()
    {
        if (_loadingManager != null) return;
        var go = new GameObject("[LoadingManager]");
        go.transform.SetParent(transform, false);
        _loadingManager = go.AddComponent<LoadingManager>();
    }

    // All Ensure helpers below search the scene for an existing component (FindObjectOfType
    // catches editor-authored [GameUI] roots even when the singleton's Awake hasn't fired
    // yet) BEFORE spawning a new GameObject. This way the editor builder's GameObjects are
    // always preferred and never duplicated.

    void EnsureGameScreenHub()
    {
        if (_gameScreenHub != null) return;
        if (GameScreenHub.Instance != null) { _gameScreenHub = GameScreenHub.Instance; return; }
        var existing = FindObjectOfType<GameScreenHub>(includeInactive: true);
        if (existing != null) { _gameScreenHub = existing; return; }
        var go = new GameObject("[GameUI]");
        go.transform.SetParent(transform, false);
        _gameScreenHub = go.AddComponent<GameScreenHub>();
    }

    void EnsurePauseController()
    {
        if (_pauseController != null) return;
        if (PauseController.Instance != null) { _pauseController = PauseController.Instance; return; }
        var existing = FindObjectOfType<PauseController>(includeInactive: true);
        if (existing != null) { _pauseController = existing; return; }
        var go = new GameObject("[PauseController]");
        go.transform.SetParent(transform, false);
        _pauseController = go.AddComponent<PauseController>();
    }

    void EnsureAudioManager()
    {
        if (_audioManager != null) return;
        if (AudioManager.Instance != null) { _audioManager = AudioManager.Instance; return; }
        var existing = FindObjectOfType<AudioManager>(includeInactive: true);
        if (existing != null) { _audioManager = existing; return; }
        var go = new GameObject("[AudioManager]");
        go.transform.SetParent(transform, false);
        _audioManager = go.AddComponent<AudioManager>();
    }

    void EnsureStatusBanner()
    {
        if (_statusBanner != null) return;
        if (StatusBanner.Instance != null) { _statusBanner = StatusBanner.Instance; return; }
        var existing = FindObjectOfType<StatusBanner>(includeInactive: true);
        if (existing != null) { _statusBanner = existing; return; }
        var go = new GameObject("[StatusBanner]");
        go.transform.SetParent(transform, false);
        _statusBanner = go.AddComponent<StatusBanner>();
    }

    void EnsurePlatformBootstrap()
    {
        if (_platformBootstrap != null) return;
        if (PlatformBootstrap.Instance != null) { _platformBootstrap = PlatformBootstrap.Instance; return; }
        var existing = FindObjectOfType<PlatformBootstrap>(includeInactive: true);
        if (existing != null) { _platformBootstrap = existing; return; }
        var go = new GameObject("[PlatformBootstrap]");
        go.transform.SetParent(transform, false);
        _platformBootstrap = go.AddComponent<PlatformBootstrap>();
    }

    void EnsureSkillManager()
    {
        if (_skillManager != null) return;
        var go = new GameObject("[SkillManager]");
        go.transform.SetParent(transform, false);
        _skillManager = go.AddComponent<SkillManager>();
    }

    void EnsureSkillHud()
    {
        if (_skillHud != null) return;
        var go = new GameObject("[SkillHudPanel]");
        go.transform.SetParent(transform, false);
        _skillHud = go.AddComponent<SkillHudPanel>();
    }

    void EnsureWorldManager()
    {
        if (_worldManager != null) return;
        var go = new GameObject("[WorldManager]");
        go.transform.SetParent(transform, false);
        _worldManager = go.AddComponent<WorldManager>();
    }

    void EnsureVillageSpawnManager()
    {
        if (_villageSpawnManager != null) return;
        var go = new GameObject("[VillageSpawnManager]");
        go.transform.SetParent(transform, false);
        _villageSpawnManager = go.AddComponent<VillageSpawnManager>();
    }

    void EnsureBossEncounter()
    {
        if (_bossEncounter != null) return;
        var go = new GameObject("[BossEncounter]");
        go.transform.SetParent(transform, false);
        _bossEncounter = go.AddComponent<BossEncounter>();
    }

    void EnsureMissionManager()
    {
        if (_missionManager != null) return;
        var go = new GameObject("[MissionManager]");
        go.transform.SetParent(transform, false);
        _missionManager = go.AddComponent<MissionManager>();
    }

    void EnsureDarkVisionOverlay()
    {
        if (_darkVisionOverlay != null) return;
        var go = new GameObject("[DarkVisionOverlay]");
        go.transform.SetParent(transform, false);
        _darkVisionOverlay = go.AddComponent<DarkVisionOverlay>();
    }

    /// <summary>
    /// Auto-creates a top-level [BeastManager] GameObject if the scene doesn't already wire one.
    /// The preferred setup is a hand-placed scene object (so the dropdown/PlayerPrefs survives
    /// across runs), but we fall back to runtime creation so a fresh scene still works.
    /// </summary>
    void EnsureBeastManager()
    {
        if (_beastManager != null) return;
        if (BeastManager.Instance != null) { _beastManager = BeastManager.Instance; return; }
        var go = new GameObject("[BeastManager]");
        go.transform.SetParent(transform, false);
        _beastManager = go.AddComponent<BeastManager>();
    }

    /// <summary>
    /// Auto-creates a top-level [CharacterManager] GameObject for the editor/testing force-override.
    /// Default: _forceSelectedBeast = false, so the run uses the normal BeastManager selection.
    /// Spec: keep this VISIBLE in the Hierarchy so the developer can flip the toggle and pick a
    /// beast in the Inspector before pressing Play.
    /// </summary>
    void EnsureCharacterManager()
    {
        if (_characterManager != null) return;
        if (CharacterManager.Instance != null) { _characterManager = CharacterManager.Instance; return; }
        var go = new GameObject("[CharacterManager]");
        go.transform.SetParent(transform, false);
        _characterManager = go.AddComponent<CharacterManager>();
    }

    void EnsureInput()
    {
        if (_inputDriver == null)
        {
            var go = new GameObject("[InputDriver]");
            go.transform.SetParent(transform, false);
            _inputDriver = go.AddComponent<InputDriver>();
        }
        if (_mobileControls == null)
        {
            var go = new GameObject("[MobileControls]");
            go.transform.SetParent(transform, false);
            _mobileControls = go.AddComponent<MobileControls>();
        }
    }
}
