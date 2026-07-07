// ============================================================
//  ThreatTable.cs
//  Attached to enemy entities.  Tracks accumulated threat
//  per player and determines the current aggro target.
//
//  Tank design notes:
//  • Taunt sets the taunting player's threat to 110% of the
//    current top threat holder (guaranteed aggro for 3s).
//  • Threat is a float accumulator — no decay in combat.
//  • Threat is wiped when combat ends.
//  • ThreatMultiplier stat on tanks makes all their threat
//    generation higher, keeping them at the top naturally.
// ============================================================

using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ShatteredVaults
{
    [RequireComponent(typeof(CharacterEntity))]
    public class ThreatTable : MonoBehaviour
    {
        // ── State ────────────────────────────────────────────
        private readonly Dictionary<CharacterEntity, float> _threat = new();
        private CharacterEntity _currentTarget;
        private CharacterEntity _tauntTarget;
        private float           _tauntTimer;

        public CharacterEntity CurrentTarget => _tauntTimer > 0f ? _tauntTarget : _currentTarget;

        // ── Events ───────────────────────────────────────────
        public event System.Action<CharacterEntity> OnAggroTargetChanged;

        // ─────────────────────────────────────────────────────
        //  Unity lifecycle
        // ─────────────────────────────────────────────────────

        private void Update()
        {
            if (_tauntTimer > 0f)
            {
                _tauntTimer -= Time.deltaTime;
                if (_tauntTimer <= 0f)
                    _tauntTarget = null;
            }
        }

        // ─────────────────────────────────────────────────────
        //  Public API
        // ─────────────────────────────────────────────────────

        /// <summary>Add threat from a player character.</summary>
        public void AddThreat(CharacterEntity source, float amount)
        {
            if (source == null || amount <= 0f) return;

            if (!_threat.ContainsKey(source))
                _threat[source] = 0f;

            _threat[source] += amount;

            UpdateAggroTarget();
        }

        /// <summary>
        /// Taunt: force this enemy to attack the taunter for duration seconds,
        /// and set their threat to 110% of the current top holder.
        /// </summary>
        public void Taunt(CharacterEntity taunter, float duration = 3f)
        {
            float topThreat = _threat.Values.DefaultIfEmpty(0f).Max();
            float newThreat = topThreat * 1.1f;

            if (!_threat.ContainsKey(taunter))
                _threat[taunter] = 0f;

            _threat[taunter] = Mathf.Max(_threat[taunter], newThreat);

            _tauntTarget = taunter;
            _tauntTimer  = duration;

            CombatEvents.ThreatGenerated(new ThreatInfo
            {
                Source = taunter, Target = GetComponent<CharacterEntity>(),
                Amount = newThreat, Reason = "Taunt"
            });

            UpdateAggroTarget();
        }

        /// <summary>
        /// Drop threat for a specific player (e.g. Ashenhand Smoke Screen ability).
        /// </summary>
        public void DropThreat(CharacterEntity source, float fraction = 1f)
        {
            if (_threat.ContainsKey(source))
                _threat[source] *= (1f - Mathf.Clamp01(fraction));

            UpdateAggroTarget();
        }

        /// <summary>
        /// Remove a player from the threat table (dead, left instance, etc.)
        /// </summary>
        public void RemoveEntry(CharacterEntity source)
        {
            _threat.Remove(source);
            if (_tauntTarget == source) { _tauntTarget = null; _tauntTimer = 0f; }
            UpdateAggroTarget();
        }

        public void ClearAll()
        {
            _threat.Clear();
            _tauntTarget = null;
            _tauntTimer  = 0f;
            _currentTarget = null;
        }

        /// <summary>Get sorted threat list for UI (tank threat meters).</summary>
        public List<(CharacterEntity Entity, float Threat)> GetSortedThreat()
        {
            return _threat
                .OrderByDescending(kvp => kvp.Value)
                .Select(kvp => (kvp.Key, kvp.Value))
                .ToList();
        }

        // ─────────────────────────────────────────────────────
        //  Internal
        // ─────────────────────────────────────────────────────

        private void UpdateAggroTarget()
        {
            if (_tauntTimer > 0f) return; // taunt overrides

            CharacterEntity newTop = null;
            float topValue = 0f;

            foreach (var kvp in _threat)
            {
                if (kvp.Key == null || kvp.Key.IsDead) continue;
                if (kvp.Value > topValue)
                {
                    topValue = kvp.Value;
                    newTop   = kvp.Key;
                }
            }

            if (newTop != _currentTarget)
            {
                _currentTarget = newTop;
                OnAggroTargetChanged?.Invoke(_currentTarget);
            }
        }
    }
}
