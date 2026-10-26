using UnityEngine;

namespace BDArmory.Control
{
    public enum AIType { None, GenericAI, PilotAI, SurfaceAI, VTOLAI, OrbitalAI };
    public interface IBDAIControl
    {
        #region PartModule

        Vessel vessel { get; }
        Transform transform { get; }
        Part part { get; }

        #endregion PartModule

        AIType aiType { get; }

        /// <summary>
        /// The weapon manager the AI connects to.
        /// </summary>
        MissileFire WeaponManager { get; }

        bool ActivatePilot();

        void DeactivatePilot();

        void TogglePilot();

        bool pilotEnabled { get; }

        /// <summary>
        /// A function to check if AI could possibly fire at the target with forward looking fixed weapons.
        /// E.g. ships won't be able to fire at airborne targets, hot air baloons might never return true, etc.
        /// </summary>
        /// <param name="target">Vessel to be checked</param>
        /// <returns>true if the AI thinks it might eventually fire on the target with direct fire weapons, false otherwise</returns>
        /// <remarks>Guard mode uses this to check if fixed weapons are viable when selecting weapons.</remarks>
        bool IsValidFixedWeaponTarget(Vessel target);

        /// <summary>
        /// Check if AI is combat-capable.
        /// E.g. dogfight competition mode checks this before starting the competition.
        /// </summary>
        /// <returns>true if AI is ready for combat</returns>
        /// <remarks>Mainly use this to check for obvious user errors such as forgetting to stage engines.</remarks>
        bool CanEngage();

        #region WingCommander

        string currentStatus { get; }

        void ReleaseCommand(bool resetAssignedPosition = true, bool storeCommand = false);

        void CommandFollow(ModuleWingCommander leader, int followerIndex);

        void CommandAG(KSPActionGroup ag);

        void CommandFlyTo(Vector3 gpsCoords);

        void CommandAttack(Vector3 gpsCoords);

        void CommandWingAttack(ModuleWingCommander leader, int followerIndex, Vector3 gpsCoords, float breakRange);

        void CommandTakeOff();

        void CommandFollowWaypoints();

        Vector3d commandGPS { get; }
        PilotCommands currentCommand { get; }
        ModuleWingCommander commandLeader { get; }
        int commandFollowIndex { get; }
        float commandWingAttackRange { get; }

        #endregion WingCommander
    }

    public enum PilotCommands { Free, Attack, Follow, FlyTo, Waypoints, WingAttack }
}
