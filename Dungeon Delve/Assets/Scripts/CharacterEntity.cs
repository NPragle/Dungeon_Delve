// ============================================================
//  CharacterEntity.cs
//  Root component for every character in the game (players
//  and enemies).  Owns the character's identity and provides
//  a clean single point of access to all subsystems.
//
//  Multiplayer note:
//  When adding NGO, make this extend NetworkBehaviour.
//  Authority checks (IsOwner / IsServer) go into the 
//  CharacterController subclass, NOT here, keeping this
//  class clean for both sides of the net boundary.
// ============================================================

using System.Collections.Generic;
using UnityEngine;

namespace ShatteredVaults
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(StatSheet))]
    [RequireComponent(typeof(ResourceSystem))]
    [RequireComponent(typeof(StatusEffectHandler))]
    public class CharacterEntity : MonoBehaviour
    {
        // ── Identity ─────────────────────────────────────────
        [Header("Identity")]
        public string       CharacterName  = "Unknown";
        public ClassType    Class;
        public CharacterRole Role;
        public bool         IsPlayer;       // false = enemy / boss

        // ── Subsystem references (set in Awake) ──────────────
        public StatSheet          Stats          { get; private set; }
        public ResourceSystem     Resources      { get; private set; }
        public StatusEffectHandler StatusEffects  { get; private set; }

        // Threat table — only used by enemy entities
        // Populated by ThreatSystem component on enemies
        private ThreatTable _threatTable;
        public ThreatTable  ThreatTable => _threatTable;

        // ── Interrupt / cast state ───────────────────────────
        public bool   IsInterrupted   { get; private set; }
        public bool   IsCasting       { get; private set; }
        public string CurrentCastName { get; private set; }

        // ── Party reference (set by PartySystem) ─────────────
        public int PartySlot { get; set; } = -1;   // -1 = unassigned / enemy

        // ─────────────────────────────────────────────────────
        //  Unity lifecycle
        // ─────────────────────────────────────────────────────

        protected virtual void Awake()
        {
            Stats         = GetComponent<StatSheet>();
            Resources     = GetComponent<ResourceSystem>();
            StatusEffects = GetComponent<StatusEffectHandler>();

            if (!IsPlayer)
                _threatTable = GetComponent<ThreatTable>();
        }

        // ─────────────────────────────────────────────────────
        //  Cast state management (used by AbilitySystem)
        // ─────────────────────────────────────────────────────

        public void BeginCast(string abilityName)
        {
            IsCasting       = true;
            CurrentCastName = abilityName;
        }

        public void EndCast()
        {
            IsCasting       = false;
            CurrentCastName = string.Empty;
        }

        /// <summary>Interrupt an in-progress cast. Returns true if a cast was interrupted.</summary>
        public bool Interrupt()
        {
            if (!IsCasting) return false;
            EndCast();
            IsInterrupted = true;
            return true;
        }

        public void ClearInterrupt()
        {
            IsInterrupted = false;
        }

        // ─────────────────────────────────────────────────────
        //  Convenience passthrough helpers
        // ─────────────────────────────────────────────────────

        public bool  IsDead     => Resources.IsDead;
        public float CurrentHP  => Resources.CurrentHealth;
        public float MaxHP      => Resources.MaxHealth;

        /// <summary>True if this entity can be targeted by hostile abilities.</summary>
        public bool IsValidTarget(CharacterEntity from)
        {
            if (IsDead) return false;
            // Immune status (TODO: StatusEffectHandler.Has(StatusEffectType.Immune))
            return true;
        }

        // ─────────────────────────────────────────────────────
        //  Debug helpers
        // ─────────────────────────────────────────────────────

#if UNITY_EDITOR
        [ContextMenu("Log Character State")]
        private void DebugLog()
        {
            Debug.Log($"[{CharacterName}] HP:{CurrentHP:F0}/{MaxHP:F0}  " +
                      $"Resource:{Resources.CurrentResource:F0}/{Resources.MaxResource:F0}  " +
                      $"Dead:{IsDead}  Casting:{IsCasting}");
        }
#endif
    }
}
