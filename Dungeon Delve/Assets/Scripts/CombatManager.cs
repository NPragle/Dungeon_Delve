// ============================================================
//  CombatManager.cs
//  Scene-level singleton that:
//  • Tracks all active characters (players + enemies)
//  • Manages in-combat / out-of-combat transitions
//  • Coordinates group wipe detection
//  • Provides a CharacterFactory.Spawn() helper
//  • Listens to CombatEvents and drives any cross-character logic
//
//  This is NOT a god class — it coordinates, not implements.
//  Each system (StatSheet, ResourceSystem, etc.) is self-contained.
//
//  Multiplayer note:
//  In NGO, make this a NetworkBehaviour on the server only.
//  All Spawn() calls become ServerRpc-gated.
// ============================================================

using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ShatteredVaults
{
    public class CombatManager : MonoBehaviour
    {
        // ── Singleton ────────────────────────────────────────
        public static CombatManager Instance { get; private set; }

        // ── Tracked entities ─────────────────────────────────
        private readonly List<CharacterEntity> _players = new();
        private readonly List<CharacterEntity> _enemies = new();

        // ── Combat state ─────────────────────────────────────
        private bool _combatActive;
        public bool  CombatActive => _combatActive;

        // ── Events ───────────────────────────────────────────
        public event System.Action OnCombatStart;
        public event System.Action OnCombatEnd;
        public event System.Action OnGroupWipe;

        // ─────────────────────────────────────────────────────
        //  Unity lifecycle
        // ─────────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void OnEnable()
        {
            CombatEvents.OnCharacterDied     += HandleDeath;
            CombatEvents.OnDamageDealt       += HandleDamageForCombatEntry;
        }

        private void OnDisable()
        {
            CombatEvents.OnCharacterDied     -= HandleDeath;
            CombatEvents.OnDamageDealt       -= HandleDamageForCombatEntry;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            CombatEvents.ClearAllListeners();
        }

        // ─────────────────────────────────────────────────────
        //  Registration (called by CharacterEntity on spawn)
        // ─────────────────────────────────────────────────────

        public void RegisterCharacter(CharacterEntity entity)
        {
            if (entity.IsPlayer)
            {
                if (!_players.Contains(entity)) _players.Add(entity);
            }
            else
            {
                if (!_enemies.Contains(entity)) _enemies.Add(entity);
            }
        }

        public void UnregisterCharacter(CharacterEntity entity)
        {
            _players.Remove(entity);
            _enemies.Remove(entity);
        }

        // ─────────────────────────────────────────────────────
        //  Public queries
        // ─────────────────────────────────────────────────────

        public IReadOnlyList<CharacterEntity> Players  => _players;
        public IReadOnlyList<CharacterEntity> Enemies  => _enemies;

        public CharacterEntity GetLowestHealthAlly(CharacterEntity relativeTo)
        {
            return _players
                .Where(p => !p.IsDead && p != relativeTo)
                .OrderBy(p => p.CurrentHP / Mathf.Max(p.MaxHP, 1f))
                .FirstOrDefault();
        }

        public List<CharacterEntity> GetEnemiesInRadius(Vector3 origin, float radius)
        {
            return _enemies.Where(e => !e.IsDead &&
                Vector3.Distance(e.transform.position, origin) <= radius).ToList();
        }

        public List<CharacterEntity> GetAlliesInRadius(Vector3 origin, float radius,
                                                         CharacterEntity exclude = null)
        {
            return _players.Where(p => !p.IsDead && p != exclude &&
                Vector3.Distance(p.transform.position, origin) <= radius).ToList();
        }

        // ─────────────────────────────────────────────────────
        //  Combat state management
        // ─────────────────────────────────────────────────────

        public void StartCombat()
        {
            if (_combatActive) return;
            _combatActive = true;
            foreach (var p in _players) p.Resources.SetCombatState(true);
            foreach (var e in _enemies) e.Resources.SetCombatState(true);
            OnCombatStart?.Invoke();
        }

        public void EndCombat()
        {
            if (!_combatActive) return;
            _combatActive = false;
            foreach (var p in _players) p.Resources.SetCombatState(false);
            foreach (var e in _enemies) e.Resources.SetCombatState(false);

            // Clear all enemy threat tables
            foreach (var e in _enemies)
                if (e.ThreatTable != null) e.ThreatTable.ClearAll();

            OnCombatEnd?.Invoke();
        }

        // ─────────────────────────────────────────────────────
        //  Event handlers
        // ─────────────────────────────────────────────────────

        private void HandleDeath(CharacterDeathInfo info)
        {
            if (!info.Deceased.IsPlayer)
            {
                // Check if all enemies dead → end combat
                bool allEnemiesDead = _enemies.All(e => e.IsDead);
                if (allEnemiesDead) EndCombat();
            }
            else
            {
                // Check for group wipe
                bool allPlayersDead = _players.All(p => p.IsDead);
                if (allPlayersDead)
                {
                    Debug.Log("[CombatManager] Group wipe detected.");
                    OnGroupWipe?.Invoke();
                }
            }
        }

        private void HandleDamageForCombatEntry(DamageInfo info)
        {
            // Auto-start combat on first damage event
            if (!_combatActive && info.FinalAmount > 0f)
                StartCombat();
        }
    }

    // ══════════════════════════════════════════════════════════
    //  CHARACTER FACTORY
    //  Spawns and initialises a CharacterEntity from a prefab
    //  and a ClassDefinitionSO.
    // ══════════════════════════════════════════════════════════

    public static class CharacterFactory
    {
        /// <summary>
        /// Spawn a player character prefab and initialise it with the given class.
        /// The prefab must have CharacterEntity, StatSheet, ResourceSystem,
        /// StatusEffectHandler, and AbilitySystem components attached.
        /// </summary>
        public static CharacterEntity SpawnPlayer(GameObject prefab,
                                                   ClassDefinitionSO classDef,
                                                   int level,
                                                   Vector3 position,
                                                   string playerName = "Player")
        {
            var go     = Object.Instantiate(prefab, position, Quaternion.identity);
            var entity = go.GetComponent<CharacterEntity>();

            if (entity == null)
            {
                Debug.LogError("[CharacterFactory] Prefab is missing CharacterEntity component.");
                Object.Destroy(go);
                return null;
            }

            entity.CharacterName = playerName;
            entity.IsPlayer      = true;
            entity.Class         = classDef.ClassType;
            entity.Role          = classDef.Role;

            // Initialise stats from class definition
            var stats = go.GetComponent<StatSheet>();
            classDef.ApplyToStatSheet(stats, level);

            // Initialise ability loadout
            var abilitySystem = go.GetComponent<AbilitySystem>();
            if (abilitySystem != null)
                classDef.ApplyLoadout(abilitySystem);

            // Reset health/resource to full after stats are set
            var resources = go.GetComponent<ResourceSystem>();
            resources.FullReset();

            // Register with CombatManager
            CombatManager.Instance?.RegisterCharacter(entity);

            return entity;
        }

        /// <summary>
        /// Spawn an enemy and register it with the CombatManager.
        /// Enemy prefabs configure their own base stats via EnemySO (not yet built).
        /// </summary>
        public static CharacterEntity SpawnEnemy(GameObject prefab,
                                                  Vector3 position,
                                                  string enemyName = "Enemy")
        {
            var go     = Object.Instantiate(prefab, position, Quaternion.identity);
            var entity = go.GetComponent<CharacterEntity>();

            if (entity == null)
            {
                Debug.LogError("[CharacterFactory] Enemy prefab missing CharacterEntity.");
                Object.Destroy(go);
                return null;
            }

            entity.CharacterName = enemyName;
            entity.IsPlayer      = false;

            CombatManager.Instance?.RegisterCharacter(entity);
            return entity;
        }
    }
}
