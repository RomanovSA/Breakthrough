using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One way of firing a weapon: a single shot, a burst... It says how many bullets leave the weapon at once, what that costs in action
/// points, how much less (or more) accurate it is than the weapon's normal shooting, and how fast the bullets of a burst follow each other.
/// </summary>
[System.Serializable]
public class FireModeData
{
    [Tooltip("The name shown on the button.")]
    public string displayName = "Одиночный";

    [Tooltip("How many bullets one use of this mode fires (1 = a single shot, 3 = a burst of three).")]
    [Min(1)] public int shots = 1;

    [Tooltip("Action points one use of this mode costs.")]
    [Min(0)] public int apCost = 2;

    [Tooltip("Multiplies the spread (the size of the ring): above 1 this mode is less accurate than the weapon's normal shooting.")]
    [Min(0.1f)] public float spreadMultiplier = 1f;

    [Tooltip("Seconds between the bullets of a burst.")]
    [Min(0.02f)] public float shotInterval = 0.1f;
}

/// <summary>
/// A weapon (a data asset: Assets > Create > Game > Weapon). It holds what makes one weapon different from another: how accurate it is
/// (the spread of the bullets, in degrees: the ring around the target is the distance times the tangent of it), how hard it hits (the
/// damage of the ammunition times this weapon's damage multiplier), how fast its bullets fly, which ammunition it uses, and the ways of
/// firing it (single shot, burst...), each with its own cost and accuracy. A unit uses it through its Weapon field (Unit).
/// </summary>
[CreateAssetMenu(menuName = "Game/Weapon", fileName = "Weapon_New")]
public class WeaponData : ScriptableObject
{
    [SerializeField] private string displayName = "Weapon";
    [Tooltip("Accuracy: how far, in degrees, the bullets scatter around the aim. Smaller is more accurate.")]
    [SerializeField, Range(0.1f, 20f)] private float spreadDegrees = 3f;
    [Tooltip("Multiplies the damage of the ammunition (1 = as the ammunition says).")]
    [SerializeField, Min(0.1f)] private float damageMultiplier = 1f;
    [Tooltip("How fast the bullets fly, world units per second (a tile is 1).")]
    [SerializeField, Min(1f)] private float bulletSpeed = 40f;
    [Tooltip("The ammunition the weapon uses unless the unit has been given another.")]
    [SerializeField] private AmmoType defaultAmmo;
    [Tooltip("The ways of firing. The first is the one a unit starts with.")]
    [SerializeField] private List<FireModeData> fireModes = new List<FireModeData> { new FireModeData() };

    public string DisplayName => displayName;
    public float SpreadDegrees => spreadDegrees;
    public float DamageMultiplier => damageMultiplier;
    public float BulletSpeed => bulletSpeed;
    public AmmoType DefaultAmmo => defaultAmmo;
    public IReadOnlyList<FireModeData> FireModes => fireModes;

    /// <summary>The damage of one bullet of this weapon with the given ammunition (the weapon's own ammunition if none is given).</summary>
    public int DamagePerBullet(AmmoType ammo)
    {
        AmmoType used = ammo != null ? ammo : defaultAmmo;
        int baseDamage = used != null ? used.Damage : 10;
        return Mathf.Max(1, Mathf.RoundToInt(baseDamage * damageMultiplier));
    }
}
