// ============================================================
//  StatSheet.cs
//  Attached to every CharacterEntity (players AND enemies).
//
//  Architecture notes:
//  • Three-layer modifier stack: Flat → PercentAdd pool → PercentMult
//  • Thread-safe dirty flag: stats only recalculate when something changed
//  • No Unity UI coupling — fire OnStatsChanged and let UI subscribe
//  • All values clamped per-stat; soft diminishing returns on Haste/CDR
//  • Multiplayer-ready: authority lives on CharacterEntity; this class
//    is pure calculation with no RPC knowledge
// ============================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShatteredVaults
{
    [Serializable]
    public class StatModifier
    {
        public StatType     Stat;
        public ModifierType Type;
        public float        Value;
        public ModifierSource Source;
        public string       SourceId;   // e.g. "item_frostwhisper_gauntlets"

        public StatModifier(StatType stat, ModifierType type, float value,
                            ModifierSource source, string sourceId = "")
        {
            Stat     = stat;
            Type     = type;
            Value    = value;
            Source   = source;
            SourceId = sourceId;
        }
    }

    public class StatSheet : MonoBehaviour
    {
        // ── Events ───────────────────────────────────────────
        /// <summary>Fired whenever any final stat value changes.</summary>
        public event Action OnStatsChanged;

        // ── Base stats (set by ClassDefinitionSO at spawn) ───
        [Header("Base Values (set by class definition)")]
        [SerializeField] private SerializableStatMap baseStats = new();

        // ── Modifier lists ───────────────────────────────────
        private readonly List<StatModifier> _modifiers = new();

        // ── Cached finals ────────────────────────────────────
        private readonly Dictionary<StatType, float> _finalCache = new();
        private bool _dirty = true;

        // ── Hard caps ────────────────────────────────────────
        private static readonly Dictionary<StatType, float> Caps = new()
        {
            { StatType.CriticalStrike,         0.75f },   // 75% crit
            { StatType.DodgeChance,            0.40f },
            { StatType.GlobalCooldownReduction, 0.50f },
            { StatType.CooldownReduction,       0.50f },
            { StatType.DamageTakenMultiplier,   0.10f },  // can't reduce below 10% dmg taken
        };

        // ── Soft DR breakpoints for Haste & CDR ─────────────
        // Rating → effective % uses a diminishing-returns curve above the knee
        private const float HasteKnee   = 0.30f;   // DR kicks in above 30%
        private const float HasteMax    = 0.50f;   // hard cap

        // ─────────────────────────────────────────────────────
        //  Public API
        // ─────────────────────────────────────────────────────

        /// <summary>Set a base stat value (called by ClassDefinitionSO at spawn).</summary>
        public void SetBase(StatType stat, float value)
        {
            baseStats[stat] = value;
            MarkDirty();
        }

        /// <summary>Batch-set multiple base stats from a dictionary.</summary>
        public void SetBaseBatch(Dictionary<StatType, float> values)
        {
            foreach (var kvp in values)
                baseStats[kvp.Key] = kvp.Value;
            MarkDirty();
        }

        /// <summary>Add a modifier from any source (gear, buff, talent, set bonus).</summary>
        public void AddModifier(StatModifier modifier)
        {
            _modifiers.Add(modifier);
            MarkDirty();
        }

        /// <summary>Remove all modifiers matching the given source ID.</summary>
        public void RemoveModifiersFromSource(string sourceId)
        {
            int removed = _modifiers.RemoveAll(m => m.SourceId == sourceId);
            if (removed > 0) MarkDirty();
        }

        /// <summary>Remove all modifiers of a given ModifierSource category (e.g. all Buffs).</summary>
        public void RemoveModifiersByCategory(ModifierSource category)
        {
            int removed = _modifiers.RemoveAll(m => m.Source == category);
            if (removed > 0) MarkDirty();
        }

        /// <summary>Get the final calculated value for a stat.</summary>
        public float Get(StatType stat)
        {
            if (_dirty) RecalculateAll();
            return _finalCache.TryGetValue(stat, out float val) ? val : 0f;
        }

        /// <summary>Force recalculation on next Get() call.</summary>
        public void MarkDirty()
        {
            _dirty = true;
        }

        // ─────────────────────────────────────────────────────
        //  Core Calculation
        // ─────────────────────────────────────────────────────

        private void RecalculateAll()
        {
            _finalCache.Clear();

            // Step 1: seed cache with base stats
            foreach (StatType stat in Enum.GetValues(typeof(StatType)))
                _finalCache[stat] = baseStats.TryGetValue(stat, out float b) ? b : 0f;

            // Step 2: apply Flat modifiers
            foreach (var mod in _modifiers)
                if (mod.Type == ModifierType.Flat)
                    _finalCache[mod.Stat] += mod.Value;

            // Step 3: sum PercentAdd pool per stat, then apply once
            var percentAddPool = new Dictionary<StatType, float>();
            foreach (var mod in _modifiers)
                if (mod.Type == ModifierType.PercentAdd)
                {
                    if (!percentAddPool.ContainsKey(mod.Stat))
                        percentAddPool[mod.Stat] = 0f;
                    percentAddPool[mod.Stat] += mod.Value;
                }
            foreach (var kvp in percentAddPool)
                _finalCache[kvp.Key] *= (1f + kvp.Value);

            // Step 4: apply PercentMult modifiers (each one is multiplicative with others)
            foreach (var mod in _modifiers)
                if (mod.Type == ModifierType.PercentMult)
                    _finalCache[mod.Stat] *= (1f + mod.Value);

            // Step 5: derive secondary stats from primaries
            DerivePrimaryStats();

            // Step 6: apply diminishing returns to Haste / CDR
            _finalCache[StatType.Haste] = ApplyHasteDR(_finalCache[StatType.Haste]);
            _finalCache[StatType.GlobalCooldownReduction] =
                ApplyHasteDR(_finalCache[StatType.GlobalCooldownReduction]);
            _finalCache[StatType.CooldownReduction] =
                ApplyHasteDR(_finalCache[StatType.CooldownReduction]);

            // Step 7: clamp all stats to caps
            foreach (var cap in Caps)
            {
                if (_finalCache.TryGetValue(cap.Key, out float v))
                    _finalCache[cap.Key] = Mathf.Min(v, cap.Value);
            }

            // Always positive
            foreach (StatType stat in Enum.GetValues(typeof(StatType)))
                if (_finalCache.TryGetValue(stat, out float v))
                    _finalCache[stat] = Mathf.Max(v, 0f);

            _dirty = false;
            OnStatsChanged?.Invoke();
        }

        /// <summary>
        /// Convert primary stats into derived stats.
        /// Formulae are intentionally tunable here in one place.
        /// </summary>
        private void DerivePrimaryStats()
        {
            // Health from Vitality
            float vitality    = _finalCache[StatType.Vitality];
            float baseHP      = _finalCache.TryGetValue(StatType.MaxHealth, out float bHP) ? bHP : 0f;
            _finalCache[StatType.MaxHealth] = baseHP + (vitality * 10f);

            // Armor from Strength (partial)
            float strength = _finalCache[StatType.Strength];
            _finalCache[StatType.Armor] += strength * 0.5f;

            // Dodge from Agility
            float agility = _finalCache[StatType.Agility];
            _finalCache[StatType.DodgeChance] += agility * 0.002f;  // 1% per 5 agi

            // Crit damage multiplier baseline + bonus from CriticalStrike
            float crit = _finalCache[StatType.CriticalStrike];
            _finalCache[StatType.CritDamageMultiplier] =
                _finalCache.TryGetValue(StatType.CritDamageMultiplier, out float cm) ? cm : 0f;
            _finalCache[StatType.CritDamageMultiplier] = 1.5f + (crit * 0.5f); // 150% baseline, bonus from crit rating

            // GCD reduction from Haste (Haste stat is a 0–1 fraction)
            float haste = _finalCache[StatType.Haste];
            _finalCache[StatType.GlobalCooldownReduction] = haste;
        }

        /// <summary>
        /// Soft diminishing returns curve for Haste / CDR.
        /// Linear up to knee, then logarithmic approach to hard max.
        /// </summary>
        private static float ApplyHasteDR(float rawFraction)
        {
            if (rawFraction <= HasteKnee)
                return rawFraction;

            float over    = rawFraction - HasteKnee;
            float remain  = HasteMax - HasteKnee;
            float drValue = remain * (1f - Mathf.Exp(-over / remain));
            return HasteKnee + drValue;
        }

        // ─────────────────────────────────────────────────────
        //  Debug
        // ─────────────────────────────────────────────────────

#if UNITY_EDITOR
        [ContextMenu("Log All Stats")]
        public void DebugLogAllStats()
        {
            if (_dirty) RecalculateAll();
            foreach (StatType stat in Enum.GetValues(typeof(StatType)))
                Debug.Log($"[StatSheet] {stat}: {Get(stat):F2}");
        }
#endif
    }

    // ── Serialisable dictionary wrapper (Unity inspector) ────
    [Serializable]
    public class SerializableStatMap : Dictionary<StatType, float>, ISerializationCallbackReceiver
    {
        [SerializeField] private List<StatType>  keys   = new();
        [SerializeField] private List<float>     values = new();

        public void OnBeforeSerialize()
        {
            keys.Clear(); values.Clear();
            foreach (var kvp in this) { keys.Add(kvp.Key); values.Add(kvp.Value); }
        }

        public void OnAfterDeserialize()
        {
            Clear();
            for (int i = 0; i < Mathf.Min(keys.Count, values.Count); i++)
                this[keys[i]] = values[i];
        }
    }
}
