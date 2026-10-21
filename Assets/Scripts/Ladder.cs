using UnityEngine;

/// <summary>A ladder between a tile on the ground (the foot) and a tile on a roof. Units climb it for LadderCost action points.</summary>
public class Ladder
{
    /// <summary>The ground tile a unit stands on to climb.</summary>
    public Tile foot;

    /// <summary>The roof tile a unit arrives on.</summary>
    public Tile roof;

    /// <summary>On the ground, right in front of the rungs: where the climb starts.</summary>
    public Vector3 climbBottom;

    /// <summary>Straight above climbBottom, at the height of the roof.</summary>
    public Vector3 climbTop;
}
