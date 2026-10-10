using UnityEngine;

/// <summary>
/// Base class for anything that decides what the field looks like.
/// Assign a component derived from it to GridManager.Settings Provider and
/// GridManager will ask it for the settings every time it builds the field.
/// </summary>
public abstract class GridSettingsProvider : MonoBehaviour
{
    public abstract GridSettings GetSettings();
}
