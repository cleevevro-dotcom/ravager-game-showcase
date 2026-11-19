using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-40)]
public class SkillManager : MonoBehaviour
{
    public static SkillManager Instance { get; private set; }

    readonly Dictionary<SkillId, SkillBase> _bySkillId = new Dictionary<SkillId, SkillBase>();
    readonly List<SkillBase>                _all       = new List<SkillBase>(16);

    public SkillInventory Inventory { get; } = new SkillInventory();

    public PlayerStats        PlayerStats { get; private set; }
    public Transform          PlayerXform { get; private set; }
    public SpatialHashManager Spatial     { get; private set; }

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void Init(PlayerStats stats, Transform playerXform, SpatialHashManager spatial)
    {
        PlayerStats = stats;
        PlayerXform = playerXform;
        Spatial     = spatial;

        EnsureAllSkillsAttached();

        _all.Clear();
        _bySkillId.Clear();
        GetComponents(_all);
        for (int i = 0; i < _all.Count; i++)
        {
            var s = _all[i];
            s.Bind(stats, playerXform, spatial);
            _bySkillId[s.Id] = s;
        }
    }

    /// <summary>
    /// Phase 7: re-call Bind on every skill so subscriptions to
    /// IPlayerAttack events re-attach to the now-active beast component.
    /// Called by <see cref="BeastApplier.EnableForBeast"/> after a beast swap.
    ///
    /// Linked-skill Bind methods are idempotent (they unsubscribe first), so
    /// calling this repeatedly is safe.
    /// </summary>
    public void RebindAllSkills()
    {
        if (PlayerStats == null || PlayerXform == null) return;
        for (int i = 0; i < _all.Count; i++)
        {
            var s = _all[i];
            if (s == null) continue;
            s.Bind(PlayerStats, PlayerXform, Spatial);
        }
    }

    void EnsureAllSkillsAttached()
    {
        EnsureOne<FireSkill>();
        EnsureOne<FreezeSkill>();
        EnsureOne<LightningSkill>();
        EnsureOne<PlagueSkill>();
        EnsureOne<TentacleSkill>();
        EnsureOne<AxeSkill>();
        EnsureOne<SwordSkill>();
        EnsureOne<PhysicalSkill>();
        EnsureOne<AgilitySkill>();
        EnsureOne<HealthSkill>();
        EnsureOne<FearSkill>();
        EnsureOne<VisionSkill>();
        // New beast skills.
        EnsureOne<EarthshatterSkill>();
        EnsureOne<WindCutterSkill>();
        EnsureOne<SpiritOrbSkill>();
        EnsureOne<BerserkSkill>();
        EnsureOne<IronSkinSkill>();
        EnsureOne<BloodlustSkill>();
        EnsureOne<ChaosSurgeSkill>();
        EnsureOne<SoulHarvestSkill>();
    }

    void EnsureOne<T>() where T : SkillBase
    {
        if (GetComponent<T>() == null) gameObject.AddComponent<T>();
    }

    public SkillBase Get(SkillId id) => _bySkillId.TryGetValue(id, out var s) ? s : null;

    public int           GetLevel(SkillId id)        => Inventory.GetLevel(id);
    public string        GetDisplayName(SkillId id)  => _bySkillId.TryGetValue(id, out var s) ? s.DisplayName : id.ToString();
    public SkillCategory GetCategory(SkillId id)     => _bySkillId.TryGetValue(id, out var s) ? s.Category : SkillCategory.Stat;
    public SkillRarity   GetRarity(SkillId id)       => _bySkillId.TryGetValue(id, out var s) ? s.Rarity : SkillRarity.Common;

    public int GetTierStacks(SkillId id, int tier)   => Inventory.GetTierStacks(id, tier);
    public int GetTotalStacks(SkillId id)            => Inventory.GetTotalStacks(id);

    public string GetTierDescription(SkillId id, int tier)
        => _bySkillId.TryGetValue(id, out var s) ? s.GetTierDescription(tier) : "";

    /// <summary>T1 always pickable; T2 needs ≥1 T1; T3 needs ≥1 T2.</summary>
    public bool CanPickTier(SkillId id, int tier) => tier switch
    {
        1 => true,
        2 => Inventory.GetTierStacks(id, 1) >= 1,
        3 => Inventory.GetTierStacks(id, 2) >= 1,
        _ => false,
    };

    public void AddStack(SkillId id, int tier)
    {
        if (!CanPickTier(id, tier)) return;
        Inventory.AddTierStack(id, tier);
        if (_bySkillId.TryGetValue(id, out var skill))
        {
            skill.ApplyStacks(
                Inventory.GetTierStacks(id, 1),
                Inventory.GetTierStacks(id, 2),
                Inventory.GetTierStacks(id, 3));
        }
    }

    public IReadOnlyList<SkillBase> AllSkills => _all;

    void Update()
    {
        if (GameManager.Instance == null || GameManager.Instance.State != GameState.Playing) return;
        float dt = Time.deltaTime;
        for (int i = 0; i < _all.Count; i++) _all[i].SkillTick(dt);
    }
}
