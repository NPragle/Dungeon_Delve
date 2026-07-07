// ============================================================
//  ConcreteAbilities.cs
//  Three fully implemented example abilities, one per role.
//  These serve as the template pattern for all future abilities.
//
//  To create a new ability:
//  1. Create a new .cs file (or add to this one)
//  2. Inherit from AbilityBase
//  3. Add [CreateAssetMenu] attribute
//  4. Override Execute() — use the helpers from AbilityBase
//  5. Right-click in Project → Create → Abilities → [YourAbility]
//  6. Assign the new SO to the class's ClassDefinitionSO
// ============================================================

using System.Collections.Generic;
using UnityEngine;

namespace ShatteredVaults
{
    // ══════════════════════════════════════════════════════════
    //  SHIELD SLAM  —  Ironclad (Tank)
    //  Instant melee strike that deals physical damage scaled
    //  on Strength and generates significant Rage + threat.
    //  Warden subclass talent: triggers a free Counterstrike.
    // ══════════════════════════════════════════════════════════

    [CreateAssetMenu(menuName = "ShatteredVaults/Abilities/Ironclad/ShieldSlam")]
    public class ShieldSlam : AbilityBase
    {
        [Header("Shield Slam Config")]
        [Tooltip("Rage generated on hit")]
        public float RageOnHit = 15f;

        [Tooltip("Threat multiplier on top of damage threat")]
        public float BonusThreatMultiplier = 2.0f;

        [Tooltip("% of remaining absorb shield added as bonus damage")]
        [Range(0f, 1f)]
        public float ShieldDamageBonus = 0.1f;

        private void Reset()
        {
            AbilityName  = "Shield Slam";
            Description  = "Slam your shield into the enemy, dealing physical damage. " +
                           "Generates high threat and bonus Rage. " +
                           "Damage increased by your active absorb shield.";
            CastType     = AbilityCastType.Instant;
            BaseCooldown = 6f;
            TriggerGCD   = true;
            TargetType   = AbilityTargetType.SingleEnemy;
            Range        = 5f;
            ResourceType = ResourceType.Rage;
            ResourceCost = 0f;              // Rage spender variant: set to 20 for spender build
            ScalingStat  = StatType.Strength;
            Coefficient  = 1.1f;
            DamageType   = DamageType.Physical;
            CanCrit      = true;
            CanMiss      = true;
        }

        public override void Execute(CharacterEntity caster, CharacterEntity target,
                                      Vector3 targetPosition)
        {
            if (target == null || target.IsDead) return;

            // Base damage from Strength
            float baseDmg = ComputeScaledValue(caster);

            // Shield bonus: 10% of current absorb shield added as flat damage
            float shieldBonus = caster.Resources.AbsorbShield * ShieldDamageBonus;
            baseDmg += shieldBonus;

            // Apply the hit
            var result = ApplyDamage(caster, target, baseDmg);

            // Only on a successful hit
            if (result.HitResult != HitResult.Miss && result.HitResult != HitResult.Dodge)
            {
                // Generate extra threat beyond the damage
                float bonusThreat = result.FinalDamage * BonusThreatMultiplier;
                if (target.ThreatTable != null)
                    target.ThreatTable.AddThreat(caster, bonusThreat);

                // Generate Rage
                caster.Resources.GenerateRage(RageOnHit);

                // Critical hit: generate additional Rage
                if (result.WasCritical)
                    caster.Resources.GenerateRage(RageOnHit * 0.5f);
            }
        }
    }

    // ══════════════════════════════════════════════════════════
    //  CASCADE HEAL  —  Lifebinder (Healer)
    //  Instant heal on primary target that chains to up to
    //  2 nearby injured allies for 60% effectiveness.
    //  Channeler subclass talent: removes one debuff on primary.
    // ══════════════════════════════════════════════════════════

    [CreateAssetMenu(menuName = "ShatteredVaults/Abilities/Lifebinder/CascadeHeal")]
    public class CascadeHeal : AbilityBase
    {
        [Header("Cascade Heal Config")]
        [Tooltip("Max number of additional allies the heal jumps to")]
        public int   ChainTargets   = 2;

        [Tooltip("Heal effectiveness per chain bounce (fraction)")]
        [Range(0f, 1f)]
        public float ChainFalloff   = 0.6f;

        [Tooltip("Max distance a chain jump can travel (metres)")]
        public float ChainRange     = 12f;

        [Tooltip("If true (Channeler talent), remove one debuff from primary target")]
        public bool  RemoveDebuffOnCast = false;

        private void Reset()
        {
            AbilityName  = "Cascade Heal";
            Description  = "Heals the target, then chains to up to 2 nearby injured allies " +
                           "for 60% effectiveness each.";
            CastType     = AbilityCastType.Instant;
            BaseCooldown = 0f;          // core spam heal, only GCD limited
            TriggerGCD   = true;
            TargetType   = AbilityTargetType.SingleAlly;
            Range        = 40f;
            ResourceType = ResourceType.Mana;
            ResourceCost = 12f;
            ScalingStat  = StatType.Intelligence;
            Coefficient  = 0.72f;
            DamageType   = DamageType.True;   // healing has no damage type
            CanCrit      = true;
            CanMiss      = false;
        }

