using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// General Dorado — boss controller. Genshin-style attack rotation:
///   1. Spin Dash       — telegraph, then charges fast in a rotating AoE for ~3s.
///   2. Long Slash      — telegraph a line; slash fires along that line.
///   3. Playful Pursuit — orbits the player at distance, then closes for a normal slash.
///   4. Soldier Circle  — telegraphs, then summons a ring of pooled soldiers around the player
///                        to pressure positioning. Soldiers chase + attack the player normally
///                        (they bypass the encounter's retreat-orbit because they're boss summons).
///   5. Pursue          — falls back to soldier-like chase when on cooldown.
///
/// Drives its own state machine inside MoveTowardPlayer so the EnemyBase.Tick still runs status
/// effects + knockback. Boss is immune to Fear-stun (fearResistance = 1).
///
/// Phase cooldowns multiply by BossEncounter.ActiveBossCooldownMul, which composes the player-level
/// milestone cooldown with the per-spawn-index reduction (boss #5 attacks faster than boss #1).
/// The cooldown floor is enforced by BossEncounter so values can't reach zero — pressure, not
/// instant unavoidable damage.
/// </summary>
public class BossGeneralDorado : EnemyBase
{
    enum Phase : byte
    {
        Pursue,
        TelegraphSpin,    ExecuteSpin,
        TelegraphSlash,   ExecuteSlash,
        Playful,
        TelegraphCircle,  ExecuteCircle,
        Recover,
    }

    // Tuning constants — easy to revise as we balance.
    const float SpinTelegraph    = 1.0f;
    const float SpinExecute      = 3.0f;
    const float SpinSpeedMul     = 2f;
    const float SpinAoeRadius    = 2.4f;
    const float SpinTickInterval = 0.18f;
    const float SpinTickDamage   = 8f;

    const float SlashTelegraph   = 1.1f;
    const float SlashExecute     = 0.55f;
    const float SlashLength      = 8.5f;
    const float SlashHalfWidth   = 0.9f;
    const float SlashDamage      = 30f;

    const float PlayfulDuration  = 4.0f;
    const float PlayfulOrbitRadius = 5.5f;
    const float PlayfulSpeedMul  = 1.6f;

    const float SpinCooldown    = 8.5f;
    const float SlashCooldown   = 10f;
    const float PlayfulCooldown = 14f;
    const float PlayfulMinRange = 3.5f;

    [Header("Soldier Circle Attack")]
    [Tooltip("Number of soldiers summoned per circle attack. Will be limited by EnemyManager._maxActiveEnemies.")]
    [SerializeField] int   _circleSoldierCount   = 8;
    [Tooltip("Distance from the player at which soldiers appear.")]
    [SerializeField] float _circleRadius         = 7f;
    [Tooltip("Telegraph duration before soldiers appear.")]
    [SerializeField] float _circleTelegraphTime  = 1.0f;
    [Tooltip("Cooldown between soldier-circle attacks.")]
    [SerializeField] float _circleCooldown       = 18f;
    [Tooltip("Minimum elapsed phase time (seconds) the boss spends in Recover after the circle goes off.")]
    [SerializeField] float _circleRecoverTime    = 0.6f;

    Phase _phase = Phase.Pursue;
    float _phaseTimer;

    float _nextSpinTime;
    float _nextSlashTime;
    float _nextPlayfulTime;
    float _nextCircleTime;

    Vector2 _slashAnchor;
    Vector2 _slashDir;
    bool    _slashHitDealt;
    float   _spinTickAcc;
    bool    _spinHitDealtThisStrike;
    Vector2 _playfulCenter;
    Vector2 _circleCenter;
    bool    _circleSpawned;

    SpriteRenderer _telegraphSR;
    GameObject     _telegraphGO;
    SpriteRenderer _hpFillSR;
    SpriteRenderer _hpBgSR;
    Transform      _hpRoot;
    GameObject     _nameGO;
    TMPro.TextMeshPro _nameLabel;

    readonly List<EnemyBase> _aoeBuffer = new List<EnemyBase>(8); // unused by boss but kept for future AoE on enemies
    static readonly int HashAttackId = Animator.StringToHash("Attack");

