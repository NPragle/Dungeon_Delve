// ============================================================
//  DamageCalculator.cs
//  Pure static class — all damage and healing math lives here.
//  No MonoBehaviour, no state, no side effects.
//
//  Calling convention:
//      var result = DamageCalculator.CalculateDamage(request);
//      target.Resources.TakeDamage(result.FinalDamage, ...);
//      CombatEvents.DamageDealt(result.ToInfo(source, target));
//
//  All tuning constants are centralised here for easy balancing.
//  In a live game, move them to a BalanceSO ScriptableObject.
// ============================================================

using UnityEngine;

namespace ShatteredVaults
{
    // ── Input / output structs ────────────────────────────────

    public struct DamageRequest
    {
        public CharacterEntity  Source;
        public CharacterEntity  Target;
        public float            BaseAmount;     // raw number from ability coefficient
        public DamageType       DamageType;
        public string           AbilityName;
        public bool             IsDoT;
        public bool             CanCrit;
        public bool             CanMiss;
        public float            CoeffOverride;  // 0 = use stat-based coeff
    }

    public struct DamageResult
    {
        public float        FinalDamage;
        public HitResult    HitResult;
        public float        ArmorMitigation;
        public float        ResistMitigation;
        public bool         WasCritical;

        public DamageInfo ToInfo(CharacterEntity source, CharacterEntity target, string abilityName, bool isDoT)
        {
            return new DamageInfo
            {
                Source      = source,
                Target      = target,
                RawAmount   = FinalDamage / Mathf.Max(0.01f, 1f - ArmorMitigation - ResistMitigation),
                FinalAmount = FinalDamage,
                HitResult   = HitResult,
                IsDoT       = isDoT,
                AbilityName = abilityName,
            };
        }
    }

    public struct HealRequest
    {
        public CharacterEntity  Source;
        public CharacterEntity  Target;
        public float            BaseAmount;
        public string           AbilityName;
        public bool             IsHoT;
        public bool             CanCrit;
    }

    public struct HealResult
    {
        public float     FinalHealing;
        public HitResult HitResult;
        public bool      WasCritical;

        public HealInfo ToInfo(CharacterEntity source, CharacterEntity target, string abilityName, bool isHoT)
        {
            return new HealInfo
            {
                Source      = source,
                Target      = target,
                RawAmount   = FinalHealing,
                FinalAmount = FinalHealing,
                HitResult   = HitResult,
                IsHoT       = isHoT,
                AbilityName = abilityName,
            };
        }
    }

    // ── Calculator ───────────────────────────────────────────

    public static class DamageCalculator
    {
        // ── Tuning constants ─────────────────────────────────
        private const float BaseCritMultiplier      = 1.5f;    // overridden by CritDamageMultiplier stat
        private const float ArmorConstant           = 1500f;   // WoW-style armor damage reduction formula
        private const float BaseHitChance           = 0.96f;   // 4% miss baseline
        private const float DodgeBaseChance         = 0.05f;   // before agility
        private const float TrueDamageMitigation    = 0f;      // True bypasses everything
        private const float HealCritMultiplier      = 1.5f;

        // Per damage type: resistance fraction baseline (0 = fully mitigated by armor formula)
        // Non-zero means the damage type has its own resist roll independent of armor
        private static readonly System.Collections.Generic.Dictionary<DamageType, float> BaseResists = new()
        {
            { DamageType.Physical,  0f   },
            { DamageType.Fire,      0f   },
            { DamageType.Frost,     0f   },
            { DamageType.Lightning, 0f   },
            { DamageType.Void,      0f   },
            { DamageType.Nature,    0f   },
            { DamageType.Arcane,    0f   },
            { DamageType.True,      0f   },  // always 0 — bypasses everything
        };

        // ─────────────────────────────────────────────────────
        //  Damage
        // ─────────────────────────────────────────────────────

