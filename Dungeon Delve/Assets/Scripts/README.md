# Shattered Vaults — Combat System
## Phase 1 Deliverable

---

## Files Delivered

```
Scripts/
├── Core/
│   ├── Enums.cs                   Central enum definitions (StatType, DamageType, etc.)
│   ├── CombatEvents.cs            Static event bus — all systems communicate here
│   └── CombatSystemBootstrap.cs   Dev-only scene wiring & integration test
│
├── Characters/
│   ├── CharacterEntity.cs         Root MonoBehaviour for every player and enemy
│   ├── StatSheet.cs               Stat calculation (flat → %add → %mult, DR, caps)
│   ├── ResourceSystem.cs          HP, resource (mana/rage/energy/focus), regen, death
│   └── StatusEffectHandler.cs     Buffs, debuffs, DoTs, HoTs, CC flags
│
├── Abilities/
│   ├── AbilityBase.cs             Abstract ScriptableObject all abilities inherit from
│   ├── AbilitySystem.cs           Loadout (10 slots), cooldowns, GCD, cast bar
│   └── ConcreteAbilities.cs       3 implemented examples: ShieldSlam, CascadeHeal, VoidStrike
│
├── Combat/
│   ├── DamageCalculator.cs        All damage/healing math (crit, armor, DR, scaling)
│   ├── ThreatTable.cs             Per-enemy threat tracking and taunt system
│   └── CombatManager.cs           Scene coordinator + CharacterFactory spawn helper
│
└── Data/
    └── DataObjects.cs             ClassDefinitionSO and ItemSO ScriptableObjects
```

---

## Required Components Per Prefab

### Player Prefab
```
CharacterEntity       (identity, passthrough access)
StatSheet             (stat calculation)
ResourceSystem        (HP + resource)
StatusEffectHandler   (buffs/debuffs/CC)
AbilitySystem         (loadout + cooldowns)
```

### Enemy Prefab
```
CharacterEntity       (IsPlayer = false)
StatSheet
ResourceSystem
StatusEffectHandler
ThreatTable           (enemies only — players don't have this)
```

---

## Setup Instructions

### 1. Import the scripts
Drop the entire `Scripts/` folder into your Unity project under `Assets/_Project/Scripts/`.

### 2. Create prefabs
Create a Player prefab and Enemy prefab with the components listed above.
Set `CharacterEntity.IsPlayer = true` on the player prefab.

### 3. Create a ClassDefinitionSO
- Right-click in Project → Create → ShatteredVaults → Data → ClassDefinition
- Fill in base stats, resource type, and drag ability SOs into the loadout slots.

### 4. Create ability SOs
- Right-click → Create → ShatteredVaults → Abilities → Ironclad → ShieldSlam
- Tweak values in the Inspector — no code changes needed.

### 5. Wire up the Bootstrap (test scene only)
- Add a GameObject, attach `CombatSystemBootstrap`
- Assign PlayerPrefab, EnemyPrefab, IroncladDefinition, ShieldSlamAbility
- Press Play — watch the Console for combat event logs

---

## Architecture Decisions

### Why a static event bus (CombatEvents)?
- Zero coupling between systems. UI, net layer, and audio all subscribe without knowing each other.
- When adding NGO networking, the net layer just listens to CombatEvents and sends RPCs — no refactoring.
- In a multiplayer build, fire events only after server-authoritative validation.

### Why ScriptableObjects for abilities?
- New abilities = new SO asset, not new code.
- Multiple casters share one SO; per-caster state lives in `AbilityCooldownState` in AbilitySystem.
- SOs are never mutated at runtime (RuntimeModifiers is marked [NonSerialized]).

### Why three-layer stat modifiers?
- Prevents the classic problem where stacking the same type of bonus gives diminishing returns unexpectedly.
- Flat (gear stats) → PercentAdd pool (talents) → PercentMult (rare powerful effects).
- Matches the modifier model used by Path of Exile, WoW, and Diablo 3 for predictable scaling.

### Why is DamageCalculator static?
- Pure functions with no state are trivially testable and thread-safe.
- Easy to unit test: pass in mock CharacterEntity data, assert output.

---

## Multiplayer Migration Path (when ready)

1. `CharacterEntity` → extend `NetworkBehaviour` (NGO)
2. `ResourceSystem` → mark `_currentHealth` and `_currentResource` as `NetworkVariable<float>`
3. `AbilitySystem.TryCastAbility()` → wrap in `[ServerRpc]`; clients call locally, server validates and executes
4. `CombatEvents` → server fires events; use `ClientRpc` to broadcast VFX/audio to clients
5. `CombatManager` → server-only authority; `CharacterFactory.Spawn()` → `NetworkObject.Spawn()`

No structural refactoring required — the seams are already clean.

---

## What's Next

| System | Description |
|--------|-------------|
| `InventorySystem.cs` | Equip/unequip items, apply ItemSO modifiers to StatSheet |
| `TalentSystem.cs` | Load talent selections, inject AbilityModifiers at runtime |
| `EnemySO + EnemyAI.cs` | Data-driven enemy definition + behaviour tree |
| `BossController.cs` | Phase-based boss state machine (per GDD) |
| `DungeonManager.cs` | Scene loading, encounter sequencing, loot drop trigger |
| `HubManager.cs` | Party formation, dungeon selection, character management |
| `UIHUD.cs` | Health bars, resource bars, ability cooldown display |