    // Boss telegraphs and attack timings rely on full-speed Animator regardless of camera position.
    public override bool IsLodExempt => true;

    /// <summary>Live cooldown multiplier for THIS boss instance — composes player-level milestone
    /// scaling, per-spawn-index reduction, AND the every-120s time-step reduction. Read every time
    /// we set a phase cooldown so the internal Spin/Slash/Playful/Circle cycles also speed up as
    /// the run goes on.</summary>
    float CdMul => BossEncounter.Instance != null ? BossEncounter.Instance.ActiveBossCooldownMul : 1f;

    /// <summary>Live outgoing-damage multiplier — grows every 120s of run time so phase damage
    /// (spin tick, slash strike, contact) gets stronger over time. HP is intentionally not scaled
    /// here; only damage the boss DEALS.</summary>
    float DmgMul => BossEncounter.Instance != null ? BossEncounter.Instance.ActiveBossDamageMul : 1f;

    protected override void Awake()
    {
        base.Awake();
        enemyType        = EnemyType.Knight;     // re-use the reserved Knight slot for the boss
        maxHp            = 2500f;
        moveSpeed        = 2f;
        damage           = 22f;                  // fallback contact damage
        attackRange      = 1.6f;
        attackCooldown   = 1.5f;
        xpValue          = 200f;
        separationRadius = 2f;                    // boss never gets shoved by other enemies
        obstacleRadius   = 0.6f;
        fearResistance   = 1f;                    // immune to fear-stun
    }

    public override void Init(Transform player, PlayerStats stats, SpatialHashManager spatialMgr, EnemyManager mgr)
    {
        base.Init(player, stats, spatialMgr, mgr);
        _phase           = Phase.Pursue;
        _phaseTimer      = 0f;
        // Grace windows — boss does NOT attack the moment the intro ends. CdMul scales them too so
        // a late-spawn boss starts pressuring sooner.
        float cd         = CdMul;
        _nextSpinTime    = Time.time + 6f  * cd;
        _nextSlashTime   = Time.time + 4f  * cd;
        _nextPlayfulTime = Time.time + 9f  * cd;
        _nextCircleTime  = Time.time + 7f  * cd;
        _circleSpawned   = false;
        EnsureTelegraph();
        EnsureNameAndHpBar();
    }

    void EnsureTelegraph()
    {
        if (_telegraphGO != null) return;
        _telegraphGO = new GameObject("BossTelegraph");
        _telegraphGO.transform.SetParent(transform, false);
        _telegraphGO.transform.localPosition = Vector3.zero;
        _telegraphSR = _telegraphGO.AddComponent<SpriteRenderer>();
        _telegraphSR.sprite       = PlaceholderSprites.GetSquare(32);
        _telegraphSR.color        = new Color(1f, 0.2f, 0.2f, 0.45f);
        _telegraphSR.sortingOrder = 31000;
        _telegraphGO.SetActive(false);
    }