        public static DamageResult CalculateDamage(DamageRequest req)
        {
            var result = new DamageResult();

            // ── Miss / dodge check ────────────────────────────
            if (req.CanMiss)
            {
                float hitChance   = BaseHitChance;
                float dodgeChance = req.Target != null
                    ? req.Target.Stats.Get(StatType.DodgeChance)
                    : DodgeBaseChance;

                float roll = Random.value;
                if (roll > hitChance)
                {
                    result.HitResult    = HitResult.Miss;
                    result.FinalDamage  = 0f;
                    return result;
                }
                if (roll < dodgeChance && req.DamageType == DamageType.Physical)
                {
                    result.HitResult    = HitResult.Dodge;
                    result.FinalDamage  = 0f;
                    return result;
                }
            }

            float damage = req.BaseAmount;

            // ── Crit roll ─────────────────────────────────────
            result.HitResult = HitResult.Normal;
            if (req.CanCrit && req.Source != null)
            {
                float critChance = req.Source.Stats.Get(StatType.CriticalStrike);
                if (Random.value < critChance)
                {
                    float critMult  = req.Source.Stats.Get(StatType.CritDamageMultiplier);
                    if (critMult <= 0f) critMult = BaseCritMultiplier;
                    damage         *= critMult;
                    result.HitResult = HitResult.Critical;
                    result.WasCritical = true;
                }
            }

            // ── Source damage dealt multiplier ────────────────
            if (req.Source != null)
            {
                float mult = req.Source.Stats.Get(StatType.DamageDealtMultiplier);
                if (mult > 0f) damage *= (1f + mult);
            }

            // ── Target damage taken multiplier ────────────────
            float damageTakenMult = 1f;
            if (req.Target != null)
            {
                float taken = req.Target.Stats.Get(StatType.DamageTakenMultiplier);
                damageTakenMult = Mathf.Max(0.1f, taken > 0f ? taken : 1f);
                damage *= damageTakenMult;
            }

            // ── Armor mitigation (physical & magical separately)
            if (req.DamageType != DamageType.True && req.Target != null)
            {
                float armor        = req.Target.Stats.Get(StatType.Armor);
                float armorFactor  = ArmorMitigationFraction(armor);
                result.ArmorMitigation = armorFactor;

                // Physical takes full armor; magical takes 40% of armor value
                float effectiveMit = req.DamageType == DamageType.Physical
                    ? armorFactor
                    : armorFactor * 0.4f;

                damage *= (1f - effectiveMit);
            }

            result.FinalDamage = Mathf.Max(1f, damage);    // minimum 1 damage always
            return result;
        }

        // ─────────────────────────────────────────────────────
        //  Healing
        // ─────────────────────────────────────────────────────

        public static HealResult CalculateHealing(HealRequest req)
        {
            var result = new HealResult();
            float healing = req.BaseAmount;

            // Crit
            result.HitResult = HitResult.Normal;
            if (req.CanCrit && req.Source != null)
            {
                float critChance = req.Source.Stats.Get(StatType.CriticalStrike);
                if (Random.value < critChance)
                {
                    healing            *= HealCritMultiplier;
                    result.WasCritical  = true;
                    result.HitResult    = HitResult.Critical;
                }
            }

            // Healer's healing dealt multiplier
            if (req.Source != null)
            {
                float mult = req.Source.Stats.Get(StatType.HealingDealtMultiplier);
                if (mult > 0f) healing *= (1f + mult);
            }

            result.FinalHealing = Mathf.Max(0f, healing);
            return result;
        }

        // ─────────────────────────────────────────────────────
        //  Stat → ability coefficient helpers
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Converts the caster's primary stat into a base damage number
        /// for ability coefficient calculations.
        /// coefficient is the ability's SP/AP ratio (e.g. 0.85 = 85% of stat).
        /// </summary>
        public static float ScaledAbilityDamage(CharacterEntity caster, StatType primaryStat,
                                                 float coefficient, float flatBonus = 0f)
        {
            float statValue = caster.Stats.Get(primaryStat);
            return (statValue * coefficient) + flatBonus;
        }

        /// <summary>Same as above but for healing abilities.</summary>
        public static float ScaledAbilityHealing(CharacterEntity caster, StatType primaryStat,
                                                  float coefficient, float flatBonus = 0f)
        {
            return ScaledAbilityDamage(caster, primaryStat, coefficient, flatBonus);
        }

        // ─────────────────────────────────────────────────────
        //  Formula helpers
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Armour damage reduction fraction.
        /// Uses the classic MMORPG formula: Armor / (Armor + K)
        /// K (ArmorConstant) is tuned so 1500 armor ≈ 50% mitigation.
        /// </summary>
        public static float ArmorMitigationFraction(float armor)
        {
            if (armor <= 0f) return 0f;
            return armor / (armor + ArmorConstant);
        }

        /// <summary>
        /// Global Cooldown duration in seconds after Haste reduction.
        /// Baseline GCD = 1.5s. Haste reduces it (hard capped at 0.75s).
        /// </summary>
        public static float EffectiveGCD(CharacterEntity caster, float baseGCD = 1.5f)
        {
            float gcdReduction = caster.Stats.Get(StatType.GlobalCooldownReduction);
            return Mathf.Max(0.75f, baseGCD * (1f - gcdReduction));
        }

        /// <summary>
        /// Cooldown after Haste and CDR reduction.
        /// </summary>
        public static float EffectiveCooldown(CharacterEntity caster, float baseCooldown)
        {
            float cdr = caster.Stats.Get(StatType.CooldownReduction);
            return Mathf.Max(0.5f, baseCooldown * (1f - cdr));
        }
    }
}
