using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What a unit carries. Six slots:
///  - Primary and Secondary: weapons (WeaponData that fit them);
///  - Armor: ArmorData, which takes off a fixed amount of damage from every hit;
///  - Vest (разгрузка): VestData, with a number of pockets (capacity) for small things;
///  - Extra: unique armament (any item that fits the Extra slot, a unique weapon for instance);
///  - Backpack: BackpackData, with more room than a vest.
/// The weapon in the unit's hands is the one in the active weapon slot (Primary, Secondary or Extra); switching to another weapon that
/// the unit carries costs Switch Cost action points. Whatever is put in the slots below Starting Equipment is what the unit begins with
/// (a unit with only a weapon set on the Unit itself carries it as its primary).
/// The inventory is added to every unit by itself; put it on the unit's prefab to give the units their equipment in the Inspector.
/// Equip and Unequip check that the item fits (kind and slot) and that a vest or a backpack is not made too small for what is in it.
/// </summary>
[DisallowMultipleComponent]
public class UnitInventory : MonoBehaviour
{
    [Header("Starting equipment")]
    [SerializeField] private ItemData primary;
    [SerializeField] private ItemData secondary;
    [SerializeField] private ItemData armor;
    [SerializeField] private ItemData vest;
    [SerializeField] private ItemData extra;
    [SerializeField] private ItemData backpack;
    [Tooltip("Things in the pockets of the vest at the start (as many as fit).")]
    [SerializeField] private List<ItemData> vestContents = new List<ItemData>();
    [Tooltip("Things in the backpack at the start (as many as fit).")]
    [SerializeField] private List<ItemData> backpackContents = new List<ItemData>();

    [Header("Weapons")]
    [Tooltip("The weapon slot the unit holds its weapon from at the start.")]
    [SerializeField] private InventorySlot activeWeaponSlot = InventorySlot.Primary;
    [Tooltip("Action points it costs to take another weapon into the hands.")]
    [SerializeField, Min(0)] private int switchCost = 1;

    private Unit unit;
    private bool initialised;

    /// <summary>Raised when something in the inventory changed: an item put in or taken out, the weapon in the hands switched.</summary>
    public event Action Changed;

    /// <summary>Called by the unit when it wakes up: puts the starting equipment in place and puts the right weapon into the hands.</summary>
    public void Initialize(Unit owner)
    {
        unit = owner;
        if (initialised)
            return;
        initialised = true;

        // a unit that was only given a weapon on the Unit carries it as its primary
        if (primary == null && owner.Weapon != null)
            primary = owner.Weapon;

        // starting equipment that does not fit its slot is dropped (with a warning)
        foreach (InventorySlot slot in InventorySlots.All)
        {
            string reason;
            ItemData item = Raw(slot);
            if (item != null && !CheckFits(slot, item, out reason))
            {
                Debug.LogWarning("UnitInventory: " + item.name + " does not fit the slot '" + InventorySlots.Label(slot) + "' of " + owner.name + ": " + reason, this);
                SetRaw(slot, null);
            }
        }

        TrimContents(vestContents, VestCapacity);
        TrimContents(backpackContents, BackpackCapacity);

        // the weapon in the hands
        if (!(Raw(activeWeaponSlot) is WeaponData))
            activeWeaponSlot = FirstWeaponSlot(InventorySlot.Primary);
        WeaponData held = Raw(activeWeaponSlot) as WeaponData;
        if (held != owner.Weapon)
            owner.ApplyWeapon(held);
    }

    // ---- the slots ----

    private ItemData Raw(InventorySlot slot)
    {
        switch (slot)
        {
            case InventorySlot.Primary: return primary;
            case InventorySlot.Secondary: return secondary;
            case InventorySlot.Armor: return armor;
            case InventorySlot.Vest: return vest;
            case InventorySlot.Extra: return extra;
            default: return backpack;
        }
    }

    private void SetRaw(InventorySlot slot, ItemData item)
    {
        switch (slot)
        {
            case InventorySlot.Primary: primary = item; break;
            case InventorySlot.Secondary: secondary = item; break;
            case InventorySlot.Armor: armor = item; break;
            case InventorySlot.Vest: vest = item; break;
            case InventorySlot.Extra: extra = item; break;
            default: backpack = item; break;
        }
    }

    /// <summary>What is in the slot (null if it is empty).</summary>
    public ItemData Get(InventorySlot slot)
    {
        return Raw(slot);
    }