    void EnsureNameAndHpBar()
    {
        if (_hpRoot != null) return;
        _hpRoot = new GameObject("BossHpRoot").transform;
        _hpRoot.SetParent(transform, false);
        // Boss is scaled 3× — counter that on the HUD root so the bar reads at world-screen size.
        _hpRoot.localScale = new Vector3(1f / 3f, 1f / 3f, 1f);
        // localPosition in BOSS-local space. The boss is 3×-scaled, so this Y is multiplied
        // by 3 when computing world position. 0.7 boss-local → 2.1 world units above the
        // sprite's center pivot, which lands just above the visible head crown.
        _hpRoot.localPosition = new Vector3(0f, 0.7f, 0f);

        var bgGO = new GameObject("HpBg");
        bgGO.transform.SetParent(_hpRoot, false);
        // Smaller, tighter bar — 3.5 world units wide, 0.4 tall (was 6 × 0.35). Reads
        // clearly above the head without dominating the screen.
        bgGO.transform.localScale = new Vector3(3.5f, 0.4f, 1f);
        _hpBgSR = bgGO.AddComponent<SpriteRenderer>();
        _hpBgSR.sprite       = PlaceholderSprites.GetSquare(32);
        _hpBgSR.color        = new Color(0.1f, 0.05f, 0.05f, 0.85f);
        _hpBgSR.sortingOrder = 31200;

        var fillGO = new GameObject("HpFill");
        fillGO.transform.SetParent(bgGO.transform, false);
        fillGO.transform.localScale = new Vector3(0.98f, 0.7f, 1f);
        _hpFillSR = fillGO.AddComponent<SpriteRenderer>();
        _hpFillSR.sprite       = PlaceholderSprites.GetSquare(32);
        _hpFillSR.color        = new Color(0.95f, 0.15f, 0.20f, 1f);
        _hpFillSR.sortingOrder = 31201;

        _nameGO = new GameObject("BossName");
        _nameGO.transform.SetParent(_hpRoot, false);
        // Name sits just above the bar — smaller font and tighter offset since the bar
        // itself moved closer to the head.
        _nameGO.transform.localPosition = new Vector3(0f, 0.40f, 0f);
        _nameLabel = _nameGO.AddComponent<TMPro.TextMeshPro>();
        _nameLabel.text     = "General Dorad";
        _nameLabel.fontSize = 2.5f;
        _nameLabel.alignment = TMPro.TextAlignmentOptions.Center;
        _nameLabel.color = new Color(1f, 0.9f, 0.7f, 1f);
        _nameLabel.sortingOrder = 31202;
    }

    protected override void LateUpdate()
    {
        base.LateUpdate(); // Y-sort the boss sprite like every other enemy.

        // Update HP bar fill so the bar's left edge stays anchored even though the parent sprite
        // is centered. Scale x by remaining %, then offset so it shrinks from right to left.
        if (_hpFillSR != null)
        {
            float pct = maxHp > 0f ? Mathf.Clamp01(currentHp / maxHp) : 0f;
            var t = _hpFillSR.transform;
            t.localScale = new Vector3(0.98f * pct, 0.7f, 1f);
            t.localPosition = new Vector3(-0.5f * (1f - pct) * 0.98f, 0f, 0f);
        }
    }

    public override void Tick(float dt)
    {
        // BossEncounter intro freeze: stand still while the player is locked.
        if (BossEncounter.Instance != null && BossEncounter.Instance.PlayerLocked)
        {
            SetWalking(false);
            return;
        }
        base.Tick(dt);
    }

    protected override void MoveTowardPlayer(float dt)
    {
        if (playerTransform == null) return;

        _phaseTimer += dt;
        switch (_phase)
        {
            case Phase.Pursue:          TickPursue(dt);          break;
            case Phase.TelegraphSpin:   TickTelegraphSpin(dt);   break;
            case Phase.ExecuteSpin:     TickExecuteSpin(dt);     break;
            case Phase.TelegraphSlash:  TickTelegraphSlash(dt);  break;
            case Phase.ExecuteSlash:    TickExecuteSlash(dt);    break;
            case Phase.Playful:         TickPlayful(dt);         break;
            case Phase.TelegraphCircle: TickTelegraphCircle(dt); break;
            case Phase.ExecuteCircle:   TickExecuteCircle(dt);   break;
            case Phase.Recover:         TickRecover(dt);         break;
        }
    }

    protected override void TryAttack()
    {
        // Boss damage comes from its skill phases — disable the default melee attack.
    }

    // ─── Phases ────────────────────────────────────────────────────────────────

