using System;

using BDArmory.Utils;

namespace BDArmory.Targeting
{
    [Serializable]
    public struct GPSTargetInfo
    {
        public Vector3d gpsCoordinates;

        public string name;

        [NonSerialized]
        public Vessel gpsVessel;

        public Vector3d worldPos
        {
            get
            {
                if (!FlightGlobals.currentMainBody)
                    return Vector3d.zero;

                return VectorUtils.GetWorldSurfacePosition(gpsCoordinates, FlightGlobals.currentMainBody);
            }
        }

        public GPSTargetInfo(Vector3d coords, string name, Vessel vessel = null)
        {
            gpsVessel = vessel;
            gpsCoordinates = coords;
            this.name = name;
        }

        public bool EqualsTarget(GPSTargetInfo other)
        {
            return name == other.name && gpsCoordinates == other.gpsCoordinates && gpsVessel == other.gpsVessel;
        }
    }
}