    /// <summary>
    /// Whether the item can be put into the slot: it must fit that slot and be of the right kind (a weapon for a weapon slot, armor for
    /// the armor slot, a vest, a backpack). The reason is filled in when it cannot.
    /// </summary>
    public static bool CheckFits(InventorySlot slot, ItemData item, out string reason)
    {
        reason = null;
        if (item == null)
            return true;

        if (!item.FitsSlot(slot))
        {
            reason = item.DisplayName + " does not fit the slot '" + InventorySlots.Label(slot) + "'";
            return false;
        }

        bool kindOk;
        switch (slot)
        {
            case InventorySlot.Primary:
            case InventorySlot.Secondary:
                kindOk = item is WeaponData;
                break;
            case InventorySlot.Armor:
                kindOk = item is ArmorData;
                break;
            case InventorySlot.Vest:
                kindOk = item is VestData;
                break;
            case InventorySlot.Backpack:
                kindOk = item is BackpackData;
                break;
            default:
                kindOk = true;                          // the extra place takes any item that fits it
                break;
        }

        if (!kindOk)
            reason = item.DisplayName + " is not the right kind of item for the slot '" + InventorySlots.Label(slot) + "'";
        return kindOk;
    }

    /// <summary>Whether Equip(slot, item) would work (item null = take what is in the slot out).</summary>
    public bool CanEquip(InventorySlot slot, ItemData item, out string reason)
    {
        if (!CheckFits(slot, item, out reason))
            return false;

        // a vest or a backpack cannot be taken off, or changed for a smaller one, while its pockets hold more than the new one has room for
        if (slot == InventorySlot.Vest && vestContents.Count > CapacityOf(item))
        {
            reason = "the pockets of the vest are not empty enough";
            return false;
        }
        if (slot == InventorySlot.Backpack && backpackContents.Count > CapacityOf(item))
        {
            reason = "the backpack is not empty enough";
            return false;
        }

        reason = null;
        return true;
    }

    /// <summary>
    /// Puts the item into the slot (the item that was there is taken out and is gone from the inventory: the caller keeps it if need be),
    /// or empties the slot with null. Equipping a weapon into the slot the weapon in the hands comes from changes the weapon in the hands
    /// (free of charge); emptying that slot puts another weapon into the hands. Returns false if it does not fit.
    /// </summary>
    public bool Equip(InventorySlot slot, ItemData item)
    {
        string reason;
        if (!CanEquip(slot, item, out reason))
        {
            Debug.Log("UnitInventory: cannot equip: " + reason, this);
            return false;
        }

        SetRaw(slot, item);

        if (InventorySlots.IsWeaponSlot(slot))
        {
            if (slot == activeWeaponSlot)
                SelectWeaponSlot(Raw(slot) is WeaponData ? slot : FirstWeaponSlot(slot), false);       // the weapon in the hands was changed, or taken out: another one takes its place
            else if (!(Raw(activeWeaponSlot) is WeaponData))
                SelectWeaponSlot(slot, false);                                                      // the hands were empty, and now there is a weapon
        }

        Changed?.Invoke();
        return true;
    }

    /// <summary>Takes out and returns what is in the slot (null if it was empty or cannot be taken out now).</summary>
    public ItemData Unequip(InventorySlot slot)
    {
        ItemData item = Raw(slot);
        if (item == null || !Equip(slot, null))
            return null;
        return item;
    }

    // ---- the pockets ----

    private static int CapacityOf(ItemData container)
    {
        VestData v = container as VestData;
        if (v != null)
            return v.Capacity;
        BackpackData b = container as BackpackData;
        return b != null ? b.Capacity : 0;
    }

    private static void TrimContents(List<ItemData> list, int capacity)
    {
        list.RemoveAll(i => i == null || !i.Carriable);
        if (list.Count > capacity)
            list.RemoveRange(capacity, list.Count - capacity);
    }

    /// <summary>How many things the vest in the Vest slot has room for (0 without a vest).</summary>
    public int VestCapacity => CapacityOf(vest);

    /// <summary>How many things the backpack has room for (0 without a backpack).</summary>
    public int BackpackCapacity => CapacityOf(backpack);

    /// <summary>What is in the pockets of the vest.</summary>
    public IReadOnlyList<ItemData> VestItems => vestContents;

    /// <summary>What is in the backpack.</summary>
    public IReadOnlyList<ItemData> BackpackItems => backpackContents;

