// ============================================================
//  CombatEvents.cs
//  Lightweight event bus.  All combat systems communicate
//  through here — never via direct references.  This makes
//  the codebase network-ready: a future net layer just needs
//  to listen to these events and replicate them.
//
//  Usage (subscribe):
//      CombatEvents.OnDamageDealt += HandleDamage;
//  Usage (fire):
//      CombatEvents.DamageDealt(info);
// ============================================================

using System;
using UnityEngine;

namespace ShatteredVaults
{
    // ── Payload structs (value types for GC friendliness) ────

    public struct DamageInfo
    {
        public CharacterEntity Source;
        public CharacterEntity Target;
        public float RawAmount;
        public float FinalAmount;
        public DamageType DamageType;
        public HitResult HitResult;
        public bool IsDoT;
        public string AbilityName;      // for combat log
    }

    public struct HealInfo
    {
        public CharacterEntity Source;
        public CharacterEntity Target;
        public float RawAmount;
        public float FinalAmount;
        public HitResult HitResult;
        public bool IsHoT;
        public string AbilityName;
    }

    public struct ResourceChangeInfo
    {
        public CharacterEntity Owner;
        public ResourceType ResourceType;
        public float Delta;             // positive = gain, negative = spend
        public float NewValue;
        public float MaxValue;
    }

    public struct StatusEventInfo
    {
        public CharacterEntity Source;
        public CharacterEntity Target;
        public StatusEffectType EffectType;
        public float Duration;
        public bool Applied;            // false = removed
    }

    public struct ThreatInfo
    {
        public CharacterEntity Source;
        public CharacterEntity Target;   // the enemy receiving threat
        public float Amount;
        public string Reason;
    }

    public struct CharacterDeathInfo
    {
        public CharacterEntity Deceased;
        public CharacterEntity Killer;   // null if environment
    }

    // ── Event bus ────────────────────────────────────────────

    public static class CombatEvents
    {
        // Damage & healing
        public static event Action<DamageInfo>  OnDamageDealt;
        public static event Action<HealInfo>    OnHealingDealt;

        // Resources
        public static event Action<ResourceChangeInfo> OnResourceChanged;

        // Status effects
        public static event Action<StatusEventInfo> OnStatusChanged;

        // Threat
        public static event Action<ThreatInfo> OnThreatGenerated;

        // Life / death
        public static event Action<CharacterDeathInfo> OnCharacterDied;
        public static event Action<CharacterEntity>    OnCharacterRevived;

        // Ability
        public static event Action<CharacterEntity, AbilityBase> OnAbilityCast;
        public static event Action<CharacterEntity, AbilityBase> OnAbilityInterrupted;

        // ── Fire helpers ─────────────────────────────────────

        public static void DamageDealt(DamageInfo info)
        {
            OnDamageDealt?.Invoke(info);
        }

        public static void HealingDealt(HealInfo info)
        {
            OnHealingDealt?.Invoke(info);
        }

        public static void ResourceChanged(ResourceChangeInfo info)
        {
            OnResourceChanged?.Invoke(info);
        }

        public static void StatusChanged(StatusEventInfo info)
        {
            OnStatusChanged?.Invoke(info);
        }

        public static void ThreatGenerated(ThreatInfo info)
        {
            OnThreatGenerated?.Invoke(info);
        }

        public static void CharacterDied(CharacterDeathInfo info)
        {
            OnCharacterDied?.Invoke(info);
        }

        public static void CharacterRevived(CharacterEntity entity)
        {
            OnCharacterRevived?.Invoke(entity);
        }

        public static void AbilityCast(CharacterEntity caster, AbilityBase ability)
        {
            OnAbilityCast?.Invoke(caster, ability);
        }

        public static void AbilityInterrupted(CharacterEntity caster, AbilityBase ability)
        {
            OnAbilityInterrupted?.Invoke(caster, ability);
        }

        // ── Cleanup (call on scene unload) ───────────────────
        public static void ClearAllListeners()
        {
            OnDamageDealt       = null;
            OnHealingDealt      = null;
            OnResourceChanged   = null;
            OnStatusChanged     = null;
            OnThreatGenerated   = null;
            OnCharacterDied     = null;
            OnCharacterRevived  = null;
            OnAbilityCast       = null;
            OnAbilityInterrupted = null;
        }
    }
}
