// ============================================================
//  AbilitySystem.cs
//  Manages a character's equipped ability loadout (up to 10),
//  cooldown states, GCD, cast bar progression, and execution.
//
//  Responsibilities:
//  • Hold references to the 10 equipped AbilityBase SOs
//  • Track per-ability AbilityCooldownState (independent of SO)
//  • Handle the GCD (shared across all abilities)
//  • Manage cast-bar timing for Cast-type abilities
//  • Enforce all CanCast() checks before execution
//  • Fire CombatEvents.AbilityCast / AbilityInterrupted
//
//  Multiplayer note:
//  On the client: call TryCastAbility() — it validates locally
//  and sends a ServerRpc if valid.
//  On the server: call ExecuteAbility() directly.
//  When adding NGO, wrap TryCastAbility in an [ServerRpc].
// ============================================================

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ShatteredVaults
{
    [RequireComponent(typeof(CharacterEntity))]
    public class AbilitySystem : MonoBehaviour
    {
        // ── Config ───────────────────────────────────────────
        public const int MaxAbilitySlots = 10;
        private const float DefaultGCD   = 1.5f;

        // ── Loadout ───────────────────────────────────────────
        [Header("Equipped Abilities (slots 0–9)")]
        [SerializeField] private AbilityBase[] equippedAbilities = new AbilityBase[MaxAbilitySlots];

        // ── State ────────────────────────────────────────────
        private CharacterEntity _owner;
        private AbilityCooldownState[] _cooldownStates;
        private Coroutine _activeCastCoroutine;

        // Current cast info (null when not casting)
        private AbilityBase     _castingAbility;
        private CharacterEntity _castingTarget;
        private Vector3         _castingTargetPos;
        private int             _castingSlot = -1;

        // ── Public state (read by UI) ─────────────────────────
        public bool   IsCasting        => _castingAbility != null;
        public float  CastProgress     => IsCasting && _castingAbility.CastTime > 0f
                                              ? _cooldownStates[_castingSlot].CastProgress : 0f;
        public string CastingAbilityName => _castingAbility?.AbilityName ?? "";

        // ── Events ───────────────────────────────────────────
        public event System.Action<int, AbilityBase>          OnAbilityEquipped;
        public event System.Action<int, CastBlockReason>      OnCastBlocked;
        public event System.Action<AbilityBase, CharacterEntity> OnAbilityCastStart;
        public event System.Action<AbilityBase>               OnAbilityCastInterrupted;

        // ─────────────────────────────────────────────────────
        //  Unity lifecycle
        // ─────────────────────────────────────────────────────

        private void Awake()
        {
            _owner         = GetComponent<CharacterEntity>();
            _cooldownStates = new AbilityCooldownState[MaxAbilitySlots];
            for (int i = 0; i < MaxAbilitySlots; i++)
                _cooldownStates[i] = new AbilityCooldownState();
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            // Tick all cooldown states
            for (int i = 0; i < MaxAbilitySlots; i++)
                _cooldownStates[i].Tick(dt);

            // Tick cast progress for UI
            if (IsCasting && _castingSlot >= 0 && _castingAbility.CastTime > 0f)
            {
                var state = _cooldownStates[_castingSlot];
                state.CastProgress = Mathf.Min(1f, state.CastProgress + dt / _castingAbility.CastTime);
            }
        }

        // ─────────────────────────────────────────────────────
        //  Public API
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Main entry point for input.
        /// Validates, then executes instantly or starts cast bar.
        /// Returns the block reason (None = succeeded / started).
        /// </summary>
        public CastBlockReason TryCastAbility(int slot, CharacterEntity target = null,
                                               Vector3 targetPosition = default)
        {
            if (slot < 0 || slot >= MaxAbilitySlots) return CastBlockReason.InvalidTarget;

            var ability = equippedAbilities[slot];
            if (ability == null) return CastBlockReason.InvalidTarget;

            var state  = _cooldownStates[slot];
            var reason = ability.CanCast(_owner, state, target);

            if (reason != CastBlockReason.None)
            {
                OnCastBlocked?.Invoke(slot, reason);
                return reason;
            }

            // Spend resource immediately (before cast completes — allows interruption to waste it,
            // which is intentional as a risk/reward for cast-time abilities)
            _owner.Resources.SpendResource(ability.ResourceCost);

            if (ability.CastType == AbilityCastType.Instant)
            {
                ExecuteAbility(slot, ability, target, targetPosition);
            }
            else
            {
                StartCastBar(slot, ability, target, targetPosition);
            }

            return CastBlockReason.None;
        }

        /// <summary>
        /// Interrupt the current cast (called by CCs, boss mechanics, etc.)
        /// </summary>
        public bool InterruptCast()
        {
            if (!IsCasting) return false;

            if (_activeCastCoroutine != null)
            {
                StopCoroutine(_activeCastCoroutine);
                _activeCastCoroutine = null;
            }

            var interrupted = _castingAbility;
            interrupted.OnCastInterrupted(_owner);
            CombatEvents.AbilityInterrupted(_owner, interrupted);
            OnAbilityCastInterrupted?.Invoke(interrupted);

            // Refund resource on interrupt
            _owner.Resources.RestoreResource(interrupted.ResourceCost);

            ClearCastState();
            return true;
        }

        /// <summary>
        /// Force reset a specific ability's cooldown (talent procs, loot bonuses).
        /// </summary>
        public void ResetCooldown(int slot)
        {
            if (slot >= 0 && slot < MaxAbilitySlots)
                _cooldownStates[slot].CooldownRemaining = 0f;
        }

        public void ResetCooldownByName(string abilityName)
        {
            for (int i = 0; i < MaxAbilitySlots; i++)
                if (equippedAbilities[i] != null && equippedAbilities[i].AbilityName == abilityName)
                    _cooldownStates[i].CooldownRemaining = 0f;
        }

        /// <summary>Equip an ability into a slot at runtime (subclass swap, talent change).</summary>
        public void EquipAbility(int slot, AbilityBase ability)
        {
            if (slot < 0 || slot >= MaxAbilitySlots) return;
            equippedAbilities[slot] = ability;
            _cooldownStates[slot]   = new AbilityCooldownState(); // fresh state
            OnAbilityEquipped?.Invoke(slot, ability);
        }

        // ── Read access for UI ────────────────────────────────
        public AbilityBase GetAbility(int slot) =>
            (slot >= 0 && slot < MaxAbilitySlots) ? equippedAbilities[slot] : null;

        public AbilityCooldownState GetCooldownState(int slot) =>
            (slot >= 0 && slot < MaxAbilitySlots) ? _cooldownStates[slot] : null;

        public CastBlockReason GetCastBlockReason(int slot, CharacterEntity target = null)
        {
            if (slot < 0 || slot >= MaxAbilitySlots || equippedAbilities[slot] == null)
                return CastBlockReason.InvalidTarget;
            return equippedAbilities[slot].CanCast(_owner, _cooldownStates[slot], target);
        }

        // ─────────────────────────────────────────────────────
        //  Internal execution
        // ─────────────────────────────────────────────────────

        private void ExecuteAbility(int slot, AbilityBase ability, CharacterEntity target,
                                     Vector3 targetPosition)
        {
            ability.Execute(_owner, target, targetPosition);

            // Start cooldown
            float cd  = DamageCalculator.EffectiveCooldown(_owner, ability.BaseCooldown);
            _cooldownStates[slot].StartCooldown(cd);

            // Trigger GCD for abilities that respect it
            if (ability.TriggerGCD)
            {
                float gcd = DamageCalculator.EffectiveGCD(_owner);
                for (int i = 0; i < MaxAbilitySlots; i++)
                    if (equippedAbilities[i] != null && equippedAbilities[i].TriggerGCD)
                        _cooldownStates[i].StartGCD(gcd);
            }

            CombatEvents.AbilityCast(_owner, ability);
            OnAbilityCastStart?.Invoke(ability, target);
        }

        private void StartCastBar(int slot, AbilityBase ability, CharacterEntity target,
                                   Vector3 targetPosition)
        {
            // Cancel any existing cast
            if (_activeCastCoroutine != null)
            {
                StopCoroutine(_activeCastCoroutine);
                ClearCastState();
            }

            _castingAbility    = ability;
            _castingTarget     = target;
            _castingTargetPos  = targetPosition;
            _castingSlot       = slot;
            _cooldownStates[slot].CastProgress = 0f;

            _owner.BeginCast(ability.AbilityName);
            ability.OnCastStarted(_owner, target);
            OnAbilityCastStart?.Invoke(ability, target);

            _activeCastCoroutine = StartCoroutine(CastBarRoutine(slot, ability, target, targetPosition));
        }

        private IEnumerator CastBarRoutine(int slot, AbilityBase ability, CharacterEntity target,
                                            Vector3 targetPosition)
        {
            float elapsed  = 0f;
            float castTime = ability.CastTime;

            while (elapsed < castTime)
            {
                // Abort if interrupted
                if (_owner.StatusEffects.IsStunned || _owner.StatusEffects.IsSilenced)
                {
                    InterruptCast();
                    yield break;
                }

                elapsed += Time.deltaTime;
                yield return null;
            }

            // Cast complete
            _owner.EndCast();
            ExecuteAbility(slot, ability, target, targetPosition);
            ClearCastState();
        }

        private void ClearCastState()
        {
            _castingAbility   = null;
            _castingTarget    = null;
            _castingSlot      = -1;
            _owner.EndCast();
            if (_castingSlot >= 0)
                _cooldownStates[_castingSlot].CastProgress = 0f;
            _activeCastCoroutine = null;
        }
    }
}