    void TickPursue(float dt)
    {
        Vector2 pos      = Position;
        Vector2 toPlayer = (Vector2)playerTransform.position - pos;
        float   distSqr  = toPlayer.sqrMagnitude;

        // Decide on the next attack — first eligible wins by reverse priority order so
        // playful (most situational) gates first.
        if (Time.time >= _nextSpinTime)
        {
            EnterPhase(Phase.TelegraphSpin);
            return;
        }
        if (Time.time >= _nextSlashTime && distSqr <= SlashLength * SlashLength)
        {
            _slashAnchor = pos;
            _slashDir    = toPlayer.sqrMagnitude > 0.0001f ? toPlayer.normalized : Vector2.right;
            EnterPhase(Phase.TelegraphSlash);
            return;
        }
        if (Time.time >= _nextCircleTime)
        {
            // Lock the circle center to the player at the moment we commit. Telegraph + spawn use it.
            _circleCenter  = playerTransform.position;
            _circleSpawned = false;
            EnterPhase(Phase.TelegraphCircle);
            return;
        }
        if (Time.time >= _nextPlayfulTime && distSqr <= PlayfulMinRange * PlayfulMinRange * 9f)
        {
            _playfulCenter = playerTransform.position;
            EnterPhase(Phase.Playful);
            return;
        }

        Vector2 dir = toPlayer.sqrMagnitude > 0.0001f ? toPlayer.normalized : Vector2.right;
        Vector2 desired = pos + dir * ComputeEffectiveSpeed(pos) * dt;
        Vector2 resolved = ResolveBuildingCollision(desired);
        transform.position = new Vector3(resolved.x, resolved.y, transform.position.z);
        ApplyFacing(dir.x);
        SetWalking(true);
    }

    void TickTelegraphSpin(float dt)
    {
        SetWalking(false);
        SetTelegraphCircle(SpinAoeRadius * 1.05f, new Color(1f, 0.25f, 0.25f, 0.45f));
        if (_phaseTimer >= SpinTelegraph)
        {
            HideTelegraph();
            _spinTickAcc = 0f;
            EnterPhase(Phase.ExecuteSpin);
        }
    }

    void TickExecuteSpin(float dt)
    {
        Vector2 pos      = Position;
        Vector2 toPlayer = (Vector2)playerTransform.position - pos;
        Vector2 dir      = toPlayer.sqrMagnitude > 0.0001f ? toPlayer.normalized : Vector2.right;
        float   speed    = ComputeEffectiveSpeed(pos) * SpinSpeedMul;
        Vector2 desired  = pos + dir * speed * dt;
        Vector2 resolved = ResolveBuildingCollision(desired);
        transform.position = new Vector3(resolved.x, resolved.y, transform.position.z);
        ApplyFacing(dir.x);
        SetWalking(true);

        // Visual rotation. Counter-rotate the HP root so the bar doesn't tumble.
        transform.Rotate(0f, 0f, 720f * dt);
        if (_hpRoot != null) _hpRoot.rotation = Quaternion.identity;

        _spinTickAcc += dt;
        if (_spinTickAcc >= SpinTickInterval)
        {
            _spinTickAcc -= SpinTickInterval;
            float distToPlayerSqr = ((Vector2)playerTransform.position - (Vector2)transform.position).sqrMagnitude;
            if (distToPlayerSqr <= SpinAoeRadius * SpinAoeRadius)
                playerStats?.TakeDamage(SpinTickDamage * DmgMul, Position, 2f);
        }

        if (_phaseTimer >= SpinExecute)
        {
            transform.rotation = Quaternion.identity;
            _nextSpinTime = Time.time + SpinCooldown * CdMul;
            EnterPhase(Phase.Recover);
        }
    }

    void TickTelegraphSlash(float dt)
    {
        SetWalking(false);
        SetTelegraphRect(_slashAnchor, _slashDir, SlashLength, SlashHalfWidth, new Color(1f, 0.2f, 0.2f, 0.45f));
        // Track player slowly during telegraph so dodging is meaningful but possible. Scale tracking
        // speed by the inverse of CdMul so a sped-up boss tracks correspondingly faster.
        Vector2 toPlayer = (Vector2)playerTransform.position - (Vector2)transform.position;
        if (toPlayer.sqrMagnitude > 0.0001f)
        {
            Vector2 want = toPlayer.normalized;
            float trackRate = 1.5f / Mathf.Max(0.01f, CdMul);
            _slashDir = Vector2.Lerp(_slashDir, want, dt * trackRate).normalized;
        }
        if (_phaseTimer >= SlashTelegraph * CdMul)
        {
            _slashHitDealt = false;
            HideTelegraph();
            EnterPhase(Phase.ExecuteSlash);
        }
    }

