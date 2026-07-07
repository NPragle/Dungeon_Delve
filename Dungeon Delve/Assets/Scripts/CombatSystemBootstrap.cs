// ============================================================
//  CombatSystemBootstrap.cs
//  Minimal scene wiring script for testing the combat loop.
//  Attach to a single GameObject named "CombatBootstrap"
//  in your test scene.
//
//  It will:
//  1. Create a CombatManager if one isn't in the scene
//  2. Spawn a test player (Ironclad) and a test enemy
//  3. Assign Shield Slam to slot 0
//  4. Run a basic combat cycle so you can see events fire
//
//  This is a DEV TOOL — delete or #if UNITY_EDITOR guard
//  before shipping.
// ============================================================

using UnityEngine;

namespace ShatteredVaults
{
    public class CombatSystemBootstrap : MonoBehaviour
    {
        [Header("Prefabs (assign in Inspector)")]
        public GameObject        PlayerPrefab;
        public GameObject        EnemyPrefab;

        [Header("Data (assign in Inspector)")]
        public ClassDefinitionSO IroncladDefinition;
        public ShieldSlam        ShieldSlamAbility;

        [Header("Test Config")]
        public bool RunAutoTest = true;

        private CharacterEntity _testPlayer;
        private CharacterEntity _testEnemy;

        private void Start()
        {
            // Ensure CombatManager exists
            if (CombatManager.Instance == null)
            {
                var cmGO = new GameObject("CombatManager");
                cmGO.AddComponent<CombatManager>();
            }

            // Subscribe to events for console logging
            CombatEvents.OnDamageDealt     += LogDamage;
            CombatEvents.OnHealingDealt    += LogHealing;
            CombatEvents.OnCharacterDied   += LogDeath;
            CombatEvents.OnThreatGenerated += LogThreat;
            CombatEvents.OnResourceChanged += LogResource;

            if (RunAutoTest) RunTest();
        }

        private void OnDestroy()
        {
            CombatEvents.OnDamageDealt     -= LogDamage;
            CombatEvents.OnHealingDealt    -= LogHealing;
            CombatEvents.OnCharacterDied   -= LogDeath;
            CombatEvents.OnThreatGenerated -= LogThreat;
            CombatEvents.OnResourceChanged -= LogResource;
        }

        private void RunTest()
        {
            if (PlayerPrefab == null || EnemyPrefab == null || IroncladDefinition == null)
            {
                Debug.LogWarning("[Bootstrap] Assign PlayerPrefab, EnemyPrefab, and IroncladDefinition.");
                return;
            }

            // Spawn player
            _testPlayer = CharacterFactory.SpawnPlayer(
                prefab:    PlayerPrefab,
                classDef:  IroncladDefinition,
                level:     10,
                position:  Vector3.zero,
                playerName: "TestIronclad"
            );

            // Manually assign Shield Slam to slot 0 if no default loadout
            if (ShieldSlamAbility != null)
            {
                var abilitySystem = _testPlayer.GetComponent<AbilitySystem>();
                abilitySystem.EquipAbility(0, ShieldSlamAbility);
            }

            // Spawn enemy 3m away
            _testEnemy = CharacterFactory.SpawnEnemy(
                prefab:    EnemyPrefab,
                position:  new Vector3(2f, 0f, 0f),
                enemyName: "TestDummy"
            );

            if (_testPlayer == null || _testEnemy == null)
            {
                Debug.LogError("[Bootstrap] Spawn failed. Check prefab components.");
                return;
            }

            // Give enemy some HP for the test
            _testEnemy.Stats.SetBase(StatType.MaxHealth, 500f);
            _testEnemy.Stats.SetBase(StatType.Armor, 300f);
            _testEnemy.Resources.FullReset();

            // Give player some Rage to spend
            _testPlayer.Resources.GenerateRage(100f);

            Debug.Log($"[Bootstrap] Setup complete. " +
                      $"Player HP: {_testPlayer.CurrentHP}/{_testPlayer.MaxHP}  " +
                      $"Enemy HP: {_testEnemy.CurrentHP}/{_testEnemy.MaxHP}");

            // Attempt to cast Shield Slam
            var system = _testPlayer.GetComponent<AbilitySystem>();
            var reason = system.TryCastAbility(0, _testEnemy);

            Debug.Log(reason == CastBlockReason.None
                ? "[Bootstrap] Shield Slam cast successfully!"
                : $"[Bootstrap] Shield Slam blocked: {reason}");
        }

        // ── Event log handlers ───────────────────────────────

        private void LogDamage(DamageInfo info)
        {
            Debug.Log($"[DAMAGE] {info.Source?.CharacterName} → {info.Target?.CharacterName} " +
                      $"| {info.AbilityName} | {info.FinalAmount:F1} {info.DamageType} " +
                      $"({info.HitResult}) | DoT:{info.IsDoT}");
        }

        private void LogHealing(HealInfo info)
        {
            Debug.Log($"[HEAL] {info.Source?.CharacterName} → {info.Target?.CharacterName} " +
                      $"| {info.AbilityName} | +{info.FinalAmount:F1} ({info.HitResult})");
        }

        private void LogDeath(CharacterDeathInfo info)
        {
            Debug.Log($"[DEATH] {info.Deceased?.CharacterName} was slain by " +
                      $"{info.Killer?.CharacterName ?? "environment"}");
        }

        private void LogThreat(ThreatInfo info)
        {
            if (info.Target == null) return;
            Debug.Log($"[THREAT] {info.Source?.CharacterName} → {info.Target?.CharacterName} " +
                      $"+{info.Amount:F1} ({info.Reason})");
        }

        private void LogResource(ResourceChangeInfo info)
        {
            Debug.Log($"[RESOURCE] {info.Owner?.CharacterName} {info.ResourceType} " +
                      $"{info.NewValue:F0}/{info.MaxValue:F0} (Δ{info.Delta:+0.#;-0.#})");
        }
    }
}
