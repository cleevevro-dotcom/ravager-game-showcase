using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Single in-run mission tracker. One mission active at a time, displayed in the top-right
/// corner of the HUD. Progress is event-driven (no per-frame polling for kill / destroy
/// missions; only the survive timer reads WaveManager each frame). Reward is paid in Blood
/// (AuthSession.BloodCoins) — a tiny AwardBlood wrapper makes future backend swap one-line.
///
/// Structure-first: all mission types live in one MissionType enum + one MissionDefinition list.
/// Add new types by extending the enum, the OnEnemyKilled / OnBuildingDefeated switch arms, and
/// FormatTitle. No per-mission classes — keeps the surface small until a real designer wants
/// per-mission custom logic.
/// </summary>
public class MissionManager : MonoBehaviour
{
    public static MissionManager Instance { get; private set; }

    /// <summary>Phase 10: hard cap so a long run can't farm unlimited Blood.
    /// Backend enforces the same cap server-side (firestore_repo.MAX_MISSIONS_PER_RUN).</summary>
    public const int MaxCompletedPerRun = 4;

    /// <summary>Maximum Blood earnable from missions in one run. Matches the backend
    /// MAX_MISSION_BLOOD_PER_RUN constant in runs.py. Once reached, no new missions
    /// are offered — but score-based run Blood still applies separately.</summary>
    public const int MaxMissionBloodPerRun = 250;

    [System.Serializable]
    public class MissionDefinition
    {
        public MissionType type;
        public int target      = 10;
        public int bloodReward = 10;
    }

    int _completedThisRun;
    int _bloodEarnedThisRun;

    [Header("Mission pool — picked at random when one completes")]
    [SerializeField] MissionDefinition[] _missions;

    [Header("UI")]
    [Tooltip("Pixels of padding from the top-right corner of the screen.")]
    [SerializeField] Vector2 _uiOffset = new Vector2(-24f, -24f);
    [SerializeField] int     _titleFontSize = 28;
    [SerializeField] int     _completionFontSize = 24;
    [SerializeField] float   _completionFlashDuration = 2.5f;

    PlayerStats        _stats;
    MissionDefinition  _current;
    int                _progress;
    float              _missionStartElapsed;