    void TickExecuteSlash(float dt)
    {
        SetTelegraphRect(_slashAnchor, _slashDir, SlashLength, SlashHalfWidth, new Color(1f, 0.8f, 0.4f, 0.85f));

        if (!_slashHitDealt)
        {
            _slashHitDealt = true;
            anim?.SetTrigger(HashAttackId);
            Vector2 toPlayer = (Vector2)playerTransform.position - _slashAnchor;
            float along = Vector2.Dot(toPlayer, _slashDir);
            if (along >= 0f && along <= SlashLength)
            {
                Vector2 perp = toPlayer - _slashDir * along;
                if (perp.sqrMagnitude <= SlashHalfWidth * SlashHalfWidth)
                    playerStats?.TakeDamage(SlashDamage * DmgMul, _slashAnchor, 2f);
            }
        }

        if (_phaseTimer >= SlashExecute)
        {
            HideTelegraph();
            _nextSlashTime = Time.time + SlashCooldown * CdMul;
            EnterPhase(Phase.Recover);
        }
    }

    void TickPlayful(float dt)
    {
        Vector2 pos    = Position;
        Vector2 center = _playfulCenter = playerTransform.position;
        Vector2 fromCenter = pos - center;
        float   r = fromCenter.magnitude;
        if (r < 0.001f) fromCenter = Vector2.right;
        Vector2 radial = fromCenter.normalized;
        Vector2 tangent = new Vector2(-radial.y, radial.x);
        Vector2 desiredDir = (radial * (PlayfulOrbitRadius - r) * 0.5f + tangent * PlayfulOrbitRadius * 0.4f).normalized;

        float speed = ComputeEffectiveSpeed(pos) * PlayfulSpeedMul;
        Vector2 desired  = pos + desiredDir * speed * dt;
        Vector2 resolved = ResolveBuildingCollision(desired);
        transform.position = new Vector3(resolved.x, resolved.y, transform.position.z);
        ApplyFacing(desiredDir.x);
        SetWalking(true);

        if (_phaseTimer >= PlayfulDuration)
        {
            _slashAnchor = transform.position;
            Vector2 toPlayer = (Vector2)playerTransform.position - _slashAnchor;
            _slashDir = toPlayer.sqrMagnitude > 0.0001f ? toPlayer.normalized : Vector2.right;
            _nextPlayfulTime = Time.time + PlayfulCooldown * CdMul;
            EnterPhase(Phase.TelegraphSlash);
        }
    }

    // ─── Soldier Circle Attack ─────────────────────────────────────────────────
    // Boss anchors the ring at the player's position when entering TelegraphCircle.
    // Telegraph: pulsing red ring drawn at (playerPos, _circleRadius).
    // Execute: spawns _circleSoldierCount pooled soldiers evenly around the ring.
    //   - Soldiers come from EnemyManager.SpawnEnemy(Soldier, pos). If the active-enemy cap is hit,
    //     SpawnEnemy returns null and we just summon fewer this frame — no Instantiate fallback.
    //   - Spawned soldiers get ignoreBossRetreat = true so they CHASE + ATTACK the player normally
    //     instead of orbiting at the encounter retreat ring.

    void TickTelegraphCircle(float dt)
    {
        SetWalking(false);

        // Boss visually anchors at its own position; the telegraph ring is drawn AROUND THE PLAYER
        // at _circleRadius, since that's where soldiers will appear. Re-parent the telegraph to the
        // boss is fine — set its world position each frame.
        SetTelegraphRing(_circleCenter, _circleRadius, new Color(1f, 0.3f, 0.3f, 0.5f));

        if (_phaseTimer >= _circleTelegraphTime * CdMul)
        {
            HideTelegraph();
            EnterPhase(Phase.ExecuteCircle);
        }
    }

    void TickExecuteCircle(float dt)
    {
        if (!_circleSpawned)
        {
            _circleSpawned = true;
            SpawnSoldierRing(_circleCenter, _circleRadius, _circleSoldierCount);
            _nextCircleTime = Time.time + _circleCooldown * CdMul;
        }
        // Brief recovery frame before falling back to Pursue so the boss isn't insta-attacking.
        if (_phaseTimer >= _circleRecoverTime) EnterPhase(Phase.Pursue);
    }

