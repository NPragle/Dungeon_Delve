// ============================================================
//  AbilityBase.cs
//  Abstract ScriptableObject that every ability inherits from.
//
//  Design decisions:
//  • Data lives in ScriptableObjects — no code changes for new abilities
//  • Cooldowns and GCD are tracked per-instance via AbilityCooldownState
//    (so multiple casters share the SO but have independent cooldowns)
//  • Talent modifiers are injected at runtime; the SO itself is never
//    mutated (important: SOs are shared assets, not per-character)
//  • CanCast() is a pure predicate — never has side effects
//  • Execute() is the authoritative cast path — always called server-side
//    in multiplayer; clients play predicted VFX via OnClientCastStarted()
// ============================================================

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ShatteredVaults
{
    public abstract class AbilityBase : ScriptableObject
    {
        // ── Identity ─────────────────────────────────────────
        [Header("Identity")]
        public string          AbilityName     = "Unnamed Ability";
        public string          Description     = "";
        public Sprite          Icon;
        public int             AbilitySlot;    // 0–9 hotbar slot

        // ── Cast properties ───────────────────────────────────
        [Header("Cast")]
        public AbilityCastType CastType        = AbilityCastType.Instant;
        public float           CastTime        = 0f;    // seconds (Cast type only)
        public float           BaseCooldown    = 1.5f;
        public bool            TriggerGCD      = true;
        public AbilityTargetType TargetType    = AbilityTargetType.SingleEnemy;
        public float           Range           = 30f;   // max target range in metres

        // ── Resource cost ─────────────────────────────────────
        [Header("Resource")]
        public ResourceType    ResourceType    = ResourceType.Mana;
        public float           ResourceCost    = 10f;

        // ── Damage / healing coefficients ─────────────────────
        [Header("Scaling")]
        public StatType        ScalingStat     = StatType.Intelligence;
        public float           Coefficient     = 0.85f; // fraction of ScalingStat
        public float           FlatBonus       = 0f;
        public DamageType      DamageType      = DamageType.Arcane;
        public bool            CanCrit         = true;
        public bool            CanMiss         = true;

        // ── Per-instance runtime state ────────────────────────
        // Stored separately so the shared SO is never mutated.
        // AbilitySystem keeps one AbilityCooldownState per caster per ability.

        // ── Talent modifier injection ─────────────────────────
        // Talents add AbilityModifiers here at load time.
        [System.NonSerialized]
        public List<AbilityModifier> RuntimeModifiers = new();

        // ─────────────────────────────────────────────────────
        //  Core overridable methods
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Pure predicate — can this caster currently use this ability?
        /// Never has side effects. Called every frame for UI greying.
        /// </summary>
        public virtual CastBlockReason CanCast(CharacterEntity caster, AbilityCooldownState state,
                                                CharacterEntity target = null)
        {
            if (caster.IsDead)                          return CastBlockReason.Dead;
            if (state.IsOnCooldown)                     return CastBlockReason.OnCooldown;
            if (state.GCDRemaining > 0f)                return CastBlockReason.GCD;
            if (caster.StatusEffects.IsSilenced
                && CastType != AbilityCastType.Instant) return CastBlockReason.Silenced;
            if (caster.StatusEffects.IsStunned)         return CastBlockReason.Stunned;
            if (!HasSufficientResource(caster))         return CastBlockReason.NoResource;
            if (!IsTargetValid(caster, target))         return CastBlockReason.InvalidTarget;
            if (!IsInRange(caster, target))             return CastBlockReason.OutOfRange;

            return CastBlockReason.None;
        }

        /// <summary>
        /// Execute the ability — all effects, damage, healing, status.
        /// Called after cast-time completes (instant = immediately).
        /// This is the authoritative path. Override in each ability subclass.
        /// </summary>
        public abstract void Execute(CharacterEntity caster, CharacterEntity target,
                                     Vector3 targetPosition);

        /// <summary>
        /// Optional: called when the cast begins (for cast-bar abilities).
        /// Use for channel start FX, animations, etc.
        /// </summary>
        public virtual void OnCastStarted(CharacterEntity caster, CharacterEntity target) { }

        /// <summary>
        /// Optional: called on the client for predicted visuals.
        /// NEVER apply game-state here — visuals only.
        /// </summary>
        public virtual void OnClientCastStarted(CharacterEntity caster, CharacterEntity target) { }

        /// <summary>
        /// Called if the cast is interrupted before completion.
        /// </summary>
        public virtual void OnCastInterrupted(CharacterEntity caster) { }

        // ─────────────────────────────────────────────────────
        //  Shared helpers for subclasses
        // ─────────────────────────────────────────────────────

        /// <summary>Compute scaled base damage/healing for this ability.</summary>
        protected float ComputeScaledValue(CharacterEntity caster)
        {
            float value = DamageCalculator.ScaledAbilityDamage(caster, ScalingStat, Coefficient, FlatBonus);

            // Apply any runtime modifiers that change the base value
            foreach (var mod in RuntimeModifiers)
                if (mod.ModTarget == AbilityModTarget.BaseDamage)
                    value = mod.Apply(value);

            return value;
        }

        /// <summary>Apply a damage hit from this ability to a target.</summary>
        protected DamageResult ApplyDamage(CharacterEntity caster, CharacterEntity target,
                                            float baseAmount, bool isDoT = false)
        {
            var request = new DamageRequest
            {
                Source      = caster,
                Target      = target,
                BaseAmount  = baseAmount,
                DamageType  = DamageType,
                AbilityName = AbilityName,
                IsDoT       = isDoT,
                CanCrit     = CanCrit,
                CanMiss     = CanMiss,
            };

            var result = DamageCalculator.CalculateDamage(request);

            if (result.HitResult != HitResult.Miss && result.HitResult != HitResult.Dodge)
            {
                float actual = target.Resources.TakeDamage(result.FinalDamage, DamageType, caster,
                                                           AbilityName, isDoT);
                CombatEvents.DamageDealt(result.ToInfo(caster, target, AbilityName, isDoT));

                // Generate threat on the tank's threat table
                GenerateThreat(caster, target, actual);
            }

            return result;
        }

        /// <summary>Apply healing from this ability to a target.</summary>
        protected HealResult ApplyHealing(CharacterEntity caster, CharacterEntity target,
                                           float baseAmount, bool isHoT = false)
        {
            var request = new HealRequest
            {
                Source      = caster,
                Target      = target,
                BaseAmount  = baseAmount,
                AbilityName = AbilityName,
                IsHoT       = isHoT,
                CanCrit     = CanCrit,
            };

            var result = DamageCalculator.CalculateHealing(request);
            float actual = target.Resources.ReceiveHealing(result.FinalHealing, caster, AbilityName, isHoT);
            CombatEvents.HealingDealt(result.ToInfo(caster, target, AbilityName, isHoT));

            // Healing generates some threat for the healer on all enemies targeting the healed player
            GenerateHealerThreat(caster, actual);

            return result;
        }

        /// <summary>Apply a status effect to a target.</summary>
        protected void ApplyStatus(CharacterEntity caster, CharacterEntity target,
                                    ActiveStatusEffect effect)
        {
            target.StatusEffects.Apply(effect);
        }

        // ─────────────────────────────────────────────────────
        //  Validation helpers
        // ─────────────────────────────────────────────────────

        protected bool HasSufficientResource(CharacterEntity caster)
        {
            return caster.Resources.CurrentResource >= ResourceCost;
        }

        protected bool IsTargetValid(CharacterEntity caster, CharacterEntity target)
        {
            if (TargetType == AbilityTargetType.Self) return true;
            if (TargetType == AbilityTargetType.GroundTarget) return true;

            bool needsEnemy = TargetType is AbilityTargetType.SingleEnemy
                                        or AbilityTargetType.AoEEnemy
                                        or AbilityTargetType.Cone
                                        or AbilityTargetType.Line;

            bool needsAlly  = TargetType is AbilityTargetType.SingleAlly
                                        or AbilityTargetType.AoEAlly;

            if (target == null) return TargetType == AbilityTargetType.AoEEnemy
                                     || TargetType == AbilityTargetType.AoEAll;

            if (needsEnemy) return target.IsValidTarget(caster) && target.IsPlayer != caster.IsPlayer;
            if (needsAlly)  return target.IsPlayer == caster.IsPlayer && !target.IsDead;

            return true;
        }

        protected bool IsInRange(CharacterEntity caster, CharacterEntity target)
        {
            if (target == null) return true;
            if (TargetType == AbilityTargetType.Self) return true;
            float dist = Vector3.Distance(caster.transform.position, target.transform.position);
            return dist <= Range;
        }

        // ─────────────────────────────────────────────────────
        //  Threat helpers
        // ─────────────────────────────────────────────────────

        private static void GenerateThreat(CharacterEntity source, CharacterEntity target, float damageDealt)
        {
            if (!target.IsPlayer && target.ThreatTable != null)
            {
                float threatMult = source.Stats.Get(StatType.ThreatMultiplier);
                float threat = damageDealt * (1f + threatMult);
                target.ThreatTable.AddThreat(source, threat);
                CombatEvents.ThreatGenerated(new ThreatInfo
                {
                    Source = source, Target = target, Amount = threat, Reason = AbilityName
                });
            }
        }

        private static void GenerateHealerThreat(CharacterEntity healer, float healingDone)
        {
            // Healing generates 50% of its value as threat split across all enemies in combat
            // ThreatSystem handles the split; we just broadcast the event
            CombatEvents.ThreatGenerated(new ThreatInfo
            {
                Source = healer, Target = null, Amount = healingDone * 0.5f, Reason = "Healing"
            });
        }
    }

    // ── Supporting types ─────────────────────────────────────

    public enum CastBlockReason
    {
        None,
        Dead,
        OnCooldown,
        GCD,
        Silenced,
        Stunned,
        NoResource,
        InvalidTarget,
        OutOfRange,
        Casting,
    }

    public enum AbilityModTarget
    {
        BaseDamage,
        Cooldown,
        ResourceCost,
        Duration,
        Range,
        Coefficient,
    }

    [System.Serializable]
    public class AbilityModifier
    {
        public string          ModId;
        public AbilityModTarget ModTarget;
        public ModifierType    Type;
        public float           Value;

        public float Apply(float baseValue)
        {
            return Type switch
            {
                ModifierType.Flat        => baseValue + Value,
                ModifierType.PercentAdd  => baseValue * (1f + Value),
                ModifierType.PercentMult => baseValue * (1f + Value),
                _                        => baseValue,
            };
        }
    }

    /// <summary>
    /// Per-caster, per-ability runtime state.
    /// Held by AbilitySystem — never stored on the SO itself.
    /// </summary>
    public class AbilityCooldownState
    {
        public float CooldownRemaining;
        public float GCDRemaining;
        public bool  IsCasting;
        public float CastProgress;     // 0–1

        public bool IsOnCooldown => CooldownRemaining > 0f;

        public void Tick(float dt)
        {
            CooldownRemaining = Mathf.Max(0f, CooldownRemaining - dt);
            GCDRemaining      = Mathf.Max(0f, GCDRemaining - dt);
        }

        public void StartCooldown(float duration) => CooldownRemaining = duration;
        public void StartGCD(float duration)      => GCDRemaining      = duration;
    }
}