        public override void Execute(CharacterEntity caster, CharacterEntity target,
                                      Vector3 targetPosition)
        {
            if (target == null) return;

            // Primary heal
            float baseHeal = ComputeScaledValue(caster);
            ApplyHealing(caster, target, baseHeal);

            // Optional debuff removal (Channeler talent)
            if (RemoveDebuffOnCast)
                target.StatusEffects.CleansAllDebuffs();

            // Chain bounces — find nearby injured allies
            var chained = new HashSet<CharacterEntity> { target };
            CharacterEntity lastTarget = target;
            float currentAmount = baseHeal;

            for (int i = 0; i < ChainTargets; i++)
            {
                currentAmount *= ChainFalloff;
                CharacterEntity next = FindNearestInjuredAlly(caster, lastTarget, chained);
                if (next == null) break;

                ApplyHealing(caster, next, currentAmount, isHoT: false);
                chained.Add(next);
                lastTarget = next;
            }
        }

        private CharacterEntity FindNearestInjuredAlly(CharacterEntity caster,
                                                         CharacterEntity origin,
                                                         HashSet<CharacterEntity> exclude)
        {
            // In production: query a PartyManager or overlap sphere
            // For now: find all CharacterEntity components in scene and filter
            CharacterEntity best     = null;
            float           bestDist = ChainRange;
            float           bestHP   = float.MaxValue;

            // Only check players (allies) — enemies excluded
            foreach (var entity in FindObjectsByType<CharacterEntity>(FindObjectsSortMode.None))
            {
                if (!entity.IsPlayer)          continue;
                if (entity.IsDead)             continue;
                if (exclude.Contains(entity))  continue;

                // Only chain to injured targets
                if (entity.CurrentHP >= entity.MaxHP) continue;

                float dist = Vector3.Distance(origin.transform.position, entity.transform.position);
                if (dist > ChainRange) continue;

                // Prefer most injured (lowest HP %) within range
                float hpFrac = entity.CurrentHP / entity.MaxHP;
                if (hpFrac < bestHP)
                {
                    bestHP   = hpFrac;
                    bestDist = dist;
                    best     = entity;
                }
            }

            return best;
        }
    }

    // ══════════════════════════════════════════════════════════
    //  VOID STRIKE  —  Ashenhand (Melee DPS)
    //  Instant melee strike dealing Void damage.
    //  Builds one Void Charge stack (max 5).
    //  At 5 stacks: next Void Strike deals 200% damage and
    //  resets all stacks (Berserker subclass synergy).
    // ══════════════════════════════════════════════════════════

    [CreateAssetMenu(menuName = "ShatteredVaults/Abilities/Ashenhand/VoidStrike")]
    public class VoidStrike : AbilityBase
    {
        [Header("Void Strike Config")]
        public int   MaxVoidCharges      = 5;
        public float Empower_DamageMultiplier = 2.0f;
        public float ComboPointsPerHit   = 1f;

        [Tooltip("Bonus damage coefficient per active Void Charge stack")]
        [Range(0f, 0.5f)]
        public float PerStackBonus = 0.12f;

        // Stack counter — per instance, stored here for simplicity.
        // In a real project, store this on a class-specific component
        // so it persists across ability swaps.
        private readonly Dictionary<CharacterEntity, int> _stackTracker = new();

        private void Reset()
        {
            AbilityName  = "Void Strike";
            Description  = "A rapid void-infused strike. Builds Void Charges. " +
                           "At 5 charges, the next strike detonates them for massive damage.";
            CastType     = AbilityCastType.Instant;
            BaseCooldown = 0f;          // energy-gated, GCD limited
            TriggerGCD   = true;
            TargetType   = AbilityTargetType.SingleEnemy;
            Range        = 3f;
            ResourceType = ResourceType.Energy;
            ResourceCost = 35f;
            ScalingStat  = StatType.Agility;
            Coefficient  = 0.65f;
            DamageType   = DamageType.Void;
            CanCrit      = true;
            CanMiss      = true;
        }

        public override void Execute(CharacterEntity caster, CharacterEntity target,
                                      Vector3 targetPosition)
        {
            if (target == null || target.IsDead) return;

            // Get current stacks for this caster
            if (!_stackTracker.ContainsKey(caster))
                _stackTracker[caster] = 0;

            int stacks = _stackTracker[caster];
            float baseDmg = ComputeScaledValue(caster);

            // Add per-stack bonus
            baseDmg *= (1f + stacks * PerStackBonus);

            bool isEmpowered = stacks >= MaxVoidCharges;
            if (isEmpowered)
            {
                // Empowered strike: full damage multiplier + reset stacks
                baseDmg *= Empower_DamageMultiplier;
                _stackTracker[caster] = 0;

                // Visual feedback hint: fire a special event (UI can listen)
                Debug.Log($"[VoidStrike] EMPOWERED detonation by {caster.CharacterName}!");
            }
            else
            {
                _stackTracker[caster]++;
            }

            var result = ApplyDamage(caster, target, baseDmg);

            // Apply a Void Rupture status (light DoT) on every non-empowered hit
            if (!isEmpowered && result.HitResult != HitResult.Miss
                              && result.HitResult != HitResult.Dodge)
            {
                var rupture = new ActiveStatusEffect
                {
                    EffectType     = StatusEffectType.Bleeding,
                    EffectId       = $"void_rupture_{caster.GetInstanceID()}",
                    Source         = caster,
                    TotalDuration  = 6f,
                    RemainingDuration = 6f,
                    TickInterval   = 2f,
                    TickTimer      = 2f,
                    TickValue      = baseDmg * 0.15f,
                    DamageType     = DamageType.Void,
                    IsStackable    = false,
                };
                ApplyStatus(caster, target, rupture);
            }
        }

        // Clean up when the caster leaves combat
        public void ClearStacks(CharacterEntity caster)
        {
            _stackTracker.Remove(caster);
        }

        public int GetStacks(CharacterEntity caster) =>
            _stackTracker.TryGetValue(caster, out int s) ? s : 0;
    }
}
