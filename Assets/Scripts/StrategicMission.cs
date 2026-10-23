using System.Globalization;
using UnityEngine;

/// <summary>A mission that has shown up on the strategic map: what it is and where on the Earth.</summary>
public class StrategicMission
{
    public int number;
    public string title;
    public string description;
    public float latitude;
    public float longitude;

    /// <summary>The place as text: "41.2° с.ш., 12.5° в.д.".</summary>
    public string PlaceText => Geo.Format(latitude, longitude);
}

/// <summary>What the strategic scene hands over to the tactical one.</summary>
public static class GameSession
{
    public const string StrategicSceneName = "StrategicScene";
    public const string TacticalSceneName = "TacticalScene";

    /// <summary>The mission the player chose on the strategic map (null if the tactical scene was started by itself).</summary>
    public static StrategicMission Mission;

    /// <summary>True while a window (the list of soldiers) covers the strategic map: the globe then ignores the mouse and the keys.</summary>
    public static bool ModalOpen;
}

/// <summary>
/// Places on a globe. Latitude goes from -90 (south pole) to 90 (north pole), longitude from -180 to 180 (0 faces +Z, east is to the right
/// when the Earth is looked at from outside). The globe mesh and the Earth texture follow the same rule, so a point given by latitude and
/// longitude lies on the matching place of the picture.
/// </summary>
public static class Geo
{
    /// <summary>The direction from the centre of the globe to the place (a unit vector, in the globe's own coordinates).</summary>
    public static Vector3 Direction(float latitude, float longitude)
    {
        float la = latitude * Mathf.Deg2Rad;
        float lo = longitude * Mathf.Deg2Rad;
        return new Vector3(-Mathf.Cos(la) * Mathf.Sin(lo), Mathf.Sin(la), Mathf.Cos(la) * Mathf.Cos(lo));
    }

    /// <summary>The place a direction from the centre of the globe points to.</summary>
    public static void FromDirection(Vector3 direction, out float latitude, out float longitude)
    {
        direction.Normalize();
        latitude = Mathf.Asin(Mathf.Clamp(direction.y, -1f, 1f)) * Mathf.Rad2Deg;
        longitude = Mathf.Atan2(-direction.x, direction.z) * Mathf.Rad2Deg;
    }

    /// <summary>The place as text: "41.2° с.ш., 12.5° в.д.".</summary>
    public static string Format(float latitude, float longitude)
    {
        string ns = latitude >= 0f ? "с.ш." : "ю.ш.";
        string ew = longitude >= 0f ? "в.д." : "з.д.";
        return Mathf.Abs(latitude).ToString("0.0", CultureInfo.InvariantCulture) + "° " + ns + ", " +
               Mathf.Abs(longitude).ToString("0.0", CultureInfo.InvariantCulture) + "° " + ew;
    }
}
