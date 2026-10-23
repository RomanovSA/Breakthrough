using UnityEngine;

/// <summary>
/// Armor (a data asset: Assets > Create > Game > Armor). It goes into the Armor slot, and every hit the unit takes does that much less
/// damage (a hit always does at least 1).
/// </summary>
[CreateAssetMenu(menuName = "Game/Armor", fileName = "Armor_New")]
public class ArmorData : ItemData
{
    [Tooltip("How much damage the armor takes off every hit (a bullet of 10 damage does 8 to a unit whose armor says 2).")]
    [SerializeField, Min(0)] private int damageReduction = 2;

    public int DamageReduction => damageReduction;

    public override ItemSlots FitsSlots => ItemSlots.Armor;
}
