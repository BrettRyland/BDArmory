using UnityEngine;

using BDArmory.Weapons.Missiles;

namespace BDArmory.Guidances
{
    public interface IGuidance
    {
        Vector3d GetDirection(MissileBase missile, Vector3d targetPosition, Vector3d targetVelocity);
    }
}