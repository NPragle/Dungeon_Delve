// ============================================================
//  StatusEffectHandler.cs
//  Manages all timed status effects: buffs, debuffs, DoTs,
//  HoTs, CC (stun, silence, root, slow), and absorb shields.
//
//  Design:
//  • Effects are data structs, not GameObjects — no spawning overhead
//  • Stacking: most effects refresh duration by default;
//    stackable effects (e.g. Bleed, Ignite) accumulate instances
//  • All ticks fire CombatEvents so the net layer can see them
//  • CC effects set flags on CharacterEntity for AbilitySystem to query
// ============================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShatteredVaults
{
    [Serializable]
    public class ActiveStatusEffect
    {
        public StatusEffectType EffectType;
        public CharacterEntity  Source;
        public float            RemainingDuration;
        public float            TotalDuration;
        public float            TickInterval;       // for DoT/HoT; 0 = no tick
        public float            TickTimer;
        public float            TickValue;          // damage or healing per tick
        public DamageType       DamageType;         // for DoTs
        public float            Magnitude;          // for slows (0–1 fraction), shields, etc.
        public bool             IsStackable;        // if true, multiple instances coexist
        public string           EffectId;           // e.g. "ignite" or "bleed_ashenhand"
        public int              StackCount;
        public StatModifier     StatMod;            // optional stat change while active

        public bool IsExpired => RemainingDuration <= 0f;
    }

    [RequireComponent(typeof(CharacterEntity))]
    public class StatusEffectHandler : MonoBehaviour
    {
        // ── State ────────────────────────────────────────────
        private CharacterEntity _owner;
        private readonly List<ActiveStatusEffect> _effects = new();
        private readonly List<ActiveStatusEffect> _toRemove = new();

        // ── CC flags (queried by AbilitySystem) ──────────────
        public bool IsStunned   { get; private set; }
        public bool IsSilenced  { get; private set; }
        public bool IsRooted    { get; private set; }
        public bool IsSlowed    { get; private set; }
        public float SlowAmount { get; private set; }  // 0–1 fraction

        // ── Events ───────────────────────────────────────────
        public event Action<ActiveStatusEffect> OnEffectApplied;
        public event Action<ActiveStatusEffect> OnEffectRemoved;
        public event Action<ActiveStatusEffect, float> OnEffectTicked; // (effect, tickValue)

        // ── Read-only view for UI ─────────────────────────────
        public IReadOnlyList<ActiveStatusEffect> ActiveEffects => _effects;

        // ─────────────────────────────────────────────────────
        //  Unity lifecycle
        // ─────────────────────────────────────────────────────

        private void Awake()
        {
            _owner = GetComponent<CharacterEntity>();
        }

        private void Update()
        {
            if (_owner.IsDead) return;

            float dt = Time.deltaTime;
            _toRemove.Clear();

            foreach (var effect in _effects)
            {
                effect.RemainingDuration -= dt;

                // Tick DoT / HoT
                if (effect.TickInterval > 0f)
                {
                    effect.TickTimer -= dt;
                    if (effect.TickTimer <= 0f)
                    {
                        effect.TickTimer += effect.TickInterval;
                        ProcessTick(effect);
                    }
                }

                if (effect.IsExpired)
                    _toRemove.Add(effect);
            }

            foreach (var effect in _toRemove)
                InternalRemove(effect);
        }

        // ─────────────────────────────────────────────────────
        //  Public API
        // ─────────────────────────────────────────────────────

        /// <summary>Apply a status effect.  Handles refresh and stacking rules.</summary>
        public void Apply(ActiveStatusEffect effect)
        {
            if (_owner.IsDead) return;

            // Some CC effects are blocked by immunity (TODO: check IsImmune)

            if (effect.IsStackable)
            {
                // Each stack is a separate instance
                _effects.Add(effect);
            }
            else
            {
                // Refresh existing or add new
                var existing = _effects.Find(e => e.EffectId == effect.EffectId);
                if (existing != null)
                {
                    existing.RemainingDuration = effect.TotalDuration;
                    existing.TotalDuration     = effect.TotalDuration;
                    existing.Source            = effect.Source;
                    return; // don't fire Apply event again
                }
                _effects.Add(effect);
            }

            // Apply stat modifier if present
            if (effect.StatMod != null)
                _owner.Stats.AddModifier(effect.StatMod);

            UpdateCCFlags();

            OnEffectApplied?.Invoke(effect);
            CombatEvents.StatusChanged(new StatusEventInfo
            {
                Source     = effect.Source,
                Target     = _owner,
                EffectType = effect.EffectType,
                Duration   = effect.TotalDuration,
                Applied    = true,
            });
        }

        /// <summary>Remove all instances of the given effect type.</summary>
        public void RemoveAll(StatusEffectType type)
        {
            _effects.RemoveAll(e =>
            {
                if (e.EffectType != type) return false;
                CleanupEffect(e);
                return true;
            });
            UpdateCCFlags();
        }

        /// <summary>Cleanse all debuffs (e.g. Lifebinder Cleanse ability).</summary>
        public void CleansAllDebuffs()
        {
            _effects.RemoveAll(e =>
            {
                if (IsDebuff(e.EffectType)) { CleanupEffect(e); return true; }
                return false;
            });
            UpdateCCFlags();
        }

        /// <summary>Remove all effects (e.g. on death).</summary>
        public void ClearAll()
        {
            foreach (var e in _effects) CleanupEffect(e);
            _effects.Clear();
            UpdateCCFlags();
        }

        public bool Has(StatusEffectType type) =>
            _effects.Exists(e => e.EffectType == type);

        public int StackCount(string effectId) =>
            _effects.FindAll(e => e.EffectId == effectId).Count;

        // ─────────────────────────────────────────────────────
        //  Internal
        // ─────────────────────────────────────────────────────

        private void ProcessTick(ActiveStatusEffect effect)
        {
            float value = effect.TickValue;

            if (IsHoT(effect.EffectType))
            {
                float actual = _owner.Resources.ReceiveHealing(value, effect.Source, effect.EffectId, isHoT: true);
                CombatEvents.HealingDealt(new HealInfo
                {
                    Source      = effect.Source,
                    Target      = _owner,
                    RawAmount   = value,
                    FinalAmount = actual,
                    IsHoT       = true,
                    AbilityName = effect.EffectId,
                });
            }
            else if (IsDoT(effect.EffectType))
            {
                float actual = _owner.Resources.TakeDamage(value, effect.DamageType, effect.Source,
                                                           effect.EffectId, isDoT: true);
                CombatEvents.DamageDealt(new DamageInfo
                {
                    Source      = effect.Source,
                    Target      = _owner,
                    RawAmount   = value,
                    FinalAmount = actual,
                    DamageType  = effect.DamageType,
                    HitResult   = HitResult.Normal,
                    IsDoT       = true,
                    AbilityName = effect.EffectId,
                });
            }

            OnEffectTicked?.Invoke(effect, value);
        }

        private void InternalRemove(ActiveStatusEffect effect)
        {
            _effects.Remove(effect);
            CleanupEffect(effect);
            UpdateCCFlags();
            OnEffectRemoved?.Invoke(effect);
            CombatEvents.StatusChanged(new StatusEventInfo
            {
                Target     = _owner,
                EffectType = effect.EffectType,
                Applied    = false,
            });
        }

        private void CleanupEffect(ActiveStatusEffect effect)
        {
            if (effect.StatMod != null)
                _owner.Stats.RemoveModifiersFromSource(effect.EffectId);
        }

        private void UpdateCCFlags()
        {
            IsStunned  = Has(StatusEffectType.Stunned);
            IsSilenced = Has(StatusEffectType.Silenced);
            IsRooted   = Has(StatusEffectType.Rooted);

            var slowEffect = _effects.Find(e => e.EffectType == StatusEffectType.Slowed);
            IsSlowed  = slowEffect != null;
            SlowAmount = slowEffect?.Magnitude ?? 0f;
        }

        private static bool IsDoT(StatusEffectType t) =>
            t is StatusEffectType.Burning or StatusEffectType.Bleeding or StatusEffectType.Poisoned;

        private static bool IsHoT(StatusEffectType t) =>
            t is StatusEffectType.Regenerating;

        private static bool IsDebuff(StatusEffectType t) =>
            t is StatusEffectType.Stunned or StatusEffectType.Silenced or StatusEffectType.Rooted
              or StatusEffectType.Slowed  or StatusEffectType.Feared  or StatusEffectType.Cursed
              or StatusEffectType.Burning or StatusEffectType.Bleeding or StatusEffectType.Poisoned
              or StatusEffectType.Marked;
    }
}
