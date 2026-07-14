using System.Collections.Generic;
using UnityEngine;

public enum AttributeType
{
    Strength,
    Agility,
    Critical,
    Haste,
    Mastery,
    Intelligence,
    Wisdom,
    Charm
}

public enum PlayerClass
{
    Warrior,
    Mage
}

public class PlayerAttributes : MonoBehaviour
{
    [Header("Class & Level")]
    [SerializeField] private PlayerClass playerClass = PlayerClass.Warrior;
    [SerializeField] private int level = 1;

    [Header("Base Attributes (level 1, no gear)")]
    [SerializeField] private float baseStrength = 5f;
    [SerializeField] private float baseAgility = 5f;
    [SerializeField] private float baseCritical = 5f;
    [SerializeField] private float baseHaste = 5f;
    [SerializeField] private float baseMastery = 5f;
    [SerializeField] private float baseIntelligence = 5f;
    [SerializeField] private float baseWisdom = 5f;
    [SerializeField] private float baseCharm = 5f;

    // Unity's Inspector can't display a Dictionary, so per-class growth
    // lives here in code instead of as a [SerializeField]. Tune the
    // numbers directly; add a new PlayerClass entry to support more classes.
    private static readonly Dictionary<PlayerClass, Dictionary<AttributeType, float>> GrowthPerLevel = new()
    {
        {
            PlayerClass.Warrior, new Dictionary<AttributeType, float>
            {
                { AttributeType.Strength, 2f },
                { AttributeType.Agility, 1f },
                { AttributeType.Critical, 0.5f },
                { AttributeType.Haste, 0.5f },
                { AttributeType.Mastery, 1f },
                { AttributeType.Intelligence, 0.2f },
                { AttributeType.Wisdom, 0.2f },
                { AttributeType.Charm, 0.2f },
            }
        },
        {
            PlayerClass.Mage, new Dictionary<AttributeType, float>
            {
                { AttributeType.Strength, 0.2f },
                { AttributeType.Agility, 0.5f },
                { AttributeType.Critical, 1f },
                { AttributeType.Haste, 1f },
                { AttributeType.Mastery, 1.5f },
                { AttributeType.Intelligence, 2f },
                { AttributeType.Wisdom, 1.5f },
                { AttributeType.Charm, 0.5f },
            }
        },
    };

    // sourceId -> (attribute -> bonus). Keying by sourceId lets an
    // equipment system add several attribute bonuses for one item and
    // remove all of them in one call when that item is unequipped,
    // without touching bonuses granted by other equipped items.
    private readonly Dictionary<string, Dictionary<AttributeType, float>> equipmentBonuses = new();

    public PlayerClass Class => playerClass;
    public int Level => level;

    public float GetTotal(AttributeType attribute)
    {
        float total = GetBaseValue(attribute);

        if (GrowthPerLevel.TryGetValue(playerClass, out var growth) &&
            growth.TryGetValue(attribute, out var perLevel))
        {
            total += perLevel * (level - 1);
        }

        foreach (var bonusSet in equipmentBonuses.Values)
        {
            if (bonusSet.TryGetValue(attribute, out var bonus))
                total += bonus;
        }

        return total;
    }

    public void LevelUp()
    {
        level++;
    }

    /// <summary>Called by equipment code when an item is equipped.</summary>
    public void EquipBonus(string sourceId, AttributeType attribute, float amount)
    {
        if (!equipmentBonuses.TryGetValue(sourceId, out var bonusSet))
        {
            bonusSet = new Dictionary<AttributeType, float>();
            equipmentBonuses[sourceId] = bonusSet;
        }

        bonusSet[attribute] = bonusSet.TryGetValue(attribute, out var existing)
            ? existing + amount
            : amount;
    }

    /// <summary>Called by equipment code when an item is unequipped; removes every bonus that item granted.</summary>
    public void RemoveBonuses(string sourceId)
    {
        equipmentBonuses.Remove(sourceId);
    }

    private float GetBaseValue(AttributeType attribute)
    {
        switch (attribute)
        {
            case AttributeType.Strength: return baseStrength;
            case AttributeType.Agility: return baseAgility;
            case AttributeType.Critical: return baseCritical;
            case AttributeType.Haste: return baseHaste;
            case AttributeType.Mastery: return baseMastery;
            case AttributeType.Intelligence: return baseIntelligence;
            case AttributeType.Wisdom: return baseWisdom;
            case AttributeType.Charm: return baseCharm;
            default: return 0f;
        }
    }
}
