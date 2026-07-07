// ============================================================
//  DataObjects.cs
//  ScriptableObject definitions for ClassDefinition and Item.
//  These are the two most-used data containers in the game.
//
//  ClassDefinitionSO:
//  • Defines a class's base stats, resource type, role,
//    and default ability loadout.
//  • Referenced by CharacterEntity at spawn to initialise
//    the StatSheet and AbilitySystem.
//
//  ItemSO:
//  • Defines everything about one piece of equipment.
//  • Applied to StatSheet via InventorySystem (not yet built).
//  • Set bonus system is data-driven: same SetId + count check.
// ============================================================

using System.Collections.Generic;
using UnityEngine;

namespace ShatteredVaults
{
    // ══════════════════════════════════════════════════════════
    //  CLASS DEFINITION
    // ══════════════════════════════════════════════════════════

    [CreateAssetMenu(menuName = "ShatteredVaults/Data/ClassDefinition")]
    public class ClassDefinitionSO : ScriptableObject
    {
        // ── Identity ─────────────────────────────────────────
        [Header("Identity")]
        public ClassType      ClassType;
        public CharacterRole  Role;
        public string         ClassName;
        public string         Lore;
        public Sprite         ClassIcon;

        // ── Resource ─────────────────────────────────────────
        [Header("Resource")]
        public ResourceType   PrimaryResource;
        public float          BaseMaxResource = 100f;

        // ── Primary and secondary stats ───────────────────────
        [Header("Scaling")]
        public StatType       PrimaryStat;
        public StatType       SecondaryStat;

        // ── Base stat values at level 1 ───────────────────────
        [Header("Base Stats at Level 1")]
        public float BaseStrength     = 10f;
        public float BaseAgility      = 10f;
        public float BaseIntelligence = 10f;
        public float BaseVitality     = 10f;
        public float BaseCritStrike   = 0.05f;  // 5% base crit
        public float BaseHaste        = 0f;
        public float BaseMastery      = 0f;
        public float BaseMaxHealth    = 100f;
        public float BaseArmor        = 0f;

        // ── Per-level stat growth ─────────────────────────────
        [Header("Stat Growth Per Level")]
        public float PrimaryStatGrowth   = 3f;   // added to primary stat each level
        public float VitalityGrowth      = 2f;
        public float SecondaryStatGrowth = 1.5f;

        // ── Default ability loadout ───────────────────────────
        [Header("Starting Abilities (slots 0–9)")]
        public AbilityBase[] DefaultAbilities = new AbilityBase[10];

        // ─────────────────────────────────────────────────────
        //  Runtime helpers
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Apply this class definition to a character's StatSheet.
        /// Call once at spawn, then again each level up.
        /// </summary>
        public void ApplyToStatSheet(StatSheet sheet, int level)
        {
            float lvl = Mathf.Max(1, level);

            var stats = new Dictionary<StatType, float>
            {
                [StatType.Strength]       = BaseStrength,
                [StatType.Agility]        = BaseAgility,
                [StatType.Intelligence]   = BaseIntelligence,
                [StatType.Vitality]       = BaseVitality + VitalityGrowth * (lvl - 1),
                [StatType.CriticalStrike] = BaseCritStrike,
                [StatType.Haste]          = BaseHaste,
                [StatType.Mastery]        = BaseMastery,
                [StatType.MaxHealth]      = BaseMaxHealth,
                [StatType.Armor]          = BaseArmor,
                [StatType.MaxResource]    = BaseMaxResource,
            };

            // Scale primary stat by level
            stats[PrimaryStat]   += PrimaryStatGrowth   * (lvl - 1);
            stats[SecondaryStat] += SecondaryStatGrowth * (lvl - 1);

            sheet.SetBaseBatch(stats);
        }

        /// <summary>
        /// Apply the default ability loadout to an AbilitySystem.
        /// </summary>
        public void ApplyLoadout(AbilitySystem abilitySystem)
        {
            for (int i = 0; i < Mathf.Min(DefaultAbilities.Length, AbilitySystem.MaxAbilitySlots); i++)
                if (DefaultAbilities[i] != null)
                    abilitySystem.EquipAbility(i, DefaultAbilities[i]);
        }
    }

    // ══════════════════════════════════════════════════════════
    //  ITEM
    // ══════════════════════════════════════════════════════════

    public enum ItemSlot
    {
        Helm, Shoulders, Chest, Gloves, Belt,
        Legs, Boots, MainHand, OffHand,
        Ring1, Ring2, Necklace, Trinket1, Trinket2
    }

    public enum ItemRarity
    {
        Common, Uncommon, Rare, Epic, Legendary
    }

    [System.Serializable]
    public class ItemStatEntry
    {
        public StatType    Stat;
        public float       Value;
        public ModifierType ModifierType = ModifierType.Flat;
    }

    [System.Serializable]
    public class SetBonusEntry
    {
        public int    RequiredPieces;
        [TextArea(2, 4)]
        public string BonusDescription;
        // In a full implementation, this would reference an effect or modifier list
        public List<StatModifier> BonusModifiers = new();
    }

    [CreateAssetMenu(menuName = "ShatteredVaults/Data/Item")]
    public class ItemSO : ScriptableObject
    {
        // ── Identity ─────────────────────────────────────────
        [Header("Identity")]
        public string       ItemName;
        [TextArea(2, 4)]
        public string       FlavorText;
        public Sprite       Icon;
        public ItemSlot     Slot;
        public ItemRarity   Rarity;
        public int          ItemLevel;

        // ── Class restriction ─────────────────────────────────
        [Header("Restriction")]
        public bool         AnyClass = true;
        public ClassType    RequiredClass;

        // ── Stats ─────────────────────────────────────────────
        [Header("Stats")]
        public List<ItemStatEntry> StatModifiers = new();

        // ── Passive effect ────────────────────────────────────
        [Header("Passive Effect")]
        [TextArea(2, 5)]
        public string       PassiveDescription;
        public string       PassiveEffectId;    // maps to a PassiveEffectRegistry entry

        // ── Ability enhancement ───────────────────────────────
        [Header("Ability Bonus")]
        [TextArea(2, 4)]
        public string       AbilityBonusDescription;
        public string       TargetAbilityName;  // name of the ability this modifies
        public AbilityModifier AbilityModifier; // applied to that ability's runtime modifiers

        // ── Set bonus ─────────────────────────────────────────
        [Header("Set Bonus")]
        public string       SetId;              // e.g. "blizzard_regent_stormcaller"
        public string       SetName;
        public List<SetBonusEntry> SetBonuses = new();

        // ── Source ────────────────────────────────────────────
        [Header("Source")]
        public string       DungeonSource;      // which dungeon drops this
        public string       BossSource;         // which boss

        // ─────────────────────────────────────────────────────
        //  Runtime helpers
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Build the list of StatModifiers to pass to StatSheet.
        /// Called by InventorySystem when equipping.
        /// </summary>
        public List<StatModifier> BuildStatModifiers()
        {
            var result = new List<StatModifier>();
            foreach (var entry in StatModifiers)
            {
                result.Add(new StatModifier(
                    entry.Stat,
                    entry.ModifierType,
                    entry.Value,
                    ModifierSource.Gear,
                    GetSourceId()
                ));
            }
            return result;
        }

        /// <summary>Unique source ID used by StatSheet to remove this item's modifiers.</summary>
        public string GetSourceId() => $"item_{name}_{Slot}";

        public bool CanEquip(ClassType classType) =>
            AnyClass || RequiredClass == classType;
    }
}
