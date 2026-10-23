using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One of the player's soldiers, as he is kept between scenes: who he is, what he carries and how tough he is. (The tactical scene makes a
/// Unit from it; a soldier is not a unit until then.) What he carries has the same six slots as the inventory of a unit.
/// </summary>
public class SoldierData
{
    public int id;
    public string name;
    public string role;                 // what he is called by his weapon: "Стрелок", "Штурмовик"
    public int maxHealth = 100;
    public int actionPoints = 4;
    public int speed = 1;

    private readonly ItemData[] slots = new ItemData[6];

    /// <summary>What is in the slot (null if nothing).</summary>
    public ItemData Get(InventorySlot slot)
    {
        return slots[(int)slot];
    }

    /// <summary>Puts something into the slot (null empties it). The fit is not checked here: the inventory of the unit does that.</summary>
    public void Set(InventorySlot slot, ItemData item)
    {
        slots[(int)slot] = item;
    }

    /// <summary>The primary weapon (null if there is none).</summary>
    public WeaponData PrimaryWeapon => Get(InventorySlot.Primary) as WeaponData;
}

/// <summary>
/// The player's soldiers. It is static, so it stays when the scene changes (the strategic map shows it, the tactical scene will draw on it);
/// it begins empty and is filled by Generate.
/// </summary>
public static class PlayerRoster
{
    private static readonly List<SoldierData> soldiers = new List<SoldierData>();

    private static readonly string[] firstNames =
    {
        "Алексей", "Дмитрий", "Сергей", "Андрей", "Максим", "Игорь", "Виктор", "Павел",
        "Роман", "Олег", "Артём", "Никита", "Кирилл", "Глеб", "Илья", "Захар"
    };

    private static readonly string[] surnames =
    {
        "Волков", "Соколов", "Орлов", "Громов", "Белов", "Ершов", "Кузнецов", "Морозов",
        "Зайцев", "Лебедев", "Фролов", "Тихонов", "Рябов", "Данилов", "Лавров", "Яковлев"
    };

    /// <summary>All the soldiers, in the order of the list.</summary>
    public static IReadOnlyList<SoldierData> Soldiers => soldiers;

    public static int Count => soldiers.Count;

    /// <summary>Takes everybody away.</summary>
    public static void Clear()
    {
        soldiers.Clear();
    }

    /// <summary>
    /// Makes the soldiers (the old ones are gone): the first 'riflemen' carry the rifle, the next 'shotgunners' the shotgun, each with
    /// a name nobody else has. 'template' gives the health, action points and speed (the stats of the unit the soldier becomes).
    /// </summary>
    public static void Generate(int riflemen, WeaponData rifle, int shotgunners, WeaponData shotgun, Unit template)
    {
        soldiers.Clear();
        HashSet<string> used = new HashSet<string>();

        for (int i = 0; i < riflemen + shotgunners; i++)
        {
            bool isRifleman = i < riflemen;
            SoldierData s = new SoldierData
            {
                id = i + 1,
                name = NewName(used),
                role = isRifleman ? "Стрелок" : "Штурмовик",
                maxHealth = template != null ? template.MaxHealth : 100,
                actionPoints = template != null ? template.MaxActionPoints : 4,
                speed = template != null ? template.Speed : 1
            };
            s.Set(InventorySlot.Primary, isRifleman ? rifle : shotgun);
            soldiers.Add(s);
        }
    }

    private static string NewName(HashSet<string> used)
    {
        for (int attempt = 0; attempt < 200; attempt++)
        {
            string n = firstNames[Random.Range(0, firstNames.Length)] + " " + surnames[Random.Range(0, surnames.Length)];
            if (used.Add(n))
                return n;
        }
        return "Боец " + (used.Count + 1);
    }
}
