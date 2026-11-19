using UnityEngine;

/// <summary>
/// Visible-in-Hierarchy manager that owns the player's beast selection. The user can switch
/// the dropdown in the Inspector before pressing Play. Selection is persisted to PlayerPrefs
/// so it survives across runs and synced with the backend's selected_character_id when online.
///
/// Spawn this on a top-level GameObject named "[BeastManager]" — it's intentionally NOT
/// HideInInspector so the user can edit it during development.
/// </summary>
[DefaultExecutionOrder(-90)]
public class BeastManager : MonoBehaviour
{
    public static BeastManager Instance { get; private set; }

    const string PrefKey = "ravager.selectedBeast";

    [Header("Selection")]
    [Tooltip("Beast the player will spawn as. Editable in Inspector.")]
    [SerializeField] BeastType _selected = BeastType.Goblin;

    [Tooltip("If true, BeastApplier will reapply the beast's stats every time _selected changes at runtime.")]
    [SerializeField] bool _liveSwap = false;

    [Header("Backend (optional)")]
    [Tooltip("Mirrors backend characters.id for the selected beast — used for run submission and shop.")]
    [SerializeField] int _backendCharacterId = -1;

    BeastType _appliedSelection;

    public BeastType Selected => _selected;
    public int       BackendCharacterId => _backendCharacterId;
    public bool      LiveSwap => _liveSwap;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // Honor any saved beast from a previous session — but only if the user didn't
        // manually pin a value via the inspector. Treat the inspector as authoritative
        // when it's a non-default selection ahead of Awake.
        if (_selected == BeastType.Goblin && PlayerPrefs.HasKey(PrefKey))
        {
            int saved = PlayerPrefs.GetInt(PrefKey, 0);
            if (saved >= 0 && saved < BeastDatabase.Count) _selected = (BeastType)saved;
        }

        _appliedSelection = _selected;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>Programmatic setter — called by UI (character selector) and by login flow.</summary>
    public void Select(BeastType type, int backendId = -1, bool persist = true)
    {
        _selected = type;
        if (backendId >= 0) _backendCharacterId = backendId;
        if (persist) PlayerPrefs.SetInt(PrefKey, (int)type);
        if (_liveSwap && Application.isPlaying) BeastApplier.ApplyToPlayer(this);
        _appliedSelection = type;
    }

    public BeastConfig GetConfig() => BeastDatabase.Get(_selected);

    // --- Firestore string-ID interop (Phase 6) ------------------------------

    /// <summary>Map the Firestore stable character_id ("goblin"/"skeleton"/...) to a BeastType.</summary>
    public static bool TryParseBeastId(string characterId, out BeastType type)
    {
        if (!string.IsNullOrEmpty(characterId))
        {
            switch (characterId.ToLowerInvariant())
            {
                case "goblin":   type = BeastType.Goblin;   return true;
                case "skeleton": type = BeastType.Skeleton; return true;
                case "golem":    type = BeastType.Golem;    return true;
                case "slime":    type = BeastType.Slime;    return true;
                case "zombie":   type = BeastType.Zombie;   return true;
            }
        }
        type = BeastType.Goblin;
        return false;
    }

    /// <summary>Inverse mapping for outgoing /player/firebase/select-character calls.</summary>
    public static string BeastTypeToFirestoreId(BeastType t)
    {
        switch (t)
        {
            case BeastType.Goblin:   return "goblin";
            case BeastType.Skeleton: return "skeleton";
            case BeastType.Golem:    return "golem";
            case BeastType.Slime:    return "slime";
            case BeastType.Zombie:   return "zombie";
        }
        return "goblin";
    }

    /// <summary>Select by Firestore string ID. Updates PlayerPrefs + the
    /// Firebase-shape selected_character_id cache, then optionally posts the
    /// change to the backend so the next /auth/firebase/me reflects it.</summary>
    public void SelectByFirestoreId(string characterId, bool persistToBackend)
    {
        if (!TryParseBeastId(characterId, out var t))
        {
            Debug.LogWarning("[BeastManager] Unknown character_id: " + characterId);
            return;
        }
        Select(t);
        AuthSession.SelectedCharacterIdStr = characterId;

        if (persistToBackend && AuthSession.IsFirebaseLoggedIn)
        {
            // Fire and forget — the local cache is already updated; if the
            // backend rejects (e.g. character not owned) we'll find out on
            // the next profile refresh.
            var runner = PlayerProfileService.Instance;
            if (runner != null)
                runner.StartCoroutine(ShopAPI.SelectCharacterFirebase(characterId, null));
        }
    }

    void Update()
    {
        if (!_liveSwap || !Application.isPlaying) return;
        if (_selected == _appliedSelection) return;
        BeastApplier.ApplyToPlayer(this);
        _appliedSelection = _selected;
    }
}
