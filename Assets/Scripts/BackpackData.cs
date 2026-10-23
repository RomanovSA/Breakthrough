using UnityEngine;

/// <summary>
/// A backpack (a data asset: Assets > Create > Game > Backpack). It goes into the Backpack slot and has room (capacity) for more things
/// than a vest has.
/// </summary>
[CreateAssetMenu(menuName = "Game/Backpack", fileName = "Backpack_New")]
public class BackpackData : ItemData
{
    [Tooltip("How many things fit into it.")]
    [SerializeField, Min(0)] private int capacity = 6;

    public int Capacity => capacity;

    public override ItemSlots FitsSlots => ItemSlots.Backpack;
}
