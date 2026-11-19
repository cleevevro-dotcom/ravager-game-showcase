using UnityEngine;

/// <summary>
/// Editor / testing override manager for the player's selected beast. Sits as a visible
/// GameObject in the Hierarchy ([CharacterManager]) so the developer can flip the force
/// toggle and pick a beast directly in the Inspector before pressing Play.
///
/// Priority order at run start (resolved by <see cref="ResolveEffectiveBeast"/>):
///   1. CharacterManager forced override   — only when _forceSelectedBeast == true
///   2. BeastManager normal selection      — backed by shop / profile / PlayerPrefs
///   3. Default beast                       — Goblin
///
/// IMPORTANT — this is a LOCAL TESTING ONLY override:
///   - The forced beast is NEVER persisted to PlayerPrefs.
///   - The forced beast is NEVER pushed to the backend (no profile/owned changes).
///   - Disabling _forceSelectedBeast restores the normal shop/profile selection unchanged.
/// </summary>
[DefaultExecutionOrder(-95)] // earlier than BeastManager (-90) so the override is observable when BeastManager.Awake runs
public class CharacterManager : MonoBehaviour
{
    public static CharacterManager Instance { get; private set; }

    [Header("Force override (editor / testing)")]
    [Tooltip("If true, the run starts with _forcedBeastType regardless of what the shop/profile/PlayerPrefs say. Local-only — does NOT modify the player's saved selection or backend state.")]
    [SerializeField] private bool      _forceSelectedBeast;
    [Tooltip("Beast to force when _forceSelectedBeast is true. Picked from the dropdown in the Inspector.")]
    [SerializeField] private BeastType _forcedBeastType = BeastType.Goblin;

    /// <summary>True when the developer has flipped the override on for this Play session.</summary>
    public bool      ForceEnabled => _forceSelectedBeast;
    /// <summary>Beast type the developer has chosen to force. Only meaningful when ForceEnabled.</summary>
    public BeastType ForcedBeast  => _forcedBeastType;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// Returns the beast that should actually drive the run, applying the priority order.
    /// Safe to call before BeastManager.Instance is set — falls through to default Goblin.
    ///
    /// Phase 6 priority order (cleaned up):
    ///   1. CharacterManager force override (editor testing only).
    ///   2. Firebase profile selected_character_id (online or cached offline).
    ///   3. BeastManager inspector / PlayerPrefs selection.
    ///   4. Default Goblin.
    /// </summary>
    public static BeastType ResolveEffectiveBeast(BeastManager mgr)
    {
        // 1. Force override wins for local testing only — never persisted.
        if (Instance != null && Instance.ForceEnabled) return Instance.ForcedBeast;

        // 2. Firebase-cached selection. AuthSession.SelectedCharacterIdStr falls
        //    back to "goblin" when no profile is loaded, so this is safe even
        //    for guest / offline runs.
        if (AuthSession.IsFirebaseLoggedIn || !string.IsNullOrEmpty(AuthSession.Uid))
        {
            if (BeastManager.TryParseBeastId(AuthSession.SelectedCharacterIdStr, out var fbBeast))
                return fbBeast;
        }

        // 3. Inspector / legacy PlayerPrefs selection.
        if (mgr != null) return mgr.Selected;

        // 4. Default.
        return BeastType.Goblin;
    }
}
