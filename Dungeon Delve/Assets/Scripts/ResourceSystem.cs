// ============================================================
//  ResourceSystem.cs
//  Manages a character's primary resource (Mana, Energy, Rage,
//  Focus, Flux) and their health pool.
//
//  Design decisions:
//  • Health and resource are separate concerns but live here
//    together because they share regen logic and stat deps.
//  • Regen ticks at a fixed server-rate (configurable).
//  • All mutations go through methods that fire CombatEvents,
//    keeping UI and net layers fully decoupled.
//  • Multiplayer-ready: mark this MonoBehaviour as the
//    NetworkBehaviour owner when adding NGO; all changes
//    already flow through a single validated path.
// ============================================================

using System;
using UnityEngine;

namespace ShatteredVaults
{
    [RequireComponent(typeof(StatSheet))]
    public class ResourceSystem : MonoBehaviour
    {
        // ── Config ───────────────────────────────────────────
        [Header("Class Resource")]
        [SerializeField] private ResourceType resourceType = ResourceType.Mana;
        [SerializeField] private float        baseMaxResource = 100f;

        [Header("Combat Regen (per second)")]
        [SerializeField] private float outOfCombatRegenMultiplier = 3f;
        [SerializeField] private float regenTickRate = 0.5f;   // seconds between ticks

        // ── State ────────────────────────────────────────────
        private StatSheet _stats;
        private CharacterEntity _owner;

        private float _currentHealth;
        private float _currentResource;
        private float _regenTimer;
        private bool  _inCombat;
        private bool  _isDead;

        // Absorb shields (from abilities like Bastion subclass)
        private float _absorbShield;

        // ── Events (UI subscribes here) ───────────────────────
        public event Action<float, float> OnHealthChanged;       // (current, max)
        public event Action<float, float> OnResourceChanged;     // (current, max)
        public event Action              OnDeath;
        public event Action              OnRevive;

        // ── Properties ───────────────────────────────────────
        public float CurrentHealth   => _currentHealth;
        public float MaxHealth       => _stats.Get(StatType.MaxHealth);
        public float CurrentResource => _currentResource;
        public float MaxResource     => _stats.Get(StatType.MaxResource) > 0
                                            ? _stats.Get(StatType.MaxResource)
                                            : baseMaxResource;
        public float AbsorbShield    => _absorbShield;
        public bool  IsDead          => _isDead;
        public bool  InCombat        => _inCombat;
        public ResourceType ResourceType => resourceType;

        // ─────────────────────────────────────────────────────
        //  Unity lifecycle
        // ─────────────────────────────────────────────────────

        private void Awake()
        {
            _stats = GetComponent<StatSheet>();
            _owner = GetComponent<CharacterEntity>();
            _stats.OnStatsChanged += OnStatsDirty;
        }

        private void Start()
        {
            FullReset();
        }

        private void Update()
        {
            if (_isDead) return;
            TickRegen();
        }

        private void OnDestroy()
        {
            if (_stats != null)
                _stats.OnStatsChanged -= OnStatsDirty;
        }

        // ─────────────────────────────────────────────────────
        //  Public API
        // ─────────────────────────────────────────────────────

        /// <summary>Apply damage after mitigation.  Returns actual damage dealt.</summary>
        public float TakeDamage(float amount, DamageType type, CharacterEntity source,
                                string abilityName = "", bool isDoT = false)
        {
            if (_isDead) return 0f;

            // Absorb shield soaks damage first
            float absorbed = 0f;
            if (_absorbShield > 0f)
            {
                absorbed      = Mathf.Min(_absorbShield, amount);
                _absorbShield = Mathf.Max(0f, _absorbShield - amount);
                amount       -= absorbed;
            }

            float final = Mathf.Max(0f, amount);
            _currentHealth = Mathf.Max(0f, _currentHealth - final);

            NotifyHealthChanged();

            if (_currentHealth <= 0f)
                Die(source);

            return final;
        }

        /// <summary>Receive healing.  Returns actual healing received.</summary>
        public float ReceiveHealing(float amount, CharacterEntity source, string abilityName = "",
                                    bool isHoT = false)
        {
            if (_isDead) return 0f;

            float before    = _currentHealth;
            _currentHealth  = Mathf.Min(MaxHealth, _currentHealth + amount);
            float actual    = _currentHealth - before;

            NotifyHealthChanged();
            return actual;
        }

        /// <summary>Add an absorb shield (stacks additively).</summary>
        public void AddAbsorbShield(float amount)
        {
            _absorbShield += amount;
            NotifyHealthChanged();
        }

