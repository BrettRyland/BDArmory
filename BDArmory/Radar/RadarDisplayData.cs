using BDArmory.Modules;
using BDArmory.Targeting;
using UnityEngine;

namespace BDArmory.Radar
{
    public struct RadarDisplayData
    {
        public Vessel vessel;
        public Vector2 pingPosition;
        public bool locked;
        public ModuleSensorBase detectedByRadar;
        public TargetSignatureData targetData;
        public float signalPersistTime;
        public float velAngle;
        public int jammedIndex;

        public string Name()
        {
            return vessel ? vessel.vesselName : "null";
        }

        public static RadarDisplayData noTarget
        {
            get
            {
                return
                    new RadarDisplayData(
                        _vessel: null,
                        _pingPosition: Vector2.zero, 
                        _locked: false,
                        _detectedByRadar: null,
                        _targetData: TargetSignatureData.noTarget,
                        _signalPersistTime: -1,
                        _velAngle: 0
                        );
            }
        }

        public RadarDisplayData(Vessel _vessel, Vector2 _pingPosition, bool _locked, ModuleSensorBase _detectedByRadar, TargetSignatureData _targetData, float _signalPersistTime, float _velAngle)
        {
            vessel = _vessel;
            pingPosition = _pingPosition;
            locked = _locked;
            detectedByRadar = _detectedByRadar;
            targetData = _targetData;
            signalPersistTime = _signalPersistTime;
            velAngle = _velAngle;
            jammedIndex = -1;
        }
    }
    public struct IRSTDisplayData
    {
        public Vessel vessel;
        public Vector2 pingPosition;
        public float magnitude;
        public ModuleIRST detectedByIRST;
        public TargetSignatureData targetData;
        public float signalPersistTime;
    }
}
