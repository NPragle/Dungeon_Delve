// ============================================================
//  Enums.cs
//  Central home for every enum in the combat system.
//  Add new values here; never scatter enums across files.
// ============================================================

namespace ShatteredVaults
{
    // ── Stats ────────────────────────────────────────────────
    public enum StatType
    {
        Strength,
        Agility,
        Intelligence,
        Vitality,
        CriticalStrike,
        Haste,
        Mastery,

        // Derived — never set directly on gear; calculated at runtime
        MaxHealth,
        HealthRegen,
        MaxResource,
        ResourceRegen,
        Armor,
        DodgeChance,
        CritDamageMultiplier,
        GlobalCooldownReduction,   // fraction, 0–0.5 hard cap
        CooldownReduction,         // fraction, 0–0.5 hard cap
        DamageDealtMultiplier,
        HealingDealtMultiplier,
        DamageTakenMultiplier,
        ThreatMultiplier,
    }

    // ── Modifier stacking ────────────────────────────────────
    public enum ModifierType
    {
        Flat,           // base + flat
        PercentAdd,     // additive % bonus, stacks with other PercentAdd
        PercentMult,    // multiplicative, applied after PercentAdd pool
    }

    public enum ModifierSource
    {
        Gear,
        Talent,
        Buff,
        Debuff,
        Aura,
        SetBonus,
    }

    // ── Damage & healing ─────────────────────────────────────
    public enum DamageType
    {
        Physical,
        Fire,
        Frost,
        Lightning,
        Void,
        Nature,
        Arcane,
        True,       // bypasses all mitigation
    }

    public enum HitResult
    {
        Normal,
        Critical,
        Glancing,   // reserved for future mechanic
        Miss,
        Dodge,
        Immune,
    }

    // ── Resources ────────────────────────────────────────────
    public enum ResourceType
    {
        Mana,       // Lifebinder, Stormcaller, Hexblade, Runekeeper
        Energy,     // Ashenhand
        Rage,       // Ironclad
        Focus,      // Thornwarden
        Flux,       // Runekeeper alternate
    }

    // ── Ability targeting ────────────────────────────────────
    public enum AbilityTargetType
    {
        Self,
        SingleEnemy,
        SingleAlly,
        AoEEnemy,
        AoEAlly,
        AoEAll,
        Cone,
        Line,
        GroundTarget,   // player places a reticle
    }

    public enum AbilityCastType
    {
        Instant,
        Channelled,
        Cast,           // has a cast bar
    }

    // ── Crowd-control and status ─────────────────────────────
    public enum StatusEffectType
    {
        // Buffs
        Haste,
        Shielded,
        Empowered,
        Invisible,

        // Debuffs / CC
        Stunned,
        Silenced,
        Rooted,
        Slowed,
        Feared,
        Interrupted,

        // DoT / HoT
        Burning,
        Bleeding,
        Poisoned,
        Regenerating,
        Shielding,      // absorb HoT

        // Mechanics
        Taunted,
        Marked,         // boss targeting mechanic
        Cursed,
    }

    // ── Combat events ────────────────────────────────────────
    public enum CombatEventType
    {
        DamageDealt,
        HealingDealt,
        AbilityCast,
        AbilityHit,
        StatusApplied,
        StatusRemoved,
        ResourceChanged,
        CharacterDied,
        CharacterRevived,
        ThreatGenerated,
    }

    // ── Character role ───────────────────────────────────────
    public enum CharacterRole
    {
        Tank,
        Healer,
        MeleeDPS,
        RangedDPS,
        Support,
    }

    // ── Class identifier ─────────────────────────────────────
    public enum ClassType
    {
        Ironclad,
        Lifebinder,
        Ashenhand,
        Stormcaller,
        Thornwarden,
        Hexblade,
        Runekeeper,
    }
}
