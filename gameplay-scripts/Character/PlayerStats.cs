using System;
using UnityEngine;
using UnityEngine.Profiling;

public class PlayerStats : MonoBehaviour
{
    // Profiler markers — visible in Unity Profiler under "Scripts" so the per-hit hot path
    // can be inspected when investigating the single-hit lag bug. Allocated once, no GC.
    static readonly CustomSampler s_TakeDamageSampler = CustomSampler.Create("PlayerStats.TakeDamage");
    static readonly CustomSampler s_HpChangedSampler  = CustomSampler.Create("PlayerStats.OnHpChanged");
    static readonly CustomSampler s_HitFeedbackSampler = CustomSampler.Create("PlayerStats.HitFeedback");
    [Header("Health")]
    public float maxHp = 100f;

    [Header("XP")]
    public float xpToNextLevel = 50f;
    [SerializeField] float _xpScaling = 1.4f;

    [Header("Combat")]
    public float attackDamage = 10f;
    public float attackSpeed  = 1f;
    public float moveSpeed    = 5f;

    [Header("Defense / Bonus")]
    [Range(0f, 1f)] public float knockbackResist = 0f;
    public float lifePerKill      = 0f;
    public float castMoveSpeedMul = 1f;
    public float hpRegenPerSec    = 0f;     // Health T2: regen ticks every second
    public float terrainSlowResist = 0f;    // Agility T1: 0 = full slow, 1 = ignore slow
    public float damagePerMoveSpeed = 0f;   // Agility T3: shadow agility scaling
    public float xpGainMultiplier = 1f;     // Vision: applies on AddXp
    public float xpMagnetRadiusMul = 1f;    // Vision: scales orb attract radius
    public float visionRadius     = 6f;     // Vision: world-unit radius of the visible area inside the Dark Vision overlay
    public float visionMidBrightnessBonus = 0f;  // Vision (optional): subtracts from the overlay's middle-ring darkness; clamped to >=0 darkness

    [Header("Combat Modifiers (set by skills at runtime)")]
    public float outgoingDamageMul = 1f;             // Berserk / Bloodlust frenzy / Soul Harvest buff
    [Range(0f, 0.95f)] public float damageReduction = 0f;  // Iron Skin T1
    public int invulnCount = 0;                      // counter — Berserk T3 emergency + Iron Skin T3 shield can stack
    public bool IsInvulnerable => invulnCount > 0;

    public float CurrentHp  { get; private set; }
    public float CurrentXp  { get; private set; }
    public int   Level      { get; private set; } = 1;

    public event Action<float, float> OnHpChanged;
    public event Action<float, float> OnXpChanged;
    public event Action<int>          OnLevelUp;
    public event Action               OnDeath;
    public event Action<EnemyBase>    OnEnemyKilled;

    void Awake() => CurrentHp = maxHp;

    void Update()
    {
        if (GameManager.Instance == null || GameManager.Instance.State != GameState.Playing) return;
        TickRegen(Time.deltaTime);
    }

    public void TakeDamage(float amount)
    {
        // Overload without a source position — fired by skills/effects that don't have a clear
        // origin (e.g. burn DoT). No knockback/shake feedback in this path.
        TakeDamageInternal(amount, false, Vector2.zero, 1f);
    }

    /// <summary>
    /// Take damage from a known source position. The source is used to push the player away
    /// (knockback) and to trigger a small camera shake via PlayerHitFeedback. intensityMul
    /// scales the shake (pass 2.0 for boss/tower hits).
    /// </summary>
    public void TakeDamage(float amount, Vector2 fromPos, float intensityMul = 1f)
    {
        TakeDamageInternal(amount, true, fromPos, intensityMul);
    }

    void TakeDamageInternal(float amount, bool hasSource, Vector2 fromPos, float intensityMul)
    {
        if (CurrentHp <= 0f) return;
        if (IsInvulnerable) return;
        s_TakeDamageSampler.Begin();
        if (damageReduction > 0f) amount *= 1f - damageReduction;
        CurrentHp = Mathf.Max(0f, CurrentHp - amount);

        s_HpChangedSampler.Begin();
        OnHpChanged?.Invoke(CurrentHp, maxHp);
        s_HpChangedSampler.End();

        // Hit feedback (knockback + throttled camera shake). Fires only when the caller
        // provided a source position — burn ticks and abstract damage skip this.
        if (hasSource && CurrentHp > 0f)
        {
            s_HitFeedbackSampler.Begin();
            PlayerHitFeedback.Instance?.OnHit(fromPos, intensityMul);
            s_HitFeedbackSampler.End();
        }

        if (CurrentHp <= 0f) OnDeath?.Invoke();
        s_TakeDamageSampler.End();
    }

    public void Heal(float amount)
    {
        CurrentHp = Mathf.Min(maxHp, CurrentHp + amount);
        OnHpChanged?.Invoke(CurrentHp, maxHp);
    }

    public void AddXp(float amount)
    {
        if (CurrentHp <= 0f) return;
        CurrentXp += amount * xpGainMultiplier;
        while (CurrentXp >= xpToNextLevel)
        {
            CurrentXp     -= xpToNextLevel;
            xpToNextLevel *= _xpScaling;
            Level++;
            OnLevelUp?.Invoke(Level);
        }
        OnXpChanged?.Invoke(CurrentXp, xpToNextLevel);
    }

    public void TickRegen(float dt)
    {
        if (CurrentHp <= 0f || hpRegenPerSec <= 0f) return;
        if (CurrentHp >= maxHp) return;
        Heal(hpRegenPerSec * dt);
    }

    public void RefreshStatsUI()
    {
        CurrentHp = Mathf.Min(CurrentHp, maxHp);
        OnHpChanged?.Invoke(CurrentHp, maxHp);
        OnXpChanged?.Invoke(CurrentXp, xpToNextLevel);
    }

    public void NotifyKilled(EnemyBase enemy)
    {
        if (lifePerKill > 0f) Heal(lifePerKill);
        OnEnemyKilled?.Invoke(enemy);
    }
}