    TextMeshProUGUI    _missionLabel;
    TextMeshProUGUI    _completionLabel;
    Coroutine          _completionFlashRoutine;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        if (_missions == null || _missions.Length == 0) BuildDefaultMissionPool();
    }

    void OnDestroy()
    {
        UnsubscribeStats();
        Destructible.OnAnyDefeated -= OnBuildingDefeated;
        if (Instance == this) Instance = null;
    }

    public void Init(PlayerStats stats)
    {
        UnsubscribeStats();
        _stats = stats;
        if (_stats != null) _stats.OnEnemyKilled += OnEnemyKilled;
        Destructible.OnAnyDefeated -= OnBuildingDefeated;  // de-dup if Init called twice
        Destructible.OnAnyDefeated += OnBuildingDefeated;

        // Phase 10: fresh run = no completed missions yet.
        _completedThisRun    = 0;
        _bloodEarnedThisRun  = 0;

        BuildUI();
        PickNextMission();
    }

    void UnsubscribeStats()
    {
        if (_stats != null) _stats.OnEnemyKilled -= OnEnemyKilled;
        _stats = null;
    }

    void Update()
    {
        if (_current == null) return;
        if (GameManager.Instance == null || GameManager.Instance.State != GameState.Playing) return;
        if (_current.type != MissionType.SurviveSeconds) return;

        // Survive missions sample WaveManager elapsed time once per frame. Cheap.
        float elapsed = WaveManager.Instance != null ? WaveManager.Instance.ElapsedTime : 0f;
        int seconds   = Mathf.FloorToInt(elapsed - _missionStartElapsed);
        if (seconds == _progress) return;
        _progress = Mathf.Clamp(seconds, 0, _current.target);
        RefreshLabel();
        if (_progress >= _current.target) CompleteCurrent();
    }

    // --- Event handlers ---------------------------------------------------

    void OnEnemyKilled(EnemyBase enemy)
    {
        if (_current == null || enemy == null) return;
        if (GameManager.Instance != null && GameManager.Instance.State != GameState.Playing) return;

        bool match = false;
        switch (_current.type)
        {
            case MissionType.KillSoldiers: match = enemy.enemyType == EnemyType.Soldier;  break;
            case MissionType.KillArchers:  match = enemy.enemyType == EnemyType.Archer;   break;
            case MissionType.KillAny:      match = true;                                  break;
        }
        if (!match) return;

        _progress++;
        RefreshLabel();
        if (_progress >= _current.target) CompleteCurrent();
    }

    void OnBuildingDefeated(Destructible d)
    {
        if (_current == null || d == null) return;
        if (GameManager.Instance != null && GameManager.Instance.State != GameState.Playing) return;

        bool match = false;
        switch (_current.type)
        {
            case MissionType.DestroyTowers:    match = d.BuildingKind == Destructible.Kind.Tower; break;
            case MissionType.DestroyBuildings: match = d.BuildingKind == Destructible.Kind.Tower
                                                    || d.BuildingKind == Destructible.Kind.House;  break;
        }
        if (!match) return;

        _progress++;
        RefreshLabel();
        if (_progress >= _current.target) CompleteCurrent();
    }

    // --- Mission lifecycle -------------------------------------------------

    void CompleteCurrent()
    {
        var done = _current;
        _completedThisRun++;
        _bloodEarnedThisRun += done.bloodReward;

        // Phase 10: record for backend validation on /firebase/runs/submit. We
        // map the in-Unity MissionType to the backend's mission "type" string
        // so server-side ``validate_mission_summary`` can recompute the goal
        // against the run's authoritative stats. The wallet IS updated locally
        // for the top-bar HUD but the FINAL credit comes from the server when
        // the run is submitted (offline / guest runs do NOT survive this).
        var stats = RunStats.Instance;
        if (stats != null)
        {
            stats.RecordCompletedMission(
                id: System.Guid.NewGuid().ToString(),
                type: MissionTypeToServerString(done.type),
                target: done.target,
                bloodReward: done.bloodReward);
        }

        AwardBloodLocalDisplayOnly(done.bloodReward);
        Debug.LogFormat("[Mission] Completed {0} +{1} Blood ({2}/{3} this run)",
            FormatTitle(done), done.bloodReward, _completedThisRun, MaxCompletedPerRun);
        ShowCompletionFlash($"Mission complete!  +{done.bloodReward} Blood");
        PickNextMission();
    }

    void PickNextMission()
    {
        // Phase 10: once we've hit the per-run Blood cap or completed the
        // count cap, stop offering new missions so the player isn't shown
        // missions they can complete but won't be rewarded for.
        if (_completedThisRun >= MaxCompletedPerRun || _bloodEarnedThisRun >= MaxMissionBloodPerRun)
        {
            _current = null;
            if (_missionLabel != null)
                _missionLabel.text = "Max mission rewards reached for this run.";
            return;
        }

        if (_missions == null || _missions.Length == 0) { _current = null; RefreshLabel(); return; }

        // Avoid picking the same mission twice in a row when there's choice.
        var prev = _current;
        for (int tries = 0; tries < 8; tries++)
        {
            _current = _missions[Random.Range(0, _missions.Length)];
            if (_missions.Length == 1 || _current != prev) break;
        }

        _progress = 0;
        if (_current.type == MissionType.SurviveSeconds)
            _missionStartElapsed = WaveManager.Instance != null ? WaveManager.Instance.ElapsedTime : 0f;

        RefreshLabel();
    }

    /// <summary>
    /// Phase 10: Blood UI nudge for HUD-only feedback. The AUTHORITATIVE Blood
    /// credit happens server-side when GameOverManager submits the run with
    /// the full mission list. Guests / offline runs never become real Blood —
    /// the next /auth/firebase/me refresh resets the wallet from the server.
    /// </summary>
    void AwardBloodLocalDisplayOnly(int amount)
    {
        if (amount <= 0) return;
        // Only nudge the local cache when the player is logged in via Firebase.
        // For guests we deliberately do nothing so they can't see fake Blood.
        if (!AuthSession.IsFirebaseLoggedIn) return;
        AuthSession.BloodCoins = AuthSession.BloodCoins + amount;
    }

    static string MissionTypeToServerString(MissionType t)
    {
        switch (t)
        {
            case MissionType.KillSoldiers:     return "kill_total";
            case MissionType.KillArchers:      return "kill_total";
            case MissionType.KillAny:          return "kill_total";
            case MissionType.DestroyTowers:    return "destroy_buildings";
            case MissionType.DestroyBuildings: return "destroy_buildings";
            case MissionType.SurviveSeconds:   return "survive_seconds";
        }
        return "kill_total";
    }

    // --- UI ----------------------------------------------------------------

    void BuildUI()
    {
        if (_missionLabel != null) return;  // Init can run twice on scene reload

        // If the editor builder pre-populated the mission slot in GameScreenHub, reuse it
        // so designers can re-skin the row from the Hierarchy.
        var hub = GameScreenHub.Instance != null ? GameScreenHub.Instance : FindObjectOfType<GameScreenHub>(includeInactive: true);
        if (hub != null && hub.MissionLabel != null)
        {
            _missionLabel = hub.MissionLabel;
            _missionLabel.text = "";

            // Build the completion-flash label as a sibling of the mission panel slot so
            // designers see it in the Hierarchy. Anchored bottom-stretch of the mission
            // panel so it sits just under the mission text wherever the panel is moved.
            if (_completionLabel == null)
            {
                var panel = hub.TopRightMissionPanel;
                if (panel != null)
                {
                    var existing = panel.Find("CompletionLabel");
                    GameObject compGo;
                    if (existing != null) compGo = existing.gameObject;
                    else
                    {
                        compGo = new GameObject("CompletionLabel", typeof(UnityEngine.RectTransform));
                        compGo.transform.SetParent(panel, false);
                    }
                    var compRt = (UnityEngine.RectTransform)compGo.transform;
                    compRt.anchorMin = new Vector2(0f, 0f);
                    compRt.anchorMax = new Vector2(1f, 0f);
                    compRt.pivot     = new Vector2(0.5f, 0f);
                    compRt.sizeDelta = new Vector2(0f, 60f);
                    compRt.anchoredPosition = new Vector2(0f, -10f);
                    _completionLabel = compGo.GetComponent<TMPro.TextMeshProUGUI>();
                    if (_completionLabel == null) _completionLabel = compGo.AddComponent<TMPro.TextMeshProUGUI>();
                    _completionLabel.fontSize  = _completionFontSize;
                    _completionLabel.alignment = TMPro.TextAlignmentOptions.Right;
                    _completionLabel.color     = new Color(0.55f, 0.95f, 0.55f, 0f);
                    _completionLabel.raycastTarget = false;
                }
            }
            return;
        }

        Transform root = ResolveHudRoot();

        _missionLabel = HUDUIFactory.BuildLabel(
            root, "Mission",
            offset: _uiOffset,
            align:  TextAlignmentOptions.TopRight,
            fontSize: _titleFontSize);
        var rt = _missionLabel.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(440f, 100f);
        _missionLabel.color = new Color(0.95f, 0.92f, 0.55f, 1f);

        _completionLabel = HUDUIFactory.BuildLabel(
            root, "",
            offset: new Vector2(_uiOffset.x, _uiOffset.y - 110f),
            align:  TextAlignmentOptions.TopRight,
            fontSize: _completionFontSize);
        var crt = _completionLabel.GetComponent<RectTransform>();
        crt.sizeDelta = new Vector2(440f, 60f);
        _completionLabel.color = new Color(0.55f, 0.95f, 0.55f, 0f);  // start invisible
    }

    /// <summary>Prefer attaching to the existing GameCanvas/HUD canvas; otherwise build our
    /// own ScreenSpaceOverlay canvas so the mission UI works in any scene wiring.</summary>
    Transform ResolveHudRoot()
    {
        var canvas = GameObject.Find("GameCanvas");
        if (canvas != null)
        {
            var hud = canvas.transform.Find("HUD");
            return hud != null ? hud : canvas.transform;
        }

        var canvasGO = new GameObject("MissionCanvas");
        canvasGO.transform.SetParent(transform, false);
        var c = canvasGO.AddComponent<Canvas>();
        c.renderMode   = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = 80;
        var scaler = canvasGO.AddComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode         = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.matchWidthOrHeight  = 0.5f;
        canvasGO.AddComponent<UnityEngine.UI.GraphicRaycaster>();
        return canvasGO.transform;
    }

    void RefreshLabel()
    {
        if (_missionLabel == null) return;
        if (_current == null) { _missionLabel.text = ""; return; }
        string title = FormatTitle(_current);
        _missionLabel.text = string.Format("{0}\n{1} / {2}   +{3} Blood",
            title, _progress, _current.target, _current.bloodReward);
    }

    void ShowCompletionFlash(string msg)
    {
        if (_completionLabel == null) return;
        _completionLabel.text = msg;
        if (_completionFlashRoutine != null) StopCoroutine(_completionFlashRoutine);
        _completionFlashRoutine = StartCoroutine(FlashRoutine());
    }

    IEnumerator FlashRoutine()
    {
        var c = _completionLabel.color;
        // Instant pop in
        c.a = 1f;
        _completionLabel.color = c;
        // Hold, then fade
        float t = 0f;
        while (t < _completionFlashDuration)
        {
            t += Time.unscaledDeltaTime;
            float k = 1f - Mathf.Clamp01((t - _completionFlashDuration * 0.5f) / (_completionFlashDuration * 0.5f));
            c.a = k;
            _completionLabel.color = c;
            yield return null;
        }
        c.a = 0f;
        _completionLabel.color = c;
        _completionFlashRoutine = null;
    }

    static string FormatTitle(MissionDefinition m)
    {
        switch (m.type)
        {
            case MissionType.KillSoldiers:     return "Kill Soldiers";
            case MissionType.KillArchers:      return "Kill Archers";
            case MissionType.KillAny:          return "Slay Enemies";
            case MissionType.DestroyTowers:    return "Destroy Towers";
            case MissionType.DestroyBuildings: return "Destroy Buildings";
            case MissionType.SurviveSeconds:   return "Survive";
        }
        return "Mission";
    }

    void BuildDefaultMissionPool()
    {
        _missions = new[]
        {
            new MissionDefinition { type = MissionType.KillSoldiers,     target = 40,  bloodReward = 15 },
            new MissionDefinition { type = MissionType.KillSoldiers,     target = 150, bloodReward = 30 },
            new MissionDefinition { type = MissionType.KillArchers,      target = 120, bloodReward = 25 },
            new MissionDefinition { type = MissionType.KillArchers,      target = 200, bloodReward = 40 },
            new MissionDefinition { type = MissionType.KillAny,          target = 200, bloodReward = 40 },
            new MissionDefinition { type = MissionType.DestroyTowers,    target = 3,   bloodReward = 30 },
            new MissionDefinition { type = MissionType.DestroyBuildings, target = 10,  bloodReward = 20 },
            new MissionDefinition { type = MissionType.SurviveSeconds,   target = 360, bloodReward = 40 },
            new MissionDefinition { type = MissionType.SurviveSeconds,   target = 600, bloodReward = 50 },
        };
    }
}