    void SpawnSoldierRing(Vector2 center, float radius, int count)
    {
        if (enemyManager == null || count <= 0) return;
        float twoPi = Mathf.PI * 2f;
        for (int i = 0; i < count; i++)
        {
            float angle = twoPi * (i / (float)count);
            Vector2 pos = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            EnemyBase soldier = enemyManager.SpawnEnemy(EnemyType.Soldier, pos);
            // SpawnEnemy returns null when at the active-enemy cap; that's fine — we summon fewer.
            if (soldier != null) soldier.ignoreBossRetreat = true;
        }
    }

    void TickRecover(float dt)
    {
        SetWalking(false);
        if (_phaseTimer >= 0.5f) EnterPhase(Phase.Pursue);
    }

    void EnterPhase(Phase next)
    {
        _phase      = next;
        _phaseTimer = 0f;
    }

    // ─── Telegraph helpers ─────────────────────────────────────────────────────

    void SetTelegraphCircle(float radius, Color color)
    {
        EnsureTelegraph();
        _telegraphSR.sprite = PlaceholderSprites.GetCircle(64);
        _telegraphSR.color  = color;
        _telegraphGO.transform.SetParent(transform, false);
        _telegraphGO.transform.localPosition = Vector3.zero;
        _telegraphGO.transform.localRotation = Quaternion.identity;
        // Boss is scaled 3× world; telegraph is a child so localScale must compensate.
        float local = (radius * 2f / PlaceholderSprites.GetVisibleDiameterUnits(64)) / 3f;
        _telegraphGO.transform.localScale = new Vector3(local, local, 1f);
        _telegraphGO.SetActive(true);
    }

    /// <summary>Telegraph drawn in WORLD space (not parented to the boss) at a fixed center —
    /// used for the soldier-circle ring so the marker stays put even if the boss moves.</summary>
    void SetTelegraphRing(Vector2 worldCenter, float radius, Color color)
    {
        EnsureTelegraph();
        _telegraphSR.sprite = PlaceholderSprites.GetRing(64, 0.15f);
        _telegraphSR.color  = color;
        _telegraphGO.transform.SetParent(null, true);
        _telegraphGO.transform.position = new Vector3(worldCenter.x, worldCenter.y, transform.position.z);
        _telegraphGO.transform.rotation = Quaternion.identity;
        float worldDiameter = radius * 2f;
        float local         = worldDiameter / PlaceholderSprites.GetVisibleDiameterUnits(64);
        _telegraphGO.transform.localScale = new Vector3(local, local, 1f);
        _telegraphGO.SetActive(true);
    }

    void SetTelegraphRect(Vector2 anchor, Vector2 dir, float length, float halfWidth, Color color)
    {
        EnsureTelegraph();
        _telegraphSR.sprite = PlaceholderSprites.GetSquare(32);
        _telegraphSR.color  = color;
        // Convert anchor (world) to local of this transform (boss). Account for 3× scale.
        Vector2 worldOffset = anchor + dir * (length * 0.5f) - (Vector2)transform.position;
        _telegraphGO.transform.position = transform.position + (Vector3)worldOffset;
        _telegraphGO.transform.SetParent(transform, true); // keep current world pose, re-parent
        float ang = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        _telegraphGO.transform.rotation = Quaternion.Euler(0f, 0f, ang);
        _telegraphGO.transform.localScale = new Vector3(length / 3f, (halfWidth * 2f) / 3f, 1f);
        _telegraphGO.SetActive(true);
    }

    void HideTelegraph()
    {
        if (_telegraphGO != null) _telegraphGO.SetActive(false);
    }

    protected override void Die()
    {
        if (IsDead) return;
        IsDead = true;
        // Notify the encounter FIRST — it reads our Position and xpValue to drop the XP burst from
        // the pool, then advances the schedule. We then tear down our own GameObject.
        if (BossEncounter.Instance != null) BossEncounter.Instance.NotifyBossDied();
        playerStats?.NotifyKilled(this);
        enemyManager?.ReturnEnemy(this);
        Destroy(gameObject);
    }
}