        /// <summary>Spend resource. Returns false if insufficient.</summary>
        public bool SpendResource(float amount)
        {
            if (_currentResource < amount) return false;

            float before      = _currentResource;
            _currentResource  = Mathf.Max(0f, _currentResource - amount);
            FireResourceEvent(before);
            return true;
        }

        /// <summary>Restore resource (ability grants, passive refunds).</summary>
        public void RestoreResource(float amount)
        {
            float before     = _currentResource;
            _currentResource = Mathf.Min(MaxResource, _currentResource + amount);
            FireResourceEvent(before);
        }

        /// <summary>Set in-combat flag — affects regen rate.</summary>
        public void SetCombatState(bool inCombat)
        {
            _inCombat = inCombat;
        }

        /// <summary>Revive with a given health fraction (0–1).</summary>
        public void Revive(float healthFraction = 0.3f)
        {
            if (!_isDead) return;

            _isDead        = false;
            _currentHealth = MaxHealth * Mathf.Clamp01(healthFraction);
            _currentResource = resourceType == ResourceType.Rage ? 0f : MaxResource * 0.5f;

            NotifyHealthChanged();
            OnRevive?.Invoke();
            CombatEvents.CharacterRevived(_owner);
        }

        public void FullReset()
        {
            _isDead          = false;
            _currentHealth   = MaxHealth;
            _absorbShield    = 0f;
            _currentResource = resourceType == ResourceType.Rage ? 0f : MaxResource;
            NotifyHealthChanged();
            FireResourceEvent(0f);
        }

        // ─────────────────────────────────────────────────────
        //  Internal
        // ─────────────────────────────────────────────────────

        private void TickRegen()
        {
            _regenTimer += Time.deltaTime;
            if (_regenTimer < regenTickRate) return;
            _regenTimer = 0f;

            float mult = _inCombat ? 1f : outOfCombatRegenMultiplier;

            // Health regen (small, primarily for out-of-combat recovery)
            float hpRegen = _stats.Get(StatType.HealthRegen) * mult * regenTickRate;
            if (hpRegen > 0f && _currentHealth < MaxHealth)
                ReceiveHealing(hpRegen, _owner);

            // Resource regen varies by resource type
            float resRegen = GetResourceRegenPerTick(mult);
            if (Mathf.Abs(resRegen) > 0f)
            {
                float before = _currentResource;
                _currentResource = Mathf.Clamp(_currentResource + resRegen, 0f, MaxResource);
                if (!Mathf.Approximately(before, _currentResource))
                    FireResourceEvent(before);
            }
        }

        private float GetResourceRegenPerTick(float mult)
        {
            float statRegen = _stats.Get(StatType.ResourceRegen);

            return resourceType switch
            {
                // Mana regens steadily (faster OOC)
                ResourceType.Mana   => (statRegen > 0 ? statRegen : 2f) * mult * regenTickRate,

                // Energy regens fast (like WoW rogue) — always at same rate
                ResourceType.Energy => 10f * regenTickRate,

                // Rage generates from taking/dealing damage, decays OOC
                ResourceType.Rage   => _inCombat ? 0f : -5f * regenTickRate,

                // Focus regens slowly in combat
                ResourceType.Focus  => 1f * mult * regenTickRate,

                // Flux regens from casting
                ResourceType.Flux   => 0f,

                _                   => 0f,
            };
        }

        /// <summary>Called by Ironclad when taking or dealing damage to generate Rage.</summary>
        public void GenerateRage(float amount)
        {
            if (resourceType != ResourceType.Rage) return;
            RestoreResource(amount);
        }

        private void Die(CharacterEntity killer)
        {
            if (_isDead) return;
            _isDead        = true;
            _currentHealth = 0f;
            _absorbShield  = 0f;

            NotifyHealthChanged();
            OnDeath?.Invoke();
            CombatEvents.CharacterDied(new CharacterDeathInfo { Deceased = _owner, Killer = killer });
        }

        private void NotifyHealthChanged()
        {
            OnHealthChanged?.Invoke(_currentHealth, MaxHealth);
        }

        private void FireResourceEvent(float previousValue)
        {
            float delta = _currentResource - previousValue;
            OnResourceChanged?.Invoke(_currentResource, MaxResource);
            CombatEvents.ResourceChanged(new ResourceChangeInfo
            {
                Owner        = _owner,
                ResourceType = resourceType,
                Delta        = delta,
                NewValue     = _currentResource,
                MaxValue     = MaxResource,
            });
        }

        private void OnStatsDirty()
        {
            // Clamp current values to new maxes when stats change (gear swap, level up)
            _currentHealth   = Mathf.Min(_currentHealth,   MaxHealth);
            _currentResource = Mathf.Min(_currentResource, MaxResource);
            NotifyHealthChanged();
            FireResourceEvent(_currentResource);
        }
    }
}
