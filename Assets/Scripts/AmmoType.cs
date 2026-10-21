using UnityEngine;

/// <summary>
/// A kind of ammunition (a data asset: Assets > Create > Game > Ammo Type). It decides how much damage each bullet does and can make
/// the shooting more or less accurate. A weapon (WeaponData) names the ammunition it uses by default; a unit can be given another.
/// </summary>
[CreateAssetMenu(menuName = "Game/Ammo Type", fileName = "Ammo_New")]
public class AmmoType : ScriptableObject
{
    [SerializeField] private string displayName = "Standard";
    [Tooltip("How much health one bullet takes away from whatever it hits (before the weapon's own damage multiplier).")]
    [SerializeField, Min(1)] private int damage = 10;
    [Tooltip("Multiplies the spread of the shooting (the size of the ring): above 1 the bullets scatter more, below 1 less.")]
    [SerializeField, Min(0.1f)] private float spreadMultiplier = 1f;

    public string DisplayName => displayName;
    public int Damage => damage;
    public float SpreadMultiplier => spreadMultiplier;
}