    /// <summary>Puts a thing into the pockets of the vest (false if there is no vest, no room, or the thing cannot be carried).</summary>
    public bool TryAddToVest(ItemData item)
    {
        if (item == null || !item.Carriable || vestContents.Count >= VestCapacity)
            return false;
        vestContents.Add(item);
        Changed?.Invoke();
        return true;
    }

    /// <summary>Puts a thing into the backpack (false if there is no backpack, no room, or the thing cannot be carried).</summary>
    public bool TryAddToBackpack(ItemData item)
    {
        if (item == null || !item.Carriable || backpackContents.Count >= BackpackCapacity)
            return false;
        backpackContents.Add(item);
        Changed?.Invoke();
        return true;
    }

    /// <summary>Takes a thing out of the vest (false if it is not there).</summary>
    public bool RemoveFromVest(ItemData item)
    {
        bool removed = vestContents.Remove(item);
        if (removed)
            Changed?.Invoke();
        return removed;
    }

    /// <summary>Takes a thing out of the backpack (false if it is not there).</summary>
    public bool RemoveFromBackpack(ItemData item)
    {
        bool removed = backpackContents.Remove(item);
        if (removed)
            Changed?.Invoke();
        return removed;
    }

    // ---- armor ----

    /// <summary>How much damage the armor takes off every hit (0 without armor).</summary>
    public int DamageReduction
    {
        get
        {
            ArmorData a = armor as ArmorData;
            return a != null ? a.DamageReduction : 0;
        }
    }

    // ---- the weapon in the hands ----

    /// <summary>The weapon slot the weapon in the hands comes from.</summary>
    public InventorySlot ActiveWeaponSlot => activeWeaponSlot;

    /// <summary>The weapon in the hands (null if the unit carries none).</summary>
    public WeaponData ActiveWeapon => Raw(activeWeaponSlot) as WeaponData;

    /// <summary>Action points it costs to take another weapon.</summary>
    public int SwitchCost => switchCost;

    private static readonly InventorySlot[] weaponOrder = { InventorySlot.Primary, InventorySlot.Secondary, InventorySlot.Extra };

    // The first weapon slot that holds a weapon, looking from the given one on (round and round).
    private InventorySlot FirstWeaponSlot(InventorySlot from)
    {
        int start = Array.IndexOf(weaponOrder, from);
        if (start < 0)
            start = 0;
        for (int i = 0; i < weaponOrder.Length; i++)
        {
            InventorySlot s = weaponOrder[(start + i) % weaponOrder.Length];
            if (Raw(s) is WeaponData)
                return s;
        }
        return from;
    }

    /// <summary>The weapon slot that would be taken next (null if there is no other weapon to take).</summary>
    public InventorySlot? NextWeaponSlot()
    {
        int start = Array.IndexOf(weaponOrder, activeWeaponSlot);
        for (int i = 1; i < weaponOrder.Length; i++)
        {
            InventorySlot s = weaponOrder[(start + i) % weaponOrder.Length];
            if (Raw(s) is WeaponData)
                return s;
        }
        return null;
    }

    /// <summary>True if the unit carries another weapon than the one in its hands.</summary>
    public bool HasOtherWeapon => NextWeaponSlot().HasValue;

    /// <summary>True if the unit can take another weapon now: it carries one, it is not walking and it has the points.</summary>
    public bool CanSwitchWeapon => unit != null && unit.IsAlive && !unit.IsMoving && HasOtherWeapon && unit.ActionPoints >= switchCost;

    /// <summary>Takes the next weapon the unit carries into its hands for Switch Cost points. Returns false if it cannot.</summary>
    public bool SwitchWeapon()
    {
        if (!CanSwitchWeapon)
            return false;

        InventorySlot next = NextWeaponSlot().Value;
        if (!unit.TrySpendActionPoints(switchCost))
            return false;

        SelectWeaponSlot(next, true);
        return true;
    }

    // Puts the weapon of the slot into the unit's hands.
    private void SelectWeaponSlot(InventorySlot slot, bool notify)
    {
        activeWeaponSlot = slot;
        WeaponData held = Raw(slot) as WeaponData;
        if (unit != null && held != unit.Weapon)
            unit.ApplyWeapon(held);
        if (notify)
            Changed?.Invoke();
    }

    /// <summary>Makes the weapon of this slot the one in the hands, free of charge (for setting a unit up; a switch in the game is SwitchWeapon).</summary>
    public bool SelectWeapon(InventorySlot slot)
    {
        if (!InventorySlots.IsWeaponSlot(slot) || !(Raw(slot) is WeaponData))
            return false;
        SelectWeaponSlot(slot, true);
        return true;
    }
}
