using UnityEngine;

/// <summary>
/// A load-bearing vest, "разгрузка" (a data asset: Assets > Create > Game > Vest). It goes into the Vest slot and has a number of pockets
/// (capacity) for small things (ammunition, grenades, a medkit) that the unit keeps within easy reach.
/// </summary>
[CreateAssetMenu(menuName = "Game/Vest", fileName = "Vest_New")]
public class VestData : ItemData
{
    [Tooltip("How many things fit into its pockets.")]
    [SerializeField, Min(0)] private int capacity = 4;

    public int Capacity => capacity;

    public override ItemSlots FitsSlots => ItemSlots.Vest;
}
