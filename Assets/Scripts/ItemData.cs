using System;
using UnityEngine;

/// <summary>The slots of a unit's inventory.</summary>
public enum InventorySlot
{
    /// <summary>The main weapon.</summary>
    Primary,

    /// <summary>The second weapon.</summary>
    Secondary,

    /// <summary>Armor (it takes some of the damage of every hit).</summary>
    Armor,

    /// <summary>The load-bearing vest (разгрузка): it has pockets for small things within easy reach.</summary>
    Vest,

    /// <summary>The extra place, for unique armament that does not fit the other slots.</summary>
    Extra,

    /// <summary>The backpack: it has room for more things.</summary>
    Backpack
}

/// <summary>Which slots an item fits into (one item can fit more than one; None = it only goes into the pockets of the vest and the backpack).</summary>
[Flags]
public enum ItemSlots
{
    None = 0,
    Primary = 1,
    Secondary = 2,
    Armor = 4,
    Vest = 8,
    Extra = 16,
    Backpack = 32
}

/// <summary>Names and helpers for the six slots.</summary>
public static class InventorySlots
{
    /// <summary>All six slots, in the order they are shown.</summary>
    public static readonly InventorySlot[] All =
    {
        InventorySlot.Primary, InventorySlot.Secondary, InventorySlot.Armor,
        InventorySlot.Vest, InventorySlot.Extra, InventorySlot.Backpack
    };

    /// <summary>The slot as a flag, to be checked against what an item fits.</summary>
    public static ItemSlots Flag(InventorySlot slot)
    {
        switch (slot)
        {
            case InventorySlot.Primary: return ItemSlots.Primary;
            case InventorySlot.Secondary: return ItemSlots.Secondary;
            case InventorySlot.Armor: return ItemSlots.Armor;
            case InventorySlot.Vest: return ItemSlots.Vest;
            case InventorySlot.Extra: return ItemSlots.Extra;
            default: return ItemSlots.Backpack;
        }
    }

    /// <summary>The name shown to the player.</summary>
    public static string Label(InventorySlot slot)
    {
        switch (slot)
        {
            case InventorySlot.Primary: return "Первичное оружие";
            case InventorySlot.Secondary: return "Вторичное оружие";
            case InventorySlot.Armor: return "Броня";
            case InventorySlot.Vest: return "Разгрузка";
            case InventorySlot.Extra: return "Экстра-место";
            default: return "Рюкзак";
        }
    }

    /// <summary>True for the slots a weapon can be held from (the ones a unit can switch between).</summary>
    public static bool IsWeaponSlot(InventorySlot slot)
    {
        return slot == InventorySlot.Primary || slot == InventorySlot.Secondary || slot == InventorySlot.Extra;
    }
}

/// <summary>
/// Anything a unit can carry (a data asset: Assets > Create > Game > Item). On its own it is a small thing for the pockets of the vest
/// and the backpack (ammunition, a medkit...); the kinds with a slot of their own derive from it: WeaponData, ArmorData, VestData,
/// BackpackData. 'Fits Slots' says which slots it can be put into; a unique piece of armament is an item that fits the Extra slot.
/// </summary>
[CreateAssetMenu(menuName = "Game/Item", fileName = "Item_New")]
public class ItemData : ScriptableObject
{
    [SerializeField] private string displayName = "Item";
    [SerializeField, TextArea(1, 4)] private string description;
    [SerializeField] private Sprite icon;
    [Tooltip("The slots this item can be put into (none = it only goes into the pockets of a vest or a backpack).")]
    [SerializeField] private ItemSlots fitsSlots = ItemSlots.None;
    [Tooltip("Whether it can be carried in the vest or the backpack.")]
    [SerializeField] private bool carriable = true;

    public string DisplayName => displayName;
    public string Description => description;
    public Sprite Icon => icon;

    /// <summary>The slots it fits (the kinds with a slot of their own say so themselves).</summary>
    public virtual ItemSlots FitsSlots => fitsSlots;

    /// <summary>Whether it can go into a pocket of the vest or the backpack.</summary>
    public bool Carriable => carriable;

    /// <summary>True if the item can be put into the given slot (the kind of the item is checked by the inventory).</summary>
    public bool FitsSlot(InventorySlot slot)
    {
        return (FitsSlots & InventorySlots.Flag(slot)) != 0;
    }
}
