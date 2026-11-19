using UnityEngine;

public class AgilitySkill : PassiveSkillBase
{
    public override SkillId       Id          => SkillId.Agility;
    public override SkillCategory Category    => SkillCategory.Stat;
    public override string        DisplayName => "Agility";
    public override SkillRarity   Rarity      => SkillRarity.Common;

    const float DashTriggerRange = 5.5f;
    const float DashDuration     = 0.5f;
    const float DashSpeedMul     = 2.0f;

    float _baseMoveSpeed;
    float _appliedMoveAdd;
    float _appliedDamagePerSpeed;
    float _appliedTerrainResist;

    bool  _dashEnabled;
    float _dashEndTime;
    float _lastHp;

    bool  _afterimageEnabled;
    float _afterimageInterval = 1.2f;
    float _nextAfterimageTime;

    GameObject _afterimageRoot;
    DynamicPool<AgilityAfterimage> _afterimagePool;

    public override void Bind(PlayerStats stats, Transform player, SpatialHashManager spatialMgr)
    {
        base.Bind(stats, player, spatialMgr);
        interval       = 0.25f;
        _baseMoveSpeed = stats != null ? stats.moveSpeed : 5f;
        _lastHp        = stats != null ? stats.CurrentHp : 0f;
    }

    protected override void PassiveFire() { /* unused — driven by SkillTick */ }

    public override void SkillTick(float dt)
    {
        if (!Unlocked || playerTransform == null || playerStats == null) return;
        if (GameManager.Instance == null || GameManager.Instance.State != GameState.Playing) return;

        if (_dashEnabled)
        {
            float hp = playerStats.CurrentHp;
            if (hp < _lastHp)
            {
                // Dash trigger semantics: "hit by something further than DashTriggerRange away".
                // The old code searched 999 units (every enemy on the map) to find the closest
                // and then asked if it was past the trigger range — that scanned the entire
                // spatial hash on every damage tick (including burns / auras) and caused a
                // frame skip the player would see as lag.
                //
                // Equivalent and cheap: ask whether ANY enemy is within DashTriggerRange. If
                // none, the nearest must be further (or there's no enemy) — dash.
                Vector2 pPos = playerTransform.position;
                var nearby = TargetingUtility.FindNearestEnemy(pPos, DashTriggerRange);
                if (nearby == null)
                    _dashEndTime = Time.time + DashDuration;
            }
            _lastHp = hp;
        }

        if (_dashEnabled && Time.time < _dashEndTime)
            playerStats.moveSpeed = _baseMoveSpeed + _appliedMoveAdd + (_baseMoveSpeed + _appliedMoveAdd) * (DashSpeedMul - 1f);
        else if (_dashEnabled)
            playerStats.moveSpeed = _baseMoveSpeed + _appliedMoveAdd;

        if (_afterimageEnabled && Time.time >= _nextAfterimageTime)
        {
            _nextAfterimageTime = Time.time + _afterimageInterval;
            DropAfterimage();
        }
    }

    void DropAfterimage()
    {
        if (_afterimagePool == null)
        {
            _afterimageRoot = new GameObject("[AgilityAfterimagePool]");
            _afterimageRoot.transform.SetParent(null);
            _afterimagePool = new DynamicPool<AgilityAfterimage>(CreateAfterimage, 4);
        }
        var clone = _afterimagePool.Get(playerTransform.position, Quaternion.identity);
        clone.Launch(playerTransform.position, 1.5f);
    }

    AgilityAfterimage CreateAfterimage()
    {
        var go = new GameObject("Afterimage");
        go.transform.SetParent(_afterimageRoot.transform);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite       = PlaceholderSprites.GetCircle(48);
        sr.color        = SkillColors.Agility;
        sr.sortingOrder = 25000;
        var ai = go.AddComponent<AgilityAfterimage>();
        ai.Configure(_afterimagePool, sr);
        return ai;
    }

    protected override void OnStacksChanged(int t1, int t2, int t3)
    {
        if (playerStats == null) return;

        // Reverse previously-applied deltas so re-applies are idempotent.
        playerStats.moveSpeed          -= _appliedMoveAdd;
        playerStats.damagePerMoveSpeed -= _appliedDamagePerSpeed;
        playerStats.terrainSlowResist  -= _appliedTerrainResist;

        // T1 — run speed + Light Footing per stack (capped so a player can't go infinite).
        int t1Eff       = Mathf.Min(6, t1);
        float moveAdd   = 0.5f * t1Eff;
        float terrain   = Mathf.Min(0.95f, 0.25f * t1Eff);

        playerStats.moveSpeed         += moveAdd;
        playerStats.terrainSlowResist += terrain;
        _appliedMoveAdd       = moveAdd;
        _appliedTerrainResist = terrain;
        _baseMoveSpeed        = playerStats.moveSpeed - moveAdd;

        // T2 — walk-cast speed + Dash (first stack unlocks).
        playerStats.castMoveSpeedMul = Mathf.Min(1f, 0.7f + 0.15f * t2);
        _dashEnabled = t2 >= 1;

        // T3 — Afterimage decoy + Shadow Agility (move-speed → damage), per stack.
        _afterimageEnabled = t3 >= 1;
        _appliedDamagePerSpeed = Mathf.Min(2f, 0.6f * t3);
        playerStats.damagePerMoveSpeed += _appliedDamagePerSpeed;
    }

    public override string GetTierDescription(int tier) => tier switch
    {
        1 => "T1: +Run speed & Light Footing per stack",
        2 => "T2: Walk-cast speed up + Dash on long-range hit",
        3 => "T3: Afterimage decoy + Shadow Agility (speed → damage)",
        _ => "",
    };
}
